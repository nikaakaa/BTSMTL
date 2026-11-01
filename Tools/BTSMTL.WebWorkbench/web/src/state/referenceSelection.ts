import { create } from 'zustand';
import type { ReferenceChoice } from '../api/authoring';
import { parseSkillJson, stringifySkillJson, type JsonValue } from './skillJson';

export type ReferenceCatalog =
  | { kind: 'ready'; referenceKind: string; items: readonly ReferenceChoice[]; invalidReason: string }
  | { kind: 'unavailable'; reason: string };

export interface ReferenceFieldSelection {
  id: string;
  name: string;
  catalog: ReferenceCatalog;
  value: JsonValue | undefined;
  choose(choice: ReferenceChoice): void;
}

export const useReferenceSelection = create<{ field: ReferenceFieldSelection | null }>()(() => ({ field: null }));

export const referenceValueKey = (value: JsonValue | undefined) =>
  value === undefined ? undefined : stringifySkillJson(value, '');

const referenceMime = 'application/x-btsmtl-authoring-reference';
const kindMime = (kind: string) => `${referenceMime}.${encodeURIComponent(kind).toLowerCase()}`;

export function writeReferenceDrag(transfer: DataTransfer, kind: string, choice: Pick<ReferenceChoice, 'value' | 'source'>) {
  transfer.effectAllowed = 'copy';
  transfer.setData(referenceMime, JSON.stringify({ kind, value: referenceValueKey(choice.value), source: choice.source }));
  transfer.setData(kindMime(kind), '');
}

export const acceptsReferenceDrag = (transfer: DataTransfer, kind: string) =>
  transfer.types.includes(kindMime(kind));

export type ReferenceDrop =
  | { kind: 'selected'; choice: ReferenceChoice }
  | { kind: 'rejected'; reason: string };

export type ReferenceDrag =
  | { kind: 'reference'; referenceKind: string; value: JsonValue; source: string }
  | { kind: 'rejected'; reason: string };

export function readReferenceDrag(transfer: DataTransfer): ReferenceDrag {
  let payload: unknown;
  try { payload = JSON.parse(transfer.getData(referenceMime)); }
  catch { return { kind: 'rejected', reason: '拖入内容不是作者引用，原值已保留' }; }
  if (payload === null || typeof payload !== 'object' || !('kind' in payload) ||
    !('value' in payload) || !('source' in payload) || typeof payload.kind !== 'string' ||
    typeof payload.value !== 'string' || typeof payload.source !== 'string')
    return { kind: 'rejected', reason: '拖入引用缺少类型或身份，原值已保留' };
  try { return { kind: 'reference', referenceKind: payload.kind, value: parseSkillJson(payload.value), source: payload.source }; }
  catch { return { kind: 'rejected', reason: '拖入引用的值不符合作者 JSON 格式，原值已保留' }; }
}

export function readReferenceDrop(transfer: DataTransfer, catalog: ReferenceCatalog): ReferenceDrop {
  if (catalog.kind === 'unavailable') return { kind: 'rejected', reason: catalog.reason };
  const drag = readReferenceDrag(transfer);
  if (drag.kind === 'rejected') return drag;
  if (drag.referenceKind !== catalog.referenceKind)
    return { kind: 'rejected', reason: '引用类型与当前字段不匹配，原值已保留' };
  const key = referenceValueKey(drag.value);
  const choice = catalog.items.find(item => referenceValueKey(item.value) === key && item.source === drag.source);
  return choice ? { kind: 'selected', choice }
    : { kind: 'rejected', reason: catalog.invalidReason };
}
