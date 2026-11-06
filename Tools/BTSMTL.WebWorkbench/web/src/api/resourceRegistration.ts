import { ApiError, postJson } from './http';
import { array, choice, nullable, number, object, opaque, optional, text } from './playerRecords';
import { readAuthoringAvailability } from './authoringAvailability';

const readResourceReferenceFields = object({ domain: text, selection: nullable(choice('asset', 'mapped')),
  objectType: nullable(text), profile: nullable(text), mappingInputType: nullable(text), referenceType: text,
  availability: readAuthoringAvailability });
export function readResourceReferenceMetadata(value: unknown, path = 'ResourceReference') {
  const { source } = object({ source: nullable(choice('AdapterResource', 'AuthoringDocument')) })(value, path);
  if (source === null) {
    const rule = object({ domain: nullable(text), selection: nullable(choice('asset', 'mapped')),
      contextFieldId: text, contextType: text, availability: readAuthoringAvailability })(value, path);
    if (rule.domain !== null || rule.selection !== null || rule.availability.available)
      throw new Error(`${path} 未解析的资源规则需要空领域、空选择方式和明确的不可用原因`);
    return { ...rule, source, domain: null, selection: null, availability: rule.availability };
  }
  const fields = readResourceReferenceFields(value, path);
  switch (source) {
    case 'AdapterResource': return { ...fields, source, ...object({
      catalog: object({ owner: choice('Authoring'), command: choice('catalog'), domain: text }),
    })(value, path) };
    case 'AuthoringDocument': return { ...fields, source, ...object({
      catalog: object({ owner: choice('Authoring'), command: choice('elements'), domain: text }),
    })(value, path) };
  }
}
export type ResourceReferenceMetadata = ReturnType<typeof readResourceReferenceMetadata>;
export const readExecutionScopeSelection = object({ previewScope: nullable(text), runtimeScope: nullable(text) });
export type ExecutionScopeSelection = ReturnType<typeof readExecutionScopeSelection>;
const readDeclarationSource = (value: unknown, path = 'ResourceDeclaration.source') => {
  const source = number(value, path);
  switch (source) {
    case 0: return 'AdapterResource' as const;
    case 1: return 'AuthoringDocument' as const;
    default: throw new Error(`${path} 不支持 ${source}`);
  }
};
export const readResourceDeclaration = object({ domain: text, selection: nullable(choice('asset', 'mapped')), profile: nullable(text), source: readDeclarationSource,
  mappingInputType: nullable(text), objectType: nullable(text), availability: readAuthoringAvailability });
export type ResourceDeclaration = ReturnType<typeof readResourceDeclaration>;
export type ResourceObjectQuery = { adapter: string; domain: string; context: string; asset: string; objectType: string };
export type ResourceObjectCapability =
  | { kind: 'available'; scope: Omit<ResourceObjectQuery, 'asset'>; valueMember: string }
  | { kind: 'unavailable'; reason: string };
export function readObjectSelectionMetadata(value: unknown): ResourceObjectCapability {
  const { availability } = object({ availability: readAuthoringAvailability })(value, 'ObjectSelection');
  if (!availability.available) return { kind: 'unavailable', reason: availability.reason };
  const { command, valueMember, ...scope } = object({ command: choice('resources/objects'), valueMember: text,
    adapter: text, domain: text, context: text, objectType: text })(value, 'ObjectSelection');
  return { kind: 'available', scope, valueMember };
}
const readObjectCandidate = object({ value: text, type: text, name: text, hierarchy: text, data: opaque });
export type ResourceObjectCandidate = ReturnType<typeof readObjectCandidate>;
export function resourceObjectValue(candidate: ResourceObjectCandidate, member: string): string {
  let value: unknown = candidate;
  for (const name of member.split('.')) value = object({ [name]: opaque })(value, `ResourceObject.${member}`)[name];
  return text(value, `ResourceObject.${member}`);
}
function readResourceObjects(value: unknown) {
  const availability = readAuthoringAvailability(value, 'ResourceObjects');
  return { ...object({ revision: nullable(text), items: array(readObjectCandidate) })(value, 'ResourceObjects'), ...availability };
}
export type ResourceObjects = ReturnType<typeof readResourceObjects>;
const readScope = object({ scope: text, adapter: text, context: text, revision: text, resourceDeclarations: array(readResourceDeclaration) });
export type AuthoringScope = ReturnType<typeof readScope>;
export type ScopeQuery = Pick<AuthoringScope, 'scope' | 'adapter'>;
export const scopeQueryKey = (query: ScopeQuery) => JSON.stringify([query.scope, query.adapter]);
export const resourceRegistrationApi = {
  readScope: (query: ScopeQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/scopes/read', query, signal).then(value => readScope(value)),
  objects: (query: ResourceObjectQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/resources/objects', query, signal).then(readResourceObjects),
};

const readFailure = object({ authorState: choice('not-requested', 'not-completed', 'recovery-required'), runtimeState: choice('not-applied') });
export function resourceOperationError(error: unknown) {
  if (error instanceof ApiError && [400, 409, 500].includes(error.status)) {
    const body: unknown = JSON.parse(error.raw);
    if (object({ code: optional(text) })(body).code !== undefined) {
      const result = readFailure(body);
      return `${error.reason}；作者状态：${result.authorState === 'recovery-required' ? '需要恢复' : result.authorState === 'not-requested' ? '未请求' : '未完成'}，运行未应用。`;
    }
  }
  return error instanceof Error ? error.message : String(error);
}
