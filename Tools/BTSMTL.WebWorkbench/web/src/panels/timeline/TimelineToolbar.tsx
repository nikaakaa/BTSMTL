import { useTimelineContext, type Range } from 'dnd-timeline';
import { Fragment, useContext, useRef, useState, type PointerEvent, type ReactNode } from 'react';
import type { SkillElements } from '../../api/authoring';
import { authoringActions, authoringDocument, useAuthoring, useDocumentObservationNotice, useInputContext, useInputSchemes } from '../../state/authoring';
import { inputAuthoring } from '../../state/authoringInputs';
import { consoleActions, selectionActions, useWorkbench, type GraphNavigationOrigin } from '../../state/store';
import { trackSelectId } from '../../state/timelineModel';
import { callLabel, graphName, parentCallNotice, type RecordedGraphCall } from '../../state/graphModel';
import { projectActions } from '../../state/project';
import { useInputObservation } from '../../state/inputModel';
import { PanelBar } from '../../ui/blocks';
import { Button, MenuButton, Select, TextInput, Tip, type MenuEntry } from '../../ui/controls';
import { AddIcon, CrumbArrow, SelectorIcon, SettingsIcon } from '../../ui/icons';
import { TimelineAssetIcon } from './icons';
import {
  PreviewClockContext, documentPlayback, minSpan, observationKey, quickLocalSeconds, timeText, usePreviewSelect,
  type QuickPlaybackChoice, type TimelineObservation,
} from './timelineShared';
import styles from './timeline.module.css';

// ── 窗口工具条（照 Timeline 窗口顶行）：时间框、Timeline 选择器与面包屑、本次实际调用，右侧齿轮设置；播放与保存在顶栏 ──

export interface Subject {
  // 选择器（照 Sequence Selector）：技能里的 Timeline 或路线所属的技能根；只有一项可选时为 null。
  selector: { label: string; entries: MenuEntry[] } | null;
  // 面包屑：前面的层级可点击返回，最后一项是所看的内容。
  crumbs: { label: ReactNode; onSelect?(): void }[];
}

// 在 Timeline 上添加轨道（编辑行带下拉三角的＋，分组头的＋）：种类与默认值取原 C# 轨道类型。
export function AddTrack({ elements, documentId, caret }: { elements: SkillElements | null; documentId: string | null; caret?: boolean }) {
  const author = useAuthoring(state => state.author && state.target !== null);
  const schemes = useInputSchemes();
  if (!author || !documentId) return null;
  if (schemes.some(scheme => scheme.documentId === documentId)) return <AddInput documentId={documentId} caret={caret} />;
  // 每种轨道按轨道合同允许的执行域各列一项。
  const entries: MenuEntry[] = elements ? elements.tracks.flatMap(template => template.contract.domains.map((domain): MenuEntry => ({
    label: `${template.kind ?? template.clrType} · ${domain}`,
    onSelect: () => {
      const id = authoringActions.addTrack(documentId, { ...template.value, m_ExecutionDomain: domain }, template.kind ?? '新轨道');
      selectionActions.select({ kind: 'track', id: trackSelectId(documentId, id) });
    },
  }))) : [{ label: '读取中', disabled: true, onSelect: () => {} }];
  return <MenuButton label="添加轨道" variant="ghost" className={styles.addButton} iconOnly caret={caret} entries={entries}><AddIcon /></MenuButton>;
}

function AddInput({ documentId, caret }: { documentId: string; caret?: boolean }) {
  const context = useInputContext(documentId);
  const [pending, setPending] = useState(false);
  const clock = useContext(PreviewClockContext);
  const cursor = usePreviewSelect(clock, seconds => seconds);
  if (context.kind !== 'ready') {
    return <Button variant="ghost" className={styles.addButton} disabled tip={`添加输入：${context.reason}`} aria-label="添加输入"><AddIcon /></Button>;
  }
  const entries = context.actor.keys.flatMap<MenuEntry>(key => ([
    { kind: 'label', label: key.name },
    ...(['hold', 'press'] as const).map(operation => ({
      label: operation === 'hold' ? '按住' : '单按', disabled: pending,
      onSelect: () => {
        const startSeconds = Math.max(0, cursor ?? 0);
        setPending(true);
        void inputAuthoring.add(documentId, {
          scene: context.scene.path, sceneRevision: context.scene.revision, actorId: context.actor.actorId,
          key: key.name, operation, startSeconds, endSeconds: startSeconds + 1,
        }, null).catch(error => consoleActions.error('输入', error)).finally(() => setPending(false));
      },
    })),
  ]));
  return <MenuButton label="添加输入" variant="ghost" className={styles.addButton} iconOnly caret={caret} entries={entries}><AddIcon /></MenuButton>;
}

export function TimelineToolbar({ settings, subject, origin, seek, observations, selectCall, pin, togglePin, parents, openParent, observationNotice, quickPlayback, navigationOrigin }: {
  settings: MenuEntry[]; subject: Subject; origin: number; seek(value: number): void;
  observations: TimelineObservation[]; selectCall: ((value: string) => void) | undefined;
  pin: TimelineObservation | null; togglePin: (() => void) | undefined; parents: RecordedGraphCall[];
  openParent: ((item: RecordedGraphCall) => void) | undefined;
  observationNotice: string | null;
  quickPlayback: QuickPlaybackChoice | null;
  navigationOrigin: GraphNavigationOrigin | null;
}) {
  const clock = useContext(PreviewClockContext);
  const input = useInputObservation(clock.kind === 'input' ? clock.documentId : null);
  const structureNotice = useAuthoring(state => authoringDocument(state).documentError);
  const documentNotice = useDocumentObservationNotice(clock.kind === 'document' ? clock.observation?.call ?? null : null);
  const observed = clock.kind === 'document' || clock.kind === 'archive' ? clock.observation : null;
  const shownObservation = pin ?? observed;
  const seekWithoutCursor = (clock.kind === 'input' && clock.source === 'archive' && input.kind === 'before-start') ||
    (clock.kind === 'archive' && observed !== null);
  // 运行叠加读不到时的原因，一行；正常时不显示（游标头已给出时刻）。
  const notice = useWorkbench(state => {
    if (observationNotice !== null) return observationNotice;
    if (clock.kind === 'quick') return clock.source.kind === 'input' ? '当前输入方案使用路线全局时间'
      : clock.source.segment === null ? '当前路线里这个 Timeline 播了不止一次，请选择具体是哪一次'
        : quickLocalSeconds(clock.source.segment, state.quickPreview.seconds) === null ? '当前路线时刻不在所选这次播放的区间内'
          : '快速预览游标；命中与数值以真预览为准';
    if (clock.kind === 'input') return input.kind === 'ready' ? null : input.reason;
    if (!state.runId) return null;
    if (documentNotice !== null) return documentNotice;
    if (!state.frame?.route) return '档案播放记录尚未提供';
    return documentPlayback(state, clock) ? null : '本帧没有这个 Timeline 的播放记录';
  });
  return (
    <PanelBar>
      <TimeField origin={origin} seek={seek} seekWithoutCursor={seekWithoutCursor} />
      {structureNotice !== null && <span role="alert" className={styles.hint}>{structureNotice}；保留上次完整作者结构</span>}
      {subject.selector && (
        <MenuButton label={subject.selector.label} variant="ghost" className={styles.selector} iconOnly caret entries={subject.selector.entries}>
          <SelectorIcon />
        </MenuButton>
      )}
      <span className={styles.crumbs}>
        <TimelineAssetIcon className={styles.crumbIcon} />
        {subject.crumbs.map((crumb, index) => (
          <Fragment key={index}>
            {index > 0 && <CrumbArrow className={styles.crumbArrow} />}
            {crumb.onSelect
              ? <button className={styles.crumbLink} onClick={crumb.onSelect}>{crumb.label}</button>
              : <span className={styles.crumb}>{crumb.label}</span>}
          </Fragment>
        ))}
      </span>
      {observations.length > 0 && selectCall !== undefined && <Select variant="ghost" label="固定某次实际播放" placeholder="选择并固定实际播放" onChange={selectCall}
        value={observed === null ? null : observationKey(observed)}
        options={observations.map(observation => ({ value: observationKey(observation), label: `${callLabel(observation.call)} · ${observation.domain}` }))} />}
      {quickPlayback !== null && <Select variant="ghost" label="第几次播放" value={quickPlayback.value}
        options={quickPlayback.options} onChange={quickPlayback.select} />}
      {navigationOrigin !== null && <Button variant="ghost" tip={`${navigationOrigin.ownerName} · ${navigationOrigin.nodeId}`} onClick={() => {
        void projectActions.openGraph({ owner: navigationOrigin.owner, ownerName: navigationOrigin.ownerName, id: navigationOrigin.graphId }, navigationOrigin.nodeId,
          { origin: null, observation: navigationOrigin.observation, caller: null, panelId: null })
          .catch(error => consoleActions.error('作者', error));
      }}>返回引用节点</Button>}
      {shownObservation !== null && <Button variant="ghost" pressed={pin !== null} disabled={togglePin === undefined} onClick={togglePin}
        tip={`${pin !== null ? '解除固定，按当前帧选择调用' : '固定本次实际调用和执行域'}\n${callLabel(shownObservation.call)} · ${shownObservation.domain}`}>固定调用</Button>}
      {clock.kind === 'document' && clock.observation !== null && <>
        {(clock.observation.call.parentCall != null || clock.observation.call.parentInvocationPath) && (parents.length > 0 && openParent
          ? <MenuButton label="返回父调用" variant="ghost" entries={parents.map(item => ({ label: `${graphName(item.graph)} · ${callLabel(item.call)}`,
            onSelect: () => openParent(item) }))}>父调用（{parents.length}）</MenuButton>
          : <Button variant="ghost" disabled
            tip={parentCallNotice(clock.observation.call) ?? '完整父调用身份已记录，但对应来源图不在当前已加载记录中'}>父调用来源不可用</Button>)}
      </>}
      {notice && <Tip name={notice}><span className={styles.hint} data-name>{notice}</span></Tip>}
      <span className={styles.grow} />
      <MenuButton label="Timeline 设置" variant="ghost" className={styles.cog} iconOnly caret entries={settings}><SettingsIcon /></MenuButton>
    </PanelBar>
  );
}

// 游标时刻的文字：秒，至多四位小数（照 Timeline 时间框的 0.####）。
// 时间框（TimeCodeGUI，宽 100）：显示游标在所看 Timeline 里的秒数；回车或离开输入框后跳到输入的时刻，Esc 放弃输入。
function TimeField({ origin, seek, seekWithoutCursor }: { origin: number; seek(value: number): void; seekWithoutCursor: boolean }) {
  const clock = useContext(PreviewClockContext);
  const seconds = usePreviewSelect(clock, value => value);
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    if (draft === null) return;
    setDraft(null);
    const value = Number(draft);
    if (draft.trim() && Number.isFinite(value) && value >= 0) seek(origin + value);
  };
  return (
    <TextInput className={styles.timeField} aria-label="游标时间（秒）" disabled={seconds === null && !seekWithoutCursor}
      value={draft ?? (seconds === null ? '' : timeText(seconds - origin))}
      onChange={event => setDraft(event.currentTarget.value)} onBlur={commit}
      onKeyDown={event => {
        if (event.key === 'Enter') commit();
        else if (event.key === 'Escape') setDraft(null);
      }} />
  );
}

// ── 刻度（照 Unity TimeArea.TimeRuler）：点击与拖动经 transport 跳帧 ──

// 刻度级别（秒）：相邻级别成整数倍，与编辑吸附的 0.1／0.01／0.001 秒网格对齐。
const tickModulos = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30, 60, 300, 600, 1800, 3600];
const rulerHeight = 22;
// 小数位：能区分 modulo 间隔所需的位数（MathUtils.GetNumberOfDecimalsForMinimumDifference）。
const decimalsFor = (modulo: number) => Math.max(0, Math.ceil(-Math.log10(modulo) - 1e-9));

// 底部时间滚动条（照 ZoomableArea 的横向滑条）：滑块是当前可见范围，拖滑块平移，拖两端缩放。
export function TimeScroller({ bounds, onRangeChanged }: { bounds: Range; onRangeChanged(update: (previous: Range) => Range): void }) {
  const { range } = useTimelineContext();
  const track = useRef<HTMLDivElement>(null);
  const total = { start: Math.min(bounds.start, range.start), end: Math.max(bounds.end, range.end) };
  const span = total.end - total.start || 1;
  const left = (range.start - total.start) / span * 100;
  const width = (range.end - range.start) / span * 100;
  const drag = (mode: 'move' | 'start' | 'end') => (event: PointerEvent<HTMLElement>) => {
    if (event.button !== 0 || !track.current) return;
    event.stopPropagation();
    const element = event.currentTarget;
    element.setPointerCapture(event.pointerId);
    const scale = span / track.current.clientWidth;
    let lastX = event.clientX;
    const move = (next: globalThis.PointerEvent) => {
      const delta = (next.clientX - lastX) * scale;
      lastX = next.clientX;
      onRangeChanged(previous => mode === 'move' ? { start: previous.start + delta, end: previous.end + delta }
        : mode === 'start' ? { start: Math.min(previous.end - minSpan, previous.start + delta), end: previous.end }
          : { start: previous.start, end: Math.max(previous.start + minSpan, previous.end + delta) });
    };
    const up = () => {
      element.removeEventListener('pointermove', move);
      element.removeEventListener('pointerup', up);
      element.removeEventListener('pointercancel', up);
    };
    element.addEventListener('pointermove', move);
    element.addEventListener('pointerup', up);
    element.addEventListener('pointercancel', up);
  };
  return (
    <div className={styles.scroller} ref={track}>
      <div className={styles.scrollThumb} style={{ left: `${left}%`, width: `${width}%` }} onPointerDown={drag('move')}>
        <i className={styles.scrollEnd} data-side="start" onPointerDown={drag('start')} />
        <i className={styles.scrollEnd} data-side="end" onPointerDown={drag('end')} />
      </div>
    </div>
  );
}

// origin：刻度零点，单个 Timeline 视图按该次播放的本地时间标注，0 之前不画刻度。
// 每级刻度按间距定强度：3 像素为 0、80 像素为 1，开方渐显，高至多为刻度高的 0.9×0.7，自底向上画；
// 首个间距不小于 40 像素的级别在刻度右侧 3 像素标数字，小数位取帧率与该级间隔中较细的一个。
// 底部 5 像素蓝条为 Timeline 时长（HighlightTimeAreaRange）。拖动刻度时 data-scrubbing 让覆盖层显示游标时刻。
export function Ruler({ bounds, origin, rate, seekAt }: {
  bounds: Range; origin: number; rate: number | null; seekAt(clientX: number): void;
}) {
  const { range, valueToPixels } = useTimelineContext();
  const width = valueToPixels(range.end - range.start);
  const scale = width / (range.end - range.start);
  const first = Math.max(0, range.start - origin), last = range.end - origin;
  const x = (local: number) => Math.floor(valueToPixels(local + origin - range.start));
  const levels: { path: string; opacity: number }[] = [];
  const labels: { x: number; text: string }[] = [];
  if (width > 0 && last > first) {
    // 最细的可见级别与最细的满强度级别（TickHandler.SetTickStrengths）。
    const strengths = tickModulos.map(modulo => (modulo * scale - 3) / (80 - 3));
    let smallest = 0, biggest = tickModulos.length - 1;
    for (let index = tickModulos.length - 1; index >= 0; index--) {
      if (strengths[index] >= 1) biggest = index;
      if (tickModulos[index] * scale <= 3) { smallest = index; break; }
    }
    for (let index = smallest; index <= biggest; index++) {
      const strength = Math.sqrt(Math.min(1, Math.max(0, strengths[index]))) * 0.9;
      if (strength <= 0) continue;
      const modulo = tickModulos[index];
      // 与上一级重合的刻度留给上一级画。
      const coarser = index < biggest ? Math.round(tickModulos[index + 1] / modulo) : 0;
      const top = rulerHeight - rulerHeight * Math.min(1, strength) * 0.7;
      let path = '';
      for (let tick = Math.ceil(first / modulo); tick * modulo <= last; tick++)
        if (!coarser || tick % coarser) path += `M${x(tick * modulo) + 0.5} ${top}V${rulerHeight}`;
      if (path) levels.push({ path, opacity: Math.min(1, strength / 0.5 * 0.75) });
    }
    const labelModulo = tickModulos.find(modulo => modulo * scale >= 40);
    if (labelModulo !== undefined) {
      const digits = Math.max(decimalsFor(labelModulo), rate ? decimalsFor(1 / rate) : 0);
      for (let tick = Math.ceil(first / labelModulo); tick * labelModulo <= last; tick++)
        labels.push({ x: x(tick * labelModulo) + 3, text: (tick * labelModulo).toFixed(digits) });
    }
  }
  const durationStart = Math.max(0, valueToPixels(bounds.start - range.start));
  const durationEnd = Math.min(width, valueToPixels(bounds.end - range.start));
  return (
    <div className={styles.ruler} role="slider" aria-label="记录游标" aria-valuemin={bounds.start} aria-valuemax={bounds.end}
      onPointerDown={event => {
        if (event.button !== 0) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        event.currentTarget.dataset.scrubbing = '';
        seekAt(event.clientX);
      }}
      onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) seekAt(event.clientX); }}
      onLostPointerCapture={event => { delete event.currentTarget.dataset.scrubbing; }}>
      <svg className={styles.ticks} width={Math.max(0, width)} height={rulerHeight} aria-hidden>
        {levels.map((level, index) => <path key={index} d={level.path} strokeOpacity={level.opacity} />)}
      </svg>
      {labels.map(label => <span key={label.text} className={styles.tickLabel} style={{ left: label.x }}>{label.text}</span>)}
      {durationEnd > durationStart && <i className={styles.duration} style={{ left: durationStart, width: durationEnd - durationStart }} />}
    </div>
  );
}

