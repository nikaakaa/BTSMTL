import { useRef, useState, type PointerEvent } from 'react';
import { JsonFloat, cloneJson, numberOf, type JsonObject, type JsonValue } from '../../state/skillJson';
import { Button, NumberInput, Select } from '../../ui/controls';
import styles from './inspector.module.css';

// 曲线字段（Unity AnimationCurve 的 JSON：keys 与前后循环方式）。图上拖动关键帧改时间与值，
// 下方表格逐项改时间、值和切线；预览按 Hermite 插值画，权重切线按非权重近似显示。

export const isCurve = (value: JsonValue | undefined, clrType?: string) =>
  value !== null && value !== undefined && typeof value === 'object' && !(value instanceof JsonFloat) && !Array.isArray(value)
    ? Array.isArray((value as JsonObject).keys)
    : value == null && !!clrType && /Curve/.test(clrType);

const wrapModes = [
  { value: '0', label: 'Default' }, { value: '1', label: 'Once' }, { value: '2', label: 'Loop' },
  { value: '4', label: 'PingPong' }, { value: '8', label: 'ClampForever' },
];

const keyOf = (time: number, value: number, inTangent: number, outTangent: number, weight: number): JsonObject => ({
  inTangent: new JsonFloat(inTangent), inWeight: new JsonFloat(weight), outTangent: new JsonFloat(outTangent),
  outWeight: new JsonFloat(weight), tangentMode: 0, time: new JsonFloat(time), value: new JsonFloat(value), weightedMode: 0,
});

const defaultCurve = (): JsonObject => ({
  keys: [keyOf(0, 0, 0, 0, 0), keyOf(1, 1, 0, 0, 0)], postWrapMode: 8, preWrapMode: 8,
});

interface Point { t: number; v: number; inTangent: number; outTangent: number }

const pointsOf = (keys: JsonObject[]): Point[] => keys.map(key => ({
  t: numberOf(key.time), v: numberOf(key.value), inTangent: numberOf(key.inTangent), outTangent: numberOf(key.outTangent),
}));

// 段内 Hermite 采样；任一端切线为无穷（字符串 Infinity 读作 NaN）时为阶跃。
function sample(points: Point[]): [number, number][] {
  const result: [number, number][] = [];
  for (let index = 0; index + 1 < points.length; index++) {
    const from = points[index], to = points[index + 1], span = to.t - from.t;
    if (!Number.isFinite(from.outTangent) || !Number.isFinite(to.inTangent) || span <= 0) {
      result.push([from.t, from.v], [to.t, from.v], [to.t, to.v]);
      continue;
    }
    for (let step = 0; step <= 24; step++) {
      const s = step / 24, s2 = s * s, s3 = s2 * s;
      const value = (2 * s3 - 3 * s2 + 1) * from.v + (s3 - 2 * s2 + s) * from.outTangent * span
        + (-2 * s3 + 3 * s2) * to.v + (s3 - s2) * to.inTangent * span;
      result.push([from.t + s * span, value]);
    }
  }
  if (points.length === 1) result.push([points[0].t, points[0].v]);
  return result;
}

const width = 240, height = 110, pad = 8;

interface Bounds { t0: number; t1: number; v0: number; v1: number }

function boundsOf(samples: [number, number][]): Bounds {
  let t0 = Math.min(...samples.map(item => item[0])), t1 = Math.max(...samples.map(item => item[0]));
  let v0 = Math.min(...samples.map(item => item[1])), v1 = Math.max(...samples.map(item => item[1]));
  if (!(t1 > t0)) t1 = t0 + 1;
  if (!(v1 > v0)) { v0 -= 0.5; v1 += 0.5; }
  const margin = (v1 - v0) * 0.08;
  return { t0, t1, v0: v0 - margin, v1: v1 + margin };
}

export function CurveEditor({ label, value, onSet }: { label: string; value: JsonValue; onSet(value: JsonValue): void }) {
  const svg = useRef<SVGSVGElement>(null);
  const [drag, setDrag] = useState<{ index: number; t: number; v: number; bounds: Bounds } | null>(null);
  if (value === null || typeof value !== 'object' || Array.isArray(value) || value instanceof JsonFloat) {
    return (
      <div className={styles.field}><label title={label}>{label}</label>
        <div className={styles.editor}><Button variant="ghost" onClick={() => onSet(defaultCurve())}>创建曲线</Button></div>
      </div>
    );
  }
  const curve = value as JsonObject;
  const keys = curve.keys as JsonObject[];
  const points = pointsOf(keys).map((point, index) => drag?.index === index ? { ...point, t: drag.t, v: drag.v } : point);
  const samples = sample(points);
  const bounds = drag?.bounds ?? boundsOf(samples.length ? samples : [[0, 0]]);
  const x = (t: number) => pad + (t - bounds.t0) / (bounds.t1 - bounds.t0) * (width - pad * 2);
  const y = (v: number) => height - pad - (v - bounds.v0) / (bounds.v1 - bounds.v0) * (height - pad * 2);

  // 改写一个关键帧后整条曲线写回，关键帧按时间排序。
  const write = (next: JsonObject[]) => {
    const sorted = [...next].sort((left, right) => numberOf(left.time) - numberOf(right.time));
    onSet({ ...cloneJson(curve), keys: sorted });
  };
  const update = (index: number, change: JsonObject) =>
    write(keys.map((key, item) => item === index ? { ...cloneJson(key), ...change } : cloneJson(key)));
  const remove = (index: number) => write(keys.filter((_, item) => item !== index).map(key => cloneJson(key)));
  const add = () => {
    const last = points[points.length - 1], previous = points[points.length - 2];
    const t = previous ? (previous.t + last.t) / 2 : (last?.t ?? 0) + 1;
    const near = samples.reduce((best, item) => Math.abs(item[0] - t) < Math.abs(best[0] - t) ? item : best, samples[0] ?? [t, 0]);
    write([...keys.map(key => cloneJson(key)), keyOf(t, near[1], 0, 0, numberOf(keys[0]?.inWeight) || 0)]);
  };

  // 拖动结果取到万分位，避免把屏幕换算的长尾小数写进作者 JSON。
  const toCurve = (event: PointerEvent, frame: Bounds) => {
    const box = svg.current!.getBoundingClientRect();
    const px = (event.clientX - box.left) / box.width * width, py = (event.clientY - box.top) / box.height * height;
    const round = (value: number) => Math.round(value * 1e4) / 1e4;
    return {
      t: round(frame.t0 + (px - pad) / (width - pad * 2) * (frame.t1 - frame.t0)),
      v: round(frame.v0 + (height - pad - py) / (height - pad * 2) * (frame.v1 - frame.v0)),
    };
  };

  return (
    <div className={styles.curve}>
      <div className={styles.groupHead}>{label}</div>
      <svg ref={svg} className={styles.curveView} viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none"
        onPointerMove={event => { if (drag) setDrag({ ...drag, ...toCurve(event, drag.bounds) }); }}
        onPointerUp={() => {
          if (!drag) return;
          setDrag(null);
          update(drag.index, { time: new JsonFloat(drag.t), value: new JsonFloat(drag.v) });
        }}>
        <line x1={pad} x2={width - pad} y1={y(0)} y2={y(0)} className={styles.curveAxis} />
        <polyline points={samples.map(([t, v]) => `${x(t)},${y(v)}`).join(' ')} className={styles.curveLine} />
        {points.map((point, index) => (
          <circle key={index} cx={x(point.t)} cy={y(point.v)} r={3.5} className={styles.curveKey}
            data-active={drag?.index === index || undefined}
            onPointerDown={event => {
              event.currentTarget.ownerSVGElement?.setPointerCapture(event.pointerId);
              setDrag({ index, t: point.t, v: point.v, bounds });
            }} />
        ))}
      </svg>
      <div className={styles.keys}>
        <span>时间</span><span>值</span><span>入切线</span><span>出切线</span><span />
        {keys.map((key, index) => (
          <KeyRow key={index} label={`${label} 关键帧 ${index}`} keyValue={key} removable={keys.length > 1}
            onChange={change => update(index, change)} onRemove={() => remove(index)} />
        ))}
      </div>
      <div className={styles.curveFoot}>
        <Button variant="ghost" onClick={add}>添加关键帧</Button>
        <Select label="前循环" value={String(numberOf(curve.preWrapMode))} options={wrapModes}
          onChange={next => onSet({ ...cloneJson(curve), preWrapMode: Number(next) })} />
        <Select label="后循环" value={String(numberOf(curve.postWrapMode))} options={wrapModes}
          onChange={next => onSet({ ...cloneJson(curve), postWrapMode: Number(next) })} />
      </div>
    </div>
  );
}

function KeyRow({ label, keyValue, removable, onChange, onRemove }: {
  label: string; keyValue: JsonObject; removable: boolean; onChange(change: JsonObject): void; onRemove(): void;
}) {
  const cell = (name: string) => {
    const current = keyValue[name];
    // 无穷切线在 JSON 里是字符串，原样显示；改写时写回浮点。
    const number = numberOf(current);
    return (
      <NumberInput wide label={`${label} ${name}`} value={Number.isNaN(number) ? null : number}
        onChange={next => { if (next !== null && next !== number) onChange({ [name]: new JsonFloat(next) }); }} />
    );
  };
  return (
    <>
      {cell('time')}{cell('value')}{cell('inTangent')}{cell('outTangent')}
      <Button variant="ghost" disabled={!removable} tip="删除关键帧" onClick={onRemove}>×</Button>
    </>
  );
}
