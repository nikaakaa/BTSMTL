import { create } from 'zustand';
import type { AdapterCatalogItem, ReferenceChoice, ReferenceChoiceTarget } from '../api/authoring';
import { getIn, parseSkillJson, stringifySkillJson, type JsonObject, type JsonValue } from './skillJson';
import type { ResourceObjectCapability, ResourceObjectCandidate } from '../api/resourceRegistration';
import type { WorkspaceDocumentElement } from '../api/workspaceDocuments';
import { nullable, object, text } from '../api/playerRecords';

export type ReferenceCatalog =
  | { kind: 'ready'; referenceKind: string; items: readonly ReferenceChoice[]; invalidReason: string }
  | { kind: 'unavailable'; reason: string };

export type ResourceObjectFieldSelection = Extract<ResourceObjectCapability, { kind: 'unavailable' }>
  | (Extract<ResourceObjectCapability, { kind: 'available' }> & { choose(asset: ResourceObjectCandidate): void });

export interface ReferenceFieldSelection {
  id: string;
  name: string;
  domain: string | null;
  selection: 'asset' | 'mapped' | null;
  resourceContext: { adapter: string; context: string } | null;
  catalog: ReferenceCatalog;
  value: JsonValue | undefined;
  configuration: JsonObject;
  choose(choice: ReferenceChoice): void;
  resourceObjects?: ResourceObjectFieldSelection;
}

// browsing：作者在引用字段点了“浏览”，Project 按该字段筛选并定位当前值，直到填入或取消。
export const useReferenceSelection = create<{ field: ReferenceFieldSelection | null; browsing: boolean }>()(() => ({ field: null, browsing: false }));

export function resourceReferenceChoice(item: Pick<AdapterCatalogItem, 'adapter' | 'domain' | 'value' | 'display' | 'context'>, conflict: string | null): ReferenceChoice {
  return { value: item.value, name: item.display, detail: item.context === '' ? item.value : `${item.value}（${item.context}）`, source: item.adapter, conflict,
    targets: [{ kind: 'Resource', domain: item.domain, value: item.value, context: item.context }], associated: [] };
}

// 作者文档元素（Profile、曲线等）作为引用候选；字段候选与 Project 拖拽共用，拖入时才能对上同一项。
export function documentElementChoice(element: WorkspaceDocumentElement & { sourceIdentity: string }, conflict: string | null): ReferenceChoice {
  return { value: element.sourceIdentity, name: element.name, detail: element.documentPath,
    source: `${element.documentPath}/${element.relativePath}#${element.jsonPath}`, conflict, targets: [], associated: [] };
}

export const referenceValueKey = (value: JsonValue | undefined) =>
  value === undefined ? undefined : stringifySkillJson(value, '');

export const matchesReferenceChoice = (choice: ReferenceChoice, selection: Pick<ReferenceFieldSelection, 'value' | 'configuration'>) =>
  referenceValueKey(choice.value) === referenceValueKey(selection.value) && choice.associated.every(field =>
    referenceValueKey(field.value) === referenceValueKey(getIn(selection.configuration, field.serializedMemberPath)));

export function referenceTargetKey(target: ReferenceChoiceTarget): string {
  switch (target.kind) {
    case 'Node': return JSON.stringify([target.kind, target.graphId, target.nodeId]);
    case 'ProfileEntry': return JSON.stringify([target.kind, target.carrierIdentity, target.profileSource, target.domain, target.entryKind, target.key, target.resourceIdentity]);
    case 'Resource': return JSON.stringify([target.kind, target.domain, target.value, target.context]);
    default: { const unhandled: never = target; return unhandled; }
  }
}

type ReferenceDragChoice = Pick<ReferenceChoice, 'value' | 'source' | 'targets' | 'associated'>;

const referenceChoiceKey = (choice: ReferenceDragChoice) => JSON.stringify([
  referenceValueKey(choice.value), choice.source, choice.targets.map(referenceTargetKey).sort(),
  choice.associated.map(field => [field.fieldId, field.serializedMemberPath, referenceValueKey(field.value)]),
]);

const referenceMime = 'application/x-btsmtl-authoring-reference';
const kindMime = (kind: string) => `${referenceMime}.${encodeURIComponent(kind).toLowerCase()}`;

export function writeReferenceDrag(transfer: DataTransfer, kind: string, choice: ReferenceDragChoice) {
  const resources = choice.targets.filter(target => target.kind === 'Resource');
  const resource = resources.length === 1 ? { domain: resources[0].domain, context: resources[0].context } : null;
  transfer.effectAllowed = 'copy';
  transfer.setData(referenceMime, JSON.stringify({ kind, value: referenceValueKey(choice.value), source: choice.source, choiceKey: referenceChoiceKey(choice), resource }));
  transfer.setData(kindMime(kind), '');
}

export const acceptsReferenceDrag = (transfer: DataTransfer, kind: string) =>
  transfer.types.includes(kindMime(kind));

export type ReferenceDrop =
  | { kind: 'selected'; choice: ReferenceChoice }
  | { kind: 'rejected'; reason: string };

export type ReferenceDrag =
  | { kind: 'reference'; referenceKind: string; value: JsonValue; source: string; choiceKey: string; resource: { domain: string; context: string | null } | null }
  | { kind: 'rejected'; reason: string };

export function readReferenceDrag(transfer: DataTransfer): ReferenceDrag {
  try {
    const payload = object({ kind: text, value: text, source: text, choiceKey: text,
      resource: nullable(object({ domain: text, context: nullable(text) })) })(JSON.parse(transfer.getData(referenceMime)), 'ReferenceDrag');
    if (payload.resource !== null && payload.resource.domain !== payload.kind) throw new Error('资源域与拖放类型不一致');
    return { kind: 'reference', referenceKind: payload.kind, value: parseSkillJson(payload.value), source: payload.source,
      choiceKey: payload.choiceKey, resource: payload.resource };
  } catch (error) { return { kind: 'rejected', reason: `拖入作者引用无法读取，原值已保留：${error instanceof Error ? error.message : String(error)}` }; }
}

export function readReferenceDrop(transfer: DataTransfer, catalog: ReferenceCatalog): ReferenceDrop {
  const drag = readReferenceDrag(transfer);
  if (drag.kind === 'rejected') return drag;
  return resolveReferenceDrag(drag, catalog);
}

export function resolveReferenceDrag(drag: Extract<ReferenceDrag, { kind: 'reference' }>, catalog: ReferenceCatalog): ReferenceDrop {
  if (catalog.kind === 'unavailable') return { kind: 'rejected', reason: catalog.reason };
  if (drag.referenceKind !== catalog.referenceKind)
    return { kind: 'rejected', reason: '引用类型与当前字段不匹配，原值已保留' };
  const choices = catalog.items.filter(item => referenceChoiceKey(item) === drag.choiceKey);
  if (choices.length > 1) return { kind: 'rejected', reason: '引用仍有多个不同绑定，请从字段候选列表选择；原值已保留' };
  if (choices.length === 1 && choices[0].conflict !== null) return { kind: 'rejected', reason: choices[0].conflict };
  return choices.length === 1 ? { kind: 'selected', choice: choices[0] }
    : { kind: 'rejected', reason: catalog.invalidReason };
}
