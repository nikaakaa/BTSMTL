import { ApiError, getJson, postJson } from './http';
import { array, boolean, choice, nullable, number, object, opaque, optional, text } from './playerRecords';
import type { JsonObject } from '../state/skillJson';

function readCapabilityAvailability(value: unknown, path = 'ResourceCapability') {
  const { available, reason } = object({ available: boolean, reason: nullable(text) })(value, path);
  if (available && reason === null) return { available: true as const, reason };
  if (!available && reason !== null) return { available: false as const, reason };
  throw new Error(`${path} 的可用状态与原因不符合正式资源能力合同`);
}
export const readResourceReferenceMetadata = object({ domain: text, selection: nullable(choice('asset', 'mapped')),
  objectType: nullable(text), profile: nullable(text), mappingInputType: nullable(text), referenceType: text,
  catalog: object({ owner: choice('Authoring'), command: choice('catalog'), domain: text }), availability: readCapabilityAvailability });
export type ResourceReferenceMetadata = ReturnType<typeof readResourceReferenceMetadata>;
export const readResourceReference = object({ domain: text, value: text, context: text });
export type ResourceReference = ReturnType<typeof readResourceReference>;
export const resourceReferenceKey = (value: ResourceReference) => JSON.stringify([value.domain, value.value, value.context]);
export const readExecutionScopeSelection = object({ previewScope: nullable(text), runtimeScope: nullable(text) });
export type ExecutionScopeSelection = ReturnType<typeof readExecutionScopeSelection>;
export const readResourceDeclaration = object({ domain: text, selection: nullable(choice('asset', 'mapped')), profile: nullable(text),
  mappingInputType: nullable(text), objectType: nullable(text), availability: readCapabilityAvailability });
export type ResourceDeclaration = ReturnType<typeof readResourceDeclaration>;
const readProfileResourceValue = object({ domain: text, value: text, context: text, token: nullable(text), instanceCapacity: nullable(number),
  configurationJson: nullable(text) });
const readProfileResource = (value: unknown) => {
  const { domain, value: reference, context, ...resource } = readProfileResourceValue(value);
  return { ...resource, reference: { domain, value: reference, context } };
};
export type ResourceObjectQuery = { adapter: string; domain: string; context: string; asset: string; objectType: string };
export type ResourceObjectCapability =
  | { kind: 'available'; scope: Omit<ResourceObjectQuery, 'asset'>; valueMember: string }
  | { kind: 'unavailable'; reason: string };
export function readObjectSelectionMetadata(value: unknown): ResourceObjectCapability {
  const { availability } = object({ availability: readCapabilityAvailability })(value, 'ObjectSelection');
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
const readObjectResult = object({ available: boolean, reason: nullable(text), revision: nullable(text), items: array(readObjectCandidate) });
function readResourceObjects(value: unknown) {
  const result = readObjectResult(value, 'ResourceObjects');
  if (result.available) return { ...result, available: true as const };
  if (result.reason === null) throw new Error('ResourceObjects 缺少不可用原因');
  return { ...result, available: false as const, reason: result.reason };
}
export type ResourceObjects = ReturnType<typeof readResourceObjects>;
const readTransaction = object({ changes: array(object({ path: text, before: nullable(text), after: nullable(text) })) });
export type AuthoringDataTransaction = ReturnType<typeof readTransaction>;
const readScope = object({ scope: text, adapter: text, context: text, revision: text, resourceDeclarations: array(readResourceDeclaration) });
export type AuthoringScope = ReturnType<typeof readScope>;
export type ScopeQuery = Pick<AuthoringScope, 'scope' | 'adapter'>;
export const scopeQueryKey = (query: ScopeQuery) => JSON.stringify([query.scope, query.adapter]);
const readProfile = object({ group: text, adapter: text, assetToken: text, domain: text, context: text,
  revision: text, configurationJson: text, resourceDeclarations: array(readResourceDeclaration), resources: array(readProfileResource),
  operations: array(object({ command: text, inputType: text, resultType: text })) });
export type AuthoringProfile = ReturnType<typeof readProfile>;
export type ProfileQuery = Pick<AuthoringProfile, 'group' | 'adapter' | 'assetToken'>;
export const profileQueryKey = (query: ProfileQuery) => JSON.stringify([query.group, query.adapter, query.assetToken]);
const readProfileResult = object({ value: nullable(text), reused: boolean, instanceCapacity: nullable(number),
  transaction: readTransaction, profile: readProfile, runtimeState: choice('pending') });
export type ProfileOperationResult = ReturnType<typeof readProfileResult>;
export type ResourceMappingInput = JsonObject & { kind: string };
const readProfiles = object({ availability: readCapabilityAvailability, revision: text, items: array(object({
  adapter: text, group: text, assetToken: text, domain: text, context: text, display: text, source: text,
})) });
export type ProfileLocation = ReturnType<typeof readProfiles>['items'][number];
export const resourceRegistrationApi = {
  profiles: (signal?: AbortSignal) => getJson<unknown>('/api/authoring/profiles', signal).then(value => readProfiles(value)),
  readScope: (query: ScopeQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/scopes/read', query, signal).then(value => readScope(value)),
  readProfile: (query: ProfileQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/profiles/read', query, signal).then(value => readProfile(value)),
  mapProfile: (profile: AuthoringProfile, domain: string, input: ResourceMappingInput) => {
    return postJson<unknown>('/api/authoring/profiles/resources/map', { group: profile.group, adapter: profile.adapter,
      context: profile.context, domain, input, profile: { assetToken: profile.assetToken, expectedRevision: profile.revision } })
      .then(value => readProfileResult(value));
  },
  profileCapacity: (profile: AuthoringProfile, domain: string, value: string, instanceCapacity: number) =>
    postJson<unknown>('/api/authoring/profiles/resources/capacity', { group: profile.group, adapter: profile.adapter,
      context: profile.context, domain, value, instanceCapacity, profile: { assetToken: profile.assetToken, expectedRevision: profile.revision } })
      .then(value => readProfileResult(value)),
  configureProfile: (profile: AuthoringProfile, configurationJson: string) =>
    postJson<unknown>('/api/authoring/profiles/configure', { group: profile.group, adapter: profile.adapter,
      profile: { assetToken: profile.assetToken, expectedRevision: profile.revision }, configurationJson }).then(value => readProfileResult(value)),
  configureProfileResource: (profile: AuthoringProfile, reference: ResourceReference, configurationJson: string) =>
    postJson<unknown>('/api/authoring/profiles/resources/configure', { group: profile.group, adapter: profile.adapter,
      context: reference.context, domain: reference.domain, value: reference.value,
      profile: { assetToken: profile.assetToken, expectedRevision: profile.revision }, configurationJson }).then(value => readProfileResult(value)),
  replayProfile: (query: ProfileQuery, direction: 'undo' | 'redo', transaction: AuthoringDataTransaction) =>
    postJson<unknown>(`/api/authoring/profiles/data/${direction}`, { ...query, transaction }).then(value => readProfileResult(value)),
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
