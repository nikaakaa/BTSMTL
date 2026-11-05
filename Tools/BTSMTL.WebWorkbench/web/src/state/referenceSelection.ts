import { create } from 'zustand';
import type { ReferenceChoice, ReferenceChoiceTarget } from '../api/authoring';
import { getIn, parseSkillJson, stringifySkillJson, type JsonObject, type JsonValue } from './skillJson';

export type ReferenceCatalog =
  | { kind: 'ready'; referenceKind: string; items: readonly ReferenceChoice[]; invalidReason: string }
  | { kind: 'unavailable'; reason: string };

export interface ReferenceFieldSelection {
  id: string;
  name: string;
  catalog: ReferenceCatalog;
  value: JsonValue | undefined;
  configuration: JsonObject;
  choose(choice: ReferenceChoice): void;
}

export const useReferenceSelection = create<{ field: ReferenceFieldSelection | null }>()(() => ({ field: null }));

export const referenceValueKey = (value: JsonValue | undefined) =>
  value === undefined ? undefined : stringifySkillJson(value, '');

export const matchesReferenceChoice = (choice: ReferenceChoice, selection: Pick<ReferenceFieldSelection, 'value' | 'configuration'>) =>
  referenceValueKey(choice.value) === referenceValueKey(selection.value) && choice.associated.every(field =>
    referenceValueKey(field.value) === referenceValueKey(getIn(selection.configuration, field.serializedMemberPath)));

export const referenceTargetKey = (target: ReferenceChoiceTarget) => target.kind === 'Node'
  ? JSON.stringify([target.kind, target.graphId, target.nodeId])
  : JSON.stringify([target.kind, target.carrierIdentity, target.profileSource, target.domain, target.entryKind, target.key, target.resourceIdentity]);

type ReferenceDragChoice = Pick<ReferenceChoice, 'value' | 'source' | 'targets' | 'associated'>;

const referenceChoiceKey = (choice: ReferenceDragChoice) => JSON.stringify([
  referenceValueKey(choice.value), choice.source, choice.targets.map(referenceTargetKey).sort(),
  choice.associated.map(field => [field.fieldId, field.serializedMemberPath, referenceValueKey(field.value)]),
]);

const referenceMime = 'application/x-btsmtl-authoring-reference';
const kindMime = (kind: string) => `${referenceMime}.${encodeURIComponent(kind).toLowerCase()}`;

export function writeReferenceDrag(transfer: DataTransfer, kind: string, choice: ReferenceDragChoice) {
  transfer.effectAllowed = 'copy';
  transfer.setData(referenceMime, JSON.stringify({ kind, value: referenceValueKey(choice.value), source: choice.source, choiceKey: referenceChoiceKey(choice) }));
  transfer.setData(kindMime(kind), '');
}

export const acceptsReferenceDrag = (transfer: DataTransfer, kind: string) =>
  transfer.types.includes(kindMime(kind));

export type ReferenceDrop =
  | { kind: 'selected'; choice: ReferenceChoice }
  | { kind: 'rejected'; reason: string };

export type ReferenceDrag =
  | { kind: 'reference'; referenceKind: string; value: JsonValue; source: string; choiceKey: string }
  | { kind: 'rejected'; reason: string };

export function readReferenceDrag(transfer: DataTransfer): ReferenceDrag {
  let payload: unknown;
  try { payload = JSON.parse(transfer.getData(referenceMime)); }
  catch { return { kind: 'rejected', reason: '拖入内容不是作者引用，原值已保留' }; }
  if (payload === null || typeof payload !== 'object' || !('kind' in payload) ||
    !('value' in payload) || !('source' in payload) || !('choiceKey' in payload) || typeof payload.kind !== 'string' ||
    typeof payload.value !== 'string' || typeof payload.source !== 'string' || typeof payload.choiceKey !== 'string')
    return { kind: 'rejected', reason: '拖入引用缺少类型或身份，原值已保留' };
  try { return { kind: 'reference', referenceKind: payload.kind, value: parseSkillJson(payload.value), source: payload.source, choiceKey: payload.choiceKey }; }
  catch { return { kind: 'rejected', reason: '拖入引用的值不符合作者 JSON 格式，原值已保留' }; }
}

export function readReferenceDrop(transfer: DataTransfer, catalog: ReferenceCatalog): ReferenceDrop {
  if (catalog.kind === 'unavailable') return { kind: 'rejected', reason: catalog.reason };
  const drag = readReferenceDrag(transfer);
  if (drag.kind === 'rejected') return drag;
  if (drag.referenceKind !== catalog.referenceKind)
    return { kind: 'rejected', reason: '引用类型与当前字段不匹配，原值已保留' };
  const choices = catalog.items.filter(item => referenceChoiceKey(item) === drag.choiceKey);
  if (choices.length > 1) return { kind: 'rejected', reason: '引用仍有多个不同绑定，请从字段候选列表选择；原值已保留' };
  return choices.length === 1 ? { kind: 'selected', choice: choices[0] }
    : { kind: 'rejected', reason: catalog.invalidReason };
}
