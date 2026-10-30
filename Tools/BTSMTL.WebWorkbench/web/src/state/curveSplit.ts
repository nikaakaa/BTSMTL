import { JsonFloat, numberOf, type JsonObject, type JsonValue } from './skillJson';

// 片段切分时的归一化曲线拆分（照 Unity BtsmtlSlateTimelineBinding.SplitNormalizedCurve）：
// 取 [start, end] 内的关键帧换到 0–1，切线乘区间长度，两端缺关键帧时按原曲线求值补一帧。
// 只支持非权重切线（weightedMode 为 0），权重曲线的求值与 Unity 不一致时拒绝切分。

interface Key { time: number; value: number; inTangent: number; outTangent: number; source: JsonObject }

const keysOf = (curve: JsonObject): Key[] => (curve.keys as JsonObject[]).map(key => ({
  time: numberOf(key.time), value: numberOf(key.value), inTangent: numberOf(key.inTangent), outTangent: numberOf(key.outTangent), source: key,
}));

export const isWeighted = (curve: JsonObject) => (curve.keys as JsonObject[]).some(key => numberOf(key.weightedMode) !== 0);

// Unity AnimationCurve.Evaluate（非权重 Hermite；两端外按端点值钳制，切线为无穷时为阶跃）。
function evaluate(keys: Key[], time: number): number {
  if (!keys.length) return 0;
  if (time <= keys[0].time) return keys[0].value;
  const last = keys[keys.length - 1];
  if (time >= last.time) return last.value;
  for (let index = 1; index < keys.length; index++) {
    const from = keys[index - 1], to = keys[index];
    if (time > to.time) continue;
    const span = to.time - from.time;
    if (!Number.isFinite(from.outTangent) || !Number.isFinite(to.inTangent) || span <= 0) return from.value;
    const s = (time - from.time) / span, s2 = s * s, s3 = s2 * s;
    return (2 * s3 - 3 * s2 + 1) * from.value + (s3 - 2 * s2 + s) * from.outTangent * span
      + (-2 * s3 + 3 * s2) * to.value + (s3 - s2) * to.inTangent * span;
  }
  return last.value;
}

// 边界切线：落在关键帧之间取前一关键帧的出切线（照 Unity BoundaryTangent）。
function boundaryTangent(keys: Key[], time: number): number {
  if (!keys.length) return 0;
  if (time <= keys[0].time) return keys[0].outTangent;
  if (time >= keys[keys.length - 1].time) return keys[keys.length - 1].inTangent;
  for (let index = 1; index < keys.length; index++) if (time <= keys[index].time) return keys[index - 1].outTangent;
  return 0;
}

// 键按序号顺序排列，与宿主存盘一致。
// 无穷切线（阶跃）原样保留。
const tangentJson = (tangent: number, scale: number, raw?: JsonValue) => Number.isFinite(tangent) ? new JsonFloat(tangent * scale) : raw ?? 'Infinity';

const keyJson = (time: number, value: number, inTangent: JsonValue, outTangent: JsonValue, source?: JsonObject): JsonObject => {
  const key: JsonObject = {
    ...(source ?? { inWeight: new JsonFloat(0), outWeight: new JsonFloat(0), tangentMode: 0, weightedMode: 0 }),
    inTangent, outTangent, time: new JsonFloat(time), value: new JsonFloat(value),
  };
  return Object.fromEntries(Object.keys(key).sort((left, right) => left < right ? -1 : left > right ? 1 : 0).map(name => [name, key[name]]));
};

export function splitCurve(curve: JsonObject, start: number, end: number): JsonObject {
  const keys = keysOf(curve);
  const duration = end - start;
  const result: JsonObject[] = [];
  let hasStart = false, hasEnd = false;
  for (const key of keys) {
    if (key.time < start || key.time > end) continue;
    const time = Math.min(1, Math.max(0, (key.time - start) / duration));
    hasStart ||= time === 0;
    hasEnd ||= time === 1;
    result.push(keyJson(time, key.value, tangentJson(key.inTangent, duration, key.source.inTangent),
      tangentJson(key.outTangent, duration, key.source.outTangent), key.source));
  }
  const boundary = (at: number, normalized: number) => {
    const tangent = tangentJson(boundaryTangent(keys, at), duration);
    return keyJson(normalized, evaluate(keys, at), tangent, tangent);
  };
  if (!hasStart) result.push(boundary(start, 0));
  if (!hasEnd) result.push(boundary(end, 1));
  result.sort((left, right) => numberOf(left.time) - numberOf(right.time));
  return { ...curve, keys: result };
}
