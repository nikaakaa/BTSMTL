import { JsonFloat, cloneJson, numberOf, type JsonObject, type JsonValue } from './skillJson';

// 通道曲线的关键帧编辑（照 Unity 曲线编辑器，时间为参数区间内的归一化 0–1）。
// tangentMode 照 UnityEditor.AnimationUtility：第 0 位 Broken，1–4 位左切线模式，5–8 位右切线模式。
// 只编辑 Free / Linear / Constant；Auto 与 Clamped Auto 的切线由 Unity 原生代码计算，这里不复刻，原样保留。

export type TangentKind = 'free' | 'linear' | 'constant';
export type TangentSide = 'left' | 'right';
const modeCodes: Record<TangentKind, number> = { free: 0, linear: 2, constant: 3 };
const shifts: Record<TangentSide, number> = { left: 1, right: 5 };

const modeOf = (key: JsonObject) => numberOf(key.tangentMode) | 0;
export const tangentOf = (key: JsonObject, side: TangentSide) => numberOf(side === 'left' ? key.inTangent : key.outTangent);
const slopeJson = (slope: number): JsonValue => Number.isFinite(slope) ? new JsonFloat(slope) : 'Infinity';

export const keyTime = (key: JsonObject) => numberOf(key.time);
export const keyValue = (key: JsonObject) => numberOf(key.value);
// 未知模式（Auto、Clamped Auto）为 null。
export function tangentKind(key: JsonObject, side: TangentSide): TangentKind | null {
  const code = (modeOf(key) >> shifts[side]) & 15;
  return code === 0 ? 'free' : code === 2 ? 'linear' : code === 3 ? 'constant' : null;
}
export const isBroken = (key: JsonObject) => (modeOf(key) & 1) !== 0;

function setMode(key: JsonObject, side: TangentSide, kind: TangentKind) {
  key.tangentMode = (modeOf(key) & ~(15 << shifts[side])) | modeCodes[kind] << shifts[side];
}

const setBrokenBit = (key: JsonObject, broken: boolean) => { key.tangentMode = broken ? modeOf(key) | 1 : modeOf(key) & ~1; };

// 照 AnimationUtility.UpdateTangentsFromMode：Linear 指向相邻关键帧，Constant 为无穷（阶跃）。
function updateTangents(keys: JsonObject[], index: number) {
  const key = keys[index];
  if (!key) return;
  const slope = (from: JsonObject, to: JsonObject) => (keyValue(to) - keyValue(from)) / (keyTime(to) - keyTime(from));
  if (tangentKind(key, 'left') === 'linear' && index > 0) key.inTangent = slopeJson(slope(keys[index - 1], key));
  if (tangentKind(key, 'right') === 'linear' && index + 1 < keys.length) key.outTangent = slopeJson(slope(key, keys[index + 1]));
  if (tangentKind(key, 'left') === 'constant') key.inTangent = 'Infinity';
  if (tangentKind(key, 'right') === 'constant') key.outTangent = 'Infinity';
}

// 按时间排序后更新改动关键帧及其两侧的切线，返回新曲线与改动关键帧排序后的序号。
function finish(curve: JsonObject, keys: JsonObject[], touched: JsonObject[]): { curve: JsonObject; indices: number[] } {
  keys.sort((left, right) => keyTime(left) - keyTime(right));
  const indices = touched.map(key => keys.indexOf(key)).filter(index => index >= 0);
  const around = new Set(indices.flatMap(index => [index - 1, index, index + 1]));
  for (const index of [...around].sort((left, right) => left - right)) updateTangents(keys, index);
  return { curve: { ...cloneJson(curve), keys }, indices };
}

// 移动所选关键帧：place 给出每个关键帧的新时间与值；与其它关键帧同一时刻时拒绝（返回 null）。
export function moveKeys(curve: JsonObject, indices: number[], place: (time: number, value: number) => [number, number]) {
  const keys = cloneJson(curve.keys) as JsonObject[];
  const moved = indices.map(index => keys[index]);
  for (const key of moved) {
    const [time, value] = place(keyTime(key), keyValue(key));
    key.time = new JsonFloat(time);
    key.value = new JsonFloat(value);
  }
  const fixed = keys.filter(key => !moved.includes(key)).map(keyTime);
  const times = moved.map(keyTime);
  if (times.some((time, index) => fixed.some(other => Math.abs(other - time) < 1e-6) || times.indexOf(time) !== index)) return null;
  return finish(curve, keys, moved);
}

export function removeKeys(curve: JsonObject, indices: number[]): JsonObject {
  const keys = (cloneJson(curve.keys) as JsonObject[]);
  const neighbours = indices.flatMap(index => [keys[index - 1], keys[index + 1]]).filter(key => key && !indices.includes(keys.indexOf(key)));
  const rest = keys.filter((_, index) => !indices.includes(index));
  return finish(curve, rest, neighbours).curve;
}

// 右键菜单的切线设置（照 CurveMenuManager）：单侧改为 Linear / Constant 时同时断开（Broken）。
export function setTangentKind(curve: JsonObject, indices: number[], sides: TangentSide[], kind: TangentKind) {
  const keys = cloneJson(curve.keys) as JsonObject[];
  const picked = indices.map(index => keys[index]);
  for (const key of picked) {
    for (const side of sides) setMode(key, side, kind);
    if (kind !== 'free') setBrokenBit(key, true);
  }
  return finish(curve, keys, picked);
}

// Free Smooth：两侧 Free 且不断开，两侧切线取平均（只有一侧有限时取那一侧）。Flat：切线为 0。
export function smoothKeys(curve: JsonObject, indices: number[], flat: boolean) {
  const keys = cloneJson(curve.keys) as JsonObject[];
  const picked = indices.map(index => keys[index]);
  for (const key of picked) {
    const [left, right] = [tangentOf(key, 'left'), tangentOf(key, 'right')];
    const finite = [left, right].filter(Number.isFinite);
    const slope = flat || !finite.length ? 0 : finite.reduce((sum, value) => sum + value, 0) / finite.length;
    setMode(key, 'left', 'free');
    setMode(key, 'right', 'free');
    setBrokenBit(key, false);
    key.inTangent = new JsonFloat(slope);
    key.outTangent = new JsonFloat(slope);
  }
  return finish(curve, keys, picked);
}

// Broken 开关：断开后两侧切线可分别拖动；取消断开等同 Free Smooth。
export function breakKeys(curve: JsonObject, indices: number[], broken: boolean) {
  if (!broken) return smoothKeys(curve, indices, false);
  const keys = cloneJson(curve.keys) as JsonObject[];
  const picked = indices.map(index => keys[index]);
  for (const key of picked) setBrokenBit(key, true);
  return finish(curve, keys, picked);
}

// 拖动切线手柄：该侧改为 Free；未断开时两侧一起改。
export function setTangent(curve: JsonObject, index: number, side: TangentSide, slope: number) {
  const keys = cloneJson(curve.keys) as JsonObject[];
  const key = keys[index];
  const sides: TangentSide[] = isBroken(key) ? [side] : ['left', 'right'];
  for (const item of sides) {
    setMode(key, item, 'free');
    key[item === 'left' ? 'inTangent' : 'outTangent'] = new JsonFloat(slope);
  }
  return finish(curve, keys, [key]);
}
