// 片段曲线（照 Timeline Curves 视图）：通道参数列表、曲线绘制、关键帧与切线编辑、参考曲线叠加。

import { useTimelineContext } from 'dnd-timeline';
import { useContext, useEffect, useRef, useState, type KeyboardEvent, type MouseEvent as ReactMouseEvent, type PointerEvent, type ReactNode } from 'react';
import type { ClipCurveChannel, SkillElements } from '../../api/authoring';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { clipCurves, clipReferenceSource, defaultChannelCurve, requestReferenceCurves, useReferenceCurves, type ReferenceEntry } from '../../state/authoringTimeline';
import { useWorkbench } from '../../state/store';
import type { ClipInstance, Span, TrackRow } from '../../state/timelineModel';
import { curveWithKey, curveWithoutKey, evaluateCurve } from '../../state/curveSplit';
import {
  breakKeys, isBroken, keyTime, keyValue, moveKeys, removeKeys, setTangent, setTangentKind, smoothKeys, tangentKind, tangentOf,
  type TangentKind, type TangentSide,
} from '../../state/curveEdit';
import { numberOf, type JsonObject } from '../../state/skillJson';
import { Button, ContextMenu, NumberInput, Tip, type MenuEntry } from '../../ui/controls';
import { KeyIcon } from './icons';
import {
  CurveViewContext, PreviewClockContext, SnapContext, curveMaxHeight, curveParamHeight, curveParamPitch, curveParamTop, trackHeight, usePreviewSelect,
} from './timelineShared';
import styles from './timeline.module.css';

// 片段两端与同轨片段的混合（Mix）秒数。
export interface ClipMix { in: number; out: number }
export const noMix: ClipMix = { in: 0, out: 0 };

// 通道曲线在时间轴上的区间（照 TimelineRuntimeClipSampler.Weight）：Weight 覆盖整段；Ease In / Ease Out 只在
// 生效的缓动区间内取值，有混合时为混合秒数，否则为自身淡入淡出（不超过片段长减去另一端的混合）。
function channelSpan(role: ClipCurveChannel['role'], clip: ClipInstance, mix: ClipMix): Span | null {
  const duration = clip.end - clip.start;
  if (role === 'Weight') return { start: clip.start, end: clip.end };
  const ease = role === 'EaseIn'
    ? mix.in > 0 ? mix.in : Math.min(clip.clip.easeInSeconds ?? 0, duration - mix.out)
    : mix.out > 0 ? mix.out : Math.min(clip.clip.easeOutSeconds ?? 0, duration - mix.in);
  if (!(ease > 0)) return null;
  return role === 'EaseIn' ? { start: clip.start, end: clip.start + ease } : { start: clip.end - ease, end: clip.end };
}

const roundCurve = (value: number) => Math.round(value * 1e4) / 1e4;
const clamp01 = (value: number) => Math.max(0, Math.min(1, value));
const curveDiamond = (x: number, y: number, r: number) => `M${x} ${y - r}L${x + r} ${y}L${x} ${y + r}L${x - r} ${y}Z`;
const curveKeys = (curve: JsonObject | null) => curve ? curve.keys as JsonObject[] : [];
// 参考曲线照 Unity 曲线编辑器按分量序号着色：x 红、y 绿、z 蓝，第四条黄。
const referenceColors = ['#e86a6a', '#7cc96f', '#5f9df0', '#e3c04f'];

// 片段的一个曲线参数（照 BtsmtlTimelineParameterBinding），在 span 上按归一化时间 0–1 取值。
// 通道参数是片段自身的正式曲线通道，可编辑；参考参数是所引用源资产的曲线（BtsmtlTimelineReferenceParameterBinding），
// 只读，片段时间 τ 取源时间 源起点 + τ，超过源终点保持终值（照 MotionCurveClip.UpdateSourceDisplayCurve）。
interface CurveParameter {
  id: string; name: string; color: string; span: Span | null;
  times: number[]; value(t: number): number;
  channel: ClipCurveChannel | null; curve: JsonObject | null; reference: string | null;
}

function clipParameters(elements: SkillElements, documentId: string, clip: ClipInstance, mix: ClipMix,
  source: ReturnType<typeof clipReferenceSource>, entry: ReferenceEntry | undefined): CurveParameter[] {
  const channels = clipCurves(elements, documentId, clip.clip.id).map(({ channel, curve }): CurveParameter => ({
    id: channel.member, name: channel.name, color: channel.color, span: channelSpan(channel.role, clip, mix),
    times: curveKeys(curve).map(key => numberOf(key.time)), value: t => curve ? evaluateCurve(curve, t) : 0,
    channel, curve, reference: null,
  }));
  if (!source || !entry || 'failed' in entry) return channels;
  const length = clip.end - clip.start;
  const toSource = (t: number) => Math.min(source.sourceStart + t * length, source.sourceEnd);
  return [...channels, ...entry.curves.map((item, index): CurveParameter => {
    const curve = { keys: item.keys } as unknown as JsonObject;
    return {
      id: item.id, name: item.name, color: referenceColors[index % referenceColors.length], span: { start: clip.start, end: clip.end },
      times: item.keys.filter(key => key.time >= source.sourceStart && key.time <= source.sourceEnd)
        .map(key => (key.time - source.sourceStart) / length),
      value: t => evaluateCurve(curve, toSource(t)), channel: null, curve: null, reference: entry.label,
    };
  })];
}

// 展开曲线的轨道只看本轨选中的片段（照 IEmbeddedTimelineTrackBinding.SelectedClip）。
function useCurveClip(row: TrackRow, elements: SkillElements, mixes: Map<string, ClipMix>) {
  useAuthoring(state => authoringDocument(state).version);
  const selection = useWorkbench(state => state.selection?.kind === 'clip' ? state.selection.id : null);
  const view = useContext(CurveViewContext);
  const clip = row.clips.find(item => item.selectId === selection) ?? null;
  const source = clip ? clipReferenceSource(elements, row.timeline.documentId, clip.clip.id) : null;
  const entry = useReferenceCurves(state => source ? state[source.identity] : undefined);
  useEffect(() => { if (source) requestReferenceCurves(source.identity); }, [source?.identity]);
  const parameters = clip ? clipParameters(elements, row.timeline.documentId, clip, mixes.get(clip.key) ?? noMix, source, entry) : [];
  const inspected = view.inspected?.track === row.id ? parameters.find(item => item.id === view.inspected!.parameter) ?? null : null;
  const failure = entry && 'failed' in entry ? entry.failed : null;
  return { clip, parameters, inspected, view, failure };
}

// 展开曲线的轨道头下部（照 TrackEditorGUI.DrawParametersInfoGUI）：本轨选中片段的各曲线参数，单击查看完整曲线，再次单击收起。
// 查看时下方是上一关键帧、在游标处加或删关键帧（参考曲线只读）、下一关键帧与游标处的值；拖动底边加高曲线区。
export function CurveParameters({ row, elements, mixes, author, height }: {
  row: TrackRow; elements: SkillElements; mixes: Map<string, ClipMix>; author: boolean; height: number;
}) {
  const clock = useContext(PreviewClockContext);
  const tick = useContext(SnapContext) ?? 1 / 60;
  const { clip, parameters, inspected, view, failure } = useCurveClip(row, elements, mixes);
  const cursor = usePreviewSelect(clock, seconds => seconds === null ? null : row.segment.origin + seconds);
  const drag = useRef<{ y: number; height: number } | null>(null);
  if (!clip) return <div className={styles.curveParams}><span className={styles.curveNote}>未选中本轨片段</span></div>;
  const write = (channel: ClipCurveChannel, curve: JsonObject) =>
    authoringActions.setClipValues(row.timeline.documentId, clip.clip.id, [{ keys: [channel.member], value: curve }]);
  let controls: ReactNode = null;
  if (inspected) {
    const { span, curve, channel } = inspected;
    if (channel && !curve) controls = <div className={styles.curveKeys}>{author
      ? <Button variant="ghost" onClick={() => write(channel, defaultChannelCurve(channel))}>按默认直线创建</Button>
      : <span className={styles.curveNote}>未设置，运行时不起作用</span>}</div>;
    else if (!span) controls = <div className={styles.curveKeys}><span className={styles.curveNote}>片段没有淡入淡出或混合，不取值</span></div>;
    else {
      // 游标在参数区间里的归一化时间；离关键帧不到半个 tick 视为在关键帧上。
      const length = span.end - span.start;
      const t = cursor === null ? null : (cursor - span.start) / length;
      const tolerance = tick / 2 / length;
      const atKey = t !== null && inspected.times.some(time => Math.abs(time - t) <= tolerance);
      const previous = t === null ? undefined : inspected.times.filter(time => time < t - tolerance).at(-1);
      const next = t === null ? undefined : inspected.times.find(time => time > t + tolerance);
      const jump = (time: number) => view.seek(span.start + time * length);
      const inside = t !== null && t >= -tolerance && t <= 1 + tolerance;
      // 单选一个关键帧时可直接输入它的时间（参数区间内的秒数）与值（照 Unity Edit Key）。
      const single = author && channel && curve && view.keys.length === 1 ? curveKeys(curve)[view.keys[0]] : undefined;
      const place = (time: number, value: number) => {
        const result = moveKeys(curve!, view.keys, () => [roundCurve(clamp01(time)), roundCurve(Math.max(channel!.min, Math.min(channel!.max, value)))]);
        if (!result) return;
        write(channel!, result.curve);
        view.selectKeys(result.indices);
      };
      controls = <>
        <div className={styles.curveKeys}>
          <Tip content="上一关键帧">
            <button className={styles.curveKeyButton} disabled={previous === undefined} onClick={() => jump(previous!)}>
              <KeyIcon kind="previous" />
            </button>
          </Tip>
          <Tip content={!channel ? `参考曲线只读（${inspected.reference}）` : atKey ? '删除游标处的关键帧' : '在游标处添加关键帧'}>
            <button className={styles.curveKeyButton} data-on={atKey || undefined} disabled={!author || !inside || !channel}
              onClick={() => {
                view.selectKeys([]);
                write(channel!, atKey ? curveWithoutKey(curve!, t!, tolerance) : curveWithKey(curve!, clamp01(t!)));
              }}>
              <KeyIcon kind="key" />
            </button>
          </Tip>
          <Tip content="下一关键帧">
            <button className={styles.curveKeyButton} disabled={next === undefined} onClick={() => jump(next!)}>
              <KeyIcon kind="next" />
            </button>
          </Tip>
          <Tip content="游标处的值">
            <span className={styles.curveValue}>{t === null ? '—' : inspected.value(clamp01(t)).toFixed(3)}</span>
          </Tip>
        </div>
        {single && <div className={styles.curveKeys}>
          <span className={styles.curveKeyLabel}>时间</span>
          <NumberInput label="关键帧时间（秒）" value={roundCurve(keyTime(single) * length)} min={0} max={length} step={tick}
            onChange={value => { if (value !== null) place(value / length, keyValue(single)); }} />
          <span className={styles.curveKeyLabel}>值</span>
          <NumberInput label="关键帧值" value={keyValue(single)} min={channel!.min} max={channel!.max} step={0.01}
            onChange={value => { if (value !== null) place(keyTime(single), value); }} />
        </div>}
      </>;
    }
  }
  return (
    <div className={styles.curveParams}>
      {parameters.map(parameter => (
        <Tip key={parameter.id} name={parameter.name} content={parameter.reference && `参考：${parameter.reference}，只读`}>
          <button className={styles.curveParam} aria-pressed={parameter === inspected} style={{ ['--cc' as string]: parameter.color }}
            onClick={() => view.inspect(row.id, parameter === inspected ? null : parameter.id)}>
            <span className={styles.curveParamName} data-name>{parameter.name}</span>
            {parameter.reference ? <small>参考</small> : !parameter.curve ? <small>未设置</small> : !parameter.span ? <small>无缓动</small> : null}
          </button>
        </Tip>
      ))}
      {failure && <Tip content={failure}><span className={styles.curveNote}>参考曲线读取失败</span></Tip>}
      {inspected && controls}
      {inspected && <Tip content="拖动改变曲线区高度"><div className={styles.curveResize}
        onPointerDown={event => {
          if (event.button !== 0) return;
          event.currentTarget.setPointerCapture(event.pointerId);
          drag.current = { y: event.clientY, height };
        }}
        onPointerMove={event => { if (drag.current) view.resize(row.id, Math.min(curveMaxHeight, drag.current.height + event.clientY - drag.current.y)); }}
        onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} /></Tip>}
    </div>
  );
}

// 纵向范围 min–max 含留白；low–high 为曲线实际取值的上下端。
interface CurveScale { min: number; max: number; low: number; high: number }
// 曲线编辑的拖动草稿：移动所选关键帧、拖切线手柄（预览曲线与原曲线、纵向范围冻结），或框选关键帧（曲线区像素坐标）。
type CurveDraft =
  | { kind: 'move'; from: { t: number; v: number }; base: JsonObject; preview: JsonObject; indices: number[]; moved: number[]; scale: CurveScale }
  | { kind: 'tangent'; index: number; side: TangentSide; base: JsonObject; preview: JsonObject; scale: CurveScale }
  | { kind: 'box'; x0: number; y0: number; x1: number; y1: number; add: boolean };
const tangentNames: Record<TangentKind, string> = { free: 'Free', linear: 'Linear', constant: 'Constant' };
const tangentHandle = 36;

// 曲线的纵向范围（照 Slate FrameAllCurvesOf）：曲线实际取值（通道参数并上通道取值域），上下留白。
function curveScale(parameter: CurveParameter): CurveScale {
  let low = Infinity, high = -Infinity;
  for (const t of [...Array.from({ length: 97 }, (_, index) => index / 96), ...parameter.times.map(clamp01)]) {
    const value = parameter.value(t);
    low = Math.min(low, value);
    high = Math.max(high, value);
  }
  let min = Math.min(low, parameter.channel?.min ?? low), max = Math.max(high, parameter.channel?.max ?? high);
  if (!(max > min)) { min -= 0.5; max += 0.5; }
  const pad = (max - min) * 0.08;
  return { min: min - pad, max: max + pad, low, high };
}

const scaleLabel = (value: number) => Number(value.toPrecision(4)).toString();

// 片段下方的参数区（照 TrackEditorGUI.DrawClipCurves）：只在选中片段的时间范围内画。未查看参数时每个参数一行，
// 菱形为关键帧，单击一行查看该参数；查看时上方一行关键帧，下面是完整曲线。
// 作者模式下通道参数照 Unity 曲线编辑器编辑：单击选中、Shift 加选、空白处拖动框选，拖动所选关键帧改时间与值
// （时间按 tick 吸附并限在 0–1，值限在通道取值域，取到万分位），选中关键帧显示切线手柄，双击空白处添加关键帧，
// 右键关键帧删除或设切线模式，Delete 删除所选；参考曲线只读。
export function ClipCurves({ row, elements, mixes, author, height }: {
  row: TrackRow; elements: SkillElements; mixes: Map<string, ClipMix>; author: boolean; height: number;
}) {
  const { range, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const step = useContext(SnapContext);
  const { clip, parameters, inspected, view } = useCurveClip(row, elements, mixes);
  const [draft, setDraft] = useState<CurveDraft | null>(null);
  const [menu, setMenu] = useState<{ key: number | null; t: number }>({ key: null, t: 0 });
  const svg = useRef<SVGSVGElement>(null);
  const inspecting = inspected !== null;
  useEffect(() => { if (inspecting) view.selectKeys([]); }, [clip?.key]);
  const x = (seconds: number) => valueToPixels(seconds - range.start);
  if (!clip) return (
    <div className={styles.clipCurves} style={{ top: trackHeight, height }}>
      <span className={styles.curveHint}>选中本轨片段后在这里查看它的曲线</span>
    </div>
  );
  const left = x(clip.start), width = Math.max(1, x(clip.end) - left);
  const spanX = (span: Span, t: number) => x(span.start + t * (span.end - span.start));

  if (!inspected) return (
    <svg className={styles.clipCurves} style={{ top: trackHeight, height }}>
      <rect className={styles.curveClipArea} x={left} y={1} width={width} height={height - 4} />
      {parameters.map((parameter, index) => {
        const y = curveParamTop + index * curveParamPitch;
        const span = parameter.span;
        return (
          <Tip key={parameter.id} name={parameter.name}>
            <g className={styles.curveRow} style={{ ['--cc' as string]: parameter.color }}
              data-unset={(parameter.channel && !parameter.curve) || !span || undefined}
              onPointerDown={event => { if (event.button === 0) view.inspect(row.id, parameter.id); }}>
              <rect className={styles.curveRowBand} x={left} y={y} width={width} height={curveParamHeight} />
              {span && parameter.times.map((time, key) => (
                <path key={key} className={styles.curveDope} d={curveDiamond(spanX(span, clamp01(time)), y + curveParamHeight / 2, 4)} />
              ))}
            </g>
          </Tip>
        );
      })}
    </svg>
  );

  const { span, curve, channel } = inspected;
  const plotTop = 26, plotBottom = height - 6;
  const scale = draft && draft.kind !== 'box' ? draft.scale : curveScale(inspected);
  const pxPerV = (plotBottom - plotTop) / (scale.max - scale.min);
  const y = (value: number) => plotTop + (scale.max - value) * pxPerV;
  const editable = author && channel !== null && curve !== null && span !== null;
  const shown = draft && draft.kind !== 'box' ? draft.preview : curve;
  const keys = curveKeys(shown);
  const picked = draft?.kind === 'move' ? draft.moved : view.keys.filter(index => index < keys.length);
  const valueAt = shown ? (t: number) => evaluateCurve(shown, t) : inspected.value;
  const length = span ? span.end - span.start : 1;
  const pxPerT = span ? spanX(span, 1) - spanX(span, 0) : 1;
  const snapT = (t: number) => clamp01(step ? Math.round(t * length / step) * step / length : t);
  const clampV = (v: number) => channel ? Math.max(channel.min, Math.min(channel.max, v)) : v;
  const at = (event: { clientX: number; clientY: number }) => {
    const box = svg.current!.getBoundingClientRect();
    const px = event.clientX - box.left, py = event.clientY - box.top;
    return { px, py, t: span ? (getValueFromScreenX(event.clientX) - span.start) / length : 0, v: scale.max - (py - plotTop) / pxPerV };
  };
  const write = (next: JsonObject) => authoringActions.setClipValues(row.timeline.documentId, clip.clip.id, [{ keys: [channel!.member], value: next }]);
  const commit = (result: { curve: JsonObject; indices: number[] } | null) => {
    if (!result) return;
    write(result.curve);
    view.selectKeys(result.indices);
  };
  const addAt = (t: number) => {
    if (!curve || t < 0 || t > 1) return;
    const time = roundCurve(snapT(t));
    if (keys.some(key => Math.abs(keyTime(key) - time) < 1e-6)) return;
    const next = curveWithKey(curve, time);
    write(next);
    view.selectKeys([curveKeys(next).findIndex(key => keyTime(key) === time)]);
  };
  const remove = (indices: number[]) => {
    if (!curve || !indices.length) return;
    write(removeKeys(curve, indices));
    view.selectKeys([]);
  };
  const capture = (event: PointerEvent<Element>) => {
    event.stopPropagation();
    svg.current!.focus();
    svg.current!.setPointerCapture(event.pointerId);
  };
  const down = (event: PointerEvent<SVGSVGElement>) => {
    if (event.button !== 0) return;
    event.stopPropagation();
    if (!editable) return;
    capture(event);
    const p = at(event);
    setDraft({ kind: 'box', x0: p.px, y0: p.py, x1: p.px, y1: p.py, add: event.shiftKey });
  };
  const grabKey = (event: PointerEvent<SVGElement>, index: number) => {
    if (event.button !== 0 || !editable) return;
    capture(event);
    if (event.shiftKey) {
      view.selectKeys(picked.includes(index) ? picked.filter(item => item !== index) : [...picked, index]);
      return;
    }
    const indices = picked.includes(index) ? picked : [index];
    view.selectKeys(indices);
    const p = at(event);
    setDraft({ kind: 'move', from: { t: p.t, v: p.v }, base: curve!, preview: curve!, indices, moved: indices, scale });
  };
  const grabTangent = (event: PointerEvent<SVGElement>, index: number, side: TangentSide) => {
    if (event.button !== 0) return;
    capture(event);
    setDraft({ kind: 'tangent', index, side, base: curve!, preview: curve!, scale });
  };
  const move = (event: PointerEvent<SVGSVGElement>) => {
    if (!draft) return;
    const p = at(event);
    if (draft.kind === 'box') { setDraft({ ...draft, x1: p.px, y1: p.py }); return; }
    if (!span) return;
    if (draft.kind === 'move') {
      const dt = p.t - draft.from.t, dv = p.v - draft.from.v;
      const result = moveKeys(draft.base, draft.indices, (t, v) => [roundCurve(snapT(t + dt)), roundCurve(clampV(v + dv))]);
      if (result) setDraft({ ...draft, preview: result.curve, moved: result.indices });
      return;
    }
    // 切线为手柄方向在曲线坐标里的斜率（值 / 归一化时间）；左手柄只取向左、右手柄只取向右。
    const key = curveKeys(draft.base)[draft.index];
    const dx = (p.px - spanX(span, keyTime(key))) / pxPerT;
    const run = draft.side === 'right' ? Math.max(dx, 1e-4) : Math.min(dx, -1e-4);
    const rise = (y(keyValue(key)) - p.py) / pxPerV;
    setDraft({ ...draft, preview: setTangent(draft.base, draft.index, draft.side, roundCurve(rise / run)).curve });
  };
  const release = () => {
    if (!draft) return;
    setDraft(null);
    if (draft.kind === 'box') {
      const [x0, x1] = [Math.min(draft.x0, draft.x1), Math.max(draft.x0, draft.x1)];
      const [y0, y1] = [Math.min(draft.y0, draft.y1), Math.max(draft.y0, draft.y1)];
      const inside = span && x1 - x0 + y1 - y0 > 3 ? keys.flatMap((key, index) => {
        const kx = spanX(span, keyTime(key)), ky = y(keyValue(key));
        return kx >= x0 && kx <= x1 && ky >= y0 && ky <= y1 ? [index] : [];
      }) : [];
      view.selectKeys(draft.add ? [...new Set([...picked, ...inside])] : inside);
      return;
    }
    if (draft.preview === draft.base) return;
    write(draft.preview);
    view.selectKeys(draft.kind === 'move' ? draft.moved : [draft.index]);
  };
  const keyDown = (event: KeyboardEvent<SVGSVGElement>) => {
    if ((event.key !== 'Delete' && event.key !== 'Backspace') || !editable || !picked.length) return;
    event.preventDefault();
    event.stopPropagation();
    remove(picked);
  };
  // 右键关键帧时先选中它（已在所选中则作用于全部所选），菜单随所选给出。
  const openMenu = (event: ReactMouseEvent<SVGSVGElement>) => {
    event.stopPropagation();
    const hit = (event.target as Element).closest('[data-key]');
    const key = hit ? Number(hit.getAttribute('data-key')) : null;
    if (key !== null && !picked.includes(key)) view.selectKeys([key]);
    setMenu({ key, t: at(event).t });
  };
  const menuEntries = (): MenuEntry[] => {
    const note = (label: string): MenuEntry[] => [{ label, disabled: true, onSelect: () => {} }];
    if (!author) return note('进入作者模式后可编辑');
    if (!channel) return note(`参考曲线只读（${inspected.reference}）`);
    if (!editable) return note('这条曲线现在不取值，不能编辑');
    const add: MenuEntry = { label: '在此处添加关键帧', disabled: menu.t < 0 || menu.t > 1, onSelect: () => addAt(menu.t) };
    if (menu.key === null) return [add];
    const targets = picked.includes(menu.key) ? picked : [menu.key];
    const items = targets.map(index => keys[index]).filter(Boolean);
    const all = (test: (key: JsonObject) => boolean) => items.length > 0 && items.every(test);
    const sideEntries = (label: string, sides: TangentSide[]): MenuEntry[] => [
      { kind: 'label', label },
      ...(['free', 'linear', 'constant'] as const).map((kind): MenuEntry => ({
        kind: 'radio', label: tangentNames[kind], checked: all(key => sides.every(side => tangentKind(key, side) === kind)),
        onSelect: () => commit(setTangentKind(curve!, targets, sides, kind)),
      })),
    ];
    return [
      { label: targets.length > 1 ? `删除 ${targets.length} 个关键帧` : '删除关键帧', onSelect: () => remove(targets) },
      { kind: 'separator' },
      {
        kind: 'radio', label: 'Free Smooth', onSelect: () => commit(smoothKeys(curve!, targets, false)),
        checked: all(key => !isBroken(key) && tangentKind(key, 'left') === 'free' && tangentKind(key, 'right') === 'free'),
      },
      { label: 'Flat', onSelect: () => commit(smoothKeys(curve!, targets, true)) },
      { kind: 'check', label: 'Broken', checked: all(isBroken), onChange: checked => commit(breakKeys(curve!, targets, checked)) },
      { kind: 'separator' },
      ...sideEntries('左切线', ['left']), ...sideEntries('右切线', ['right']), ...sideEntries('两侧切线', ['left', 'right']),
    ];
  };
  const samples = span ? Math.max(16, Math.min(240, Math.round(pxPerT / 3))) : 0;
  const points = span && (curve || !channel) ? Array.from({ length: samples + 1 }, (_, index) => index / samples)
    .map(t => `${spanX(span, t)},${y(valueAt(t))}`).join(' ') : null;
  // 参考线：通道参数为取值域两端，参考曲线为实际取值的上下端。
  const guides = channel ? [channel.max, channel.min] : scale.high > scale.low ? [scale.high, scale.low] : [scale.high];
  const times = editable ? keys.map(keyTime) : inspected.times;
  // 所选关键帧的切线手柄：沿切线方向固定长度；首帧没有左手柄、末帧没有右手柄，阶跃（无穷）切线不画。
  const handles = editable ? picked.flatMap(index => {
    const key = keys[index];
    const kx = spanX(span, keyTime(key)), ky = y(keyValue(key));
    return (['left', 'right'] as const).filter(side => side === 'left' ? index > 0 : index < keys.length - 1).flatMap(side => {
      const slope = tangentOf(key, side);
      if (!Number.isFinite(slope)) return [];
      const dy = -slope * pxPerV, norm = Math.hypot(pxPerT, dy) || 1, sign = side === 'left' ? -1 : 1;
      return [{ index, side, kx, ky, hx: kx + sign * pxPerT / norm * tangentHandle, hy: ky + sign * dy / norm * tangentHandle, slope }];
    });
  }) : [];
  return (
    <ContextMenu entries={menuEntries()}>
      <svg ref={svg} className={styles.clipCurves} tabIndex={-1} style={{ top: trackHeight, height, ['--cc' as string]: inspected.color }}
        onPointerDown={down} onPointerMove={move} onPointerUp={release} onPointerCancel={() => setDraft(null)}
        onDoubleClick={event => { if (editable && !(event.target as Element).closest('[data-key], [data-handle]')) addAt(at(event).t); }}
        onKeyDown={keyDown} onContextMenu={openMenu}>
        <rect className={styles.curveClipArea} x={left} y={1} width={width} height={height - 4} />
        <rect className={styles.curveRowBand} x={left} y={4} width={width} height={16} />
        {span && times.map((time, key) => <path key={key} className={styles.curveDope} data-active={(editable && picked.includes(key)) || undefined}
          d={curveDiamond(spanX(span, clamp01(time)), 12, 4)} />)}
        <line className={styles.curvePlotEdge} x1={left} x2={left + width} y1={plotTop - 3} y2={plotTop - 3} />
        {guides.map((value, index) => (
          <g key={index}>
            <line className={styles.curveGuide} x1={left} x2={left + width} y1={y(value)} y2={y(value)} />
            <text className={styles.curveScaleText} x={left + 4} y={y(value) - 3}>{scaleLabel(value)}</text>
          </g>
        ))}
        {points && <polyline className={styles.curvePlotLine} points={points} />}
        {handles.map(handle => (
          <g key={`${handle.index}-${handle.side}`} className={styles.curveHandle} data-handle="">
            <line x1={handle.kx} y1={handle.ky} x2={handle.hx} y2={handle.hy} />
            <Tip content={`${handle.side === 'left' ? '左' : '右'}切线 ${handle.slope.toFixed(3)} · 拖动修改`}>
              <circle cx={handle.hx} cy={handle.hy} r={3} onPointerDown={event => grabTangent(event, handle.index, handle.side)} />
            </Tip>
          </g>
        ))}
        {editable && keys.map((key, index) => (
          <Tip key={index} content={`${inspected.name} 关键帧 ${index} · ${(keyTime(key) * length).toFixed(3)} s, ${keyValue(key).toFixed(3)} · 拖动修改，右键设切线`}>
            <circle data-key={index} className={styles.curvePlotKey} cx={spanX(span, clamp01(keyTime(key)))} cy={y(keyValue(key))} r={3.5}
              data-active={picked.includes(index) || undefined} onPointerDown={event => grabKey(event, index)} />
          </Tip>
        ))}
        {draft?.kind === 'box' && <rect className={styles.curveBox} x={Math.min(draft.x0, draft.x1)} y={Math.min(draft.y0, draft.y1)}
          width={Math.abs(draft.x1 - draft.x0)} height={Math.abs(draft.y1 - draft.y0)} />}
        {editable && <text className={styles.curveScaleText} x={left + width - 4} y={plotTop + 10} textAnchor="end">
          双击添加 · 拖动框选 · 右键切线 · Delete 删除
        </text>}
        {inspected.reference && <text className={styles.curveScaleText} x={left + width - 4} y={plotBottom - 2} textAnchor="end">
          参考：{inspected.reference}（只读）
        </text>}
        {channel && !curve ? <text className={styles.curveScaleText} x={left + 4} y={plotTop + 12}>未设置，运行时不起作用</text>
          : !span ? <text className={styles.curveScaleText} x={left + 4} y={plotTop + 12}>片段没有淡入淡出或混合，这条缓动曲线不取值</text> : null}
      </svg>
    </ContextMenu>
  );
}

