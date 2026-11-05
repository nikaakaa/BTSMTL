import { ApiError, postJson } from './http';
import { array, boolean, choice, nullable, number, object, text } from './playerRecords';

function readCapabilityAvailability(value: unknown, path = 'ResourceCapability') {
  const { available, reason } = object({ available: boolean, reason: nullable(text) })(value, path);
  if (available && reason === null) return { available: true as const, reason };
  if (!available && reason !== null) return { available: false as const, reason };
  throw new Error(`${path} 的可用状态与原因不符合正式资源能力合同`);
}
const readObjectCapability = object({ owner: choice('Authoring'), command: choice('resource-objects'),
  queryType: text, resultType: text, keyKind: text, domain: nullable(text), objectType: nullable(text),
  availability: readCapabilityAvailability });
export const readResourceRegistrationMetadata = object({ owner: choice('Authoring'), command: choice('register-resource'),
  keyKind: text, inputKind: choice('asset', 'instance-resource', 'camera-effect'), inputType: text, referenceType: text,
  candidates: readObjectCapability });
export type ResourceRegistrationMetadata = ReturnType<typeof readResourceRegistrationMetadata>;
export const readResourceReferenceMetadata = object({ keyKind: text, domain: nullable(text), objectType: nullable(text),
  referenceType: text, availability: readCapabilityAvailability, registration: nullable(readResourceRegistrationMetadata) });
export type ResourceReferenceMetadata = ReturnType<typeof readResourceReferenceMetadata>;
export const readResourceReference = object({ package: text, keyKind: text, key: text, context: text });
export type ResourceReference = ReturnType<typeof readResourceReference>;
export const resourceReferenceKey = (value: ResourceReference) => JSON.stringify([value.package, value.keyKind, value.key, value.context]);
export const readExecutionScopeSelection = object({ previewScope: nullable(readResourceReference), runtimeScope: nullable(readResourceReference) });
export type ExecutionScopeSelection = ReturnType<typeof readExecutionScopeSelection>;
export const readResourceDeclaration = object({ keyKind: text, referenceKind: text, domain: nullable(text),
  registrationInput: nullable(choice('asset', 'instance-resource', 'camera-effect')), inputType: nullable(text),
  objectType: nullable(text), availability: readCapabilityAvailability });
export type ResourceDeclaration = ReturnType<typeof readResourceDeclaration>;
const readProfileResource = object({ reference: readResourceReference, token: nullable(text), instanceCapacity: nullable(number),
  configurationJson: nullable(text) });
export type ResourceObjectQuery = { package: string; adapter: string; assetGuid: string; keyKind: string;
  domain: string | null; objectType: string | null };
export type ResourceObjectCapability =
  | { kind: 'available'; scope: Omit<ResourceObjectQuery, 'adapter' | 'assetGuid'> }
  | { kind: 'unavailable'; reason: string };
const readObjectResult = object({ available: boolean, reason: nullable(text), revision: text,
  items: array(object({ token: text, type: text, name: text, hierarchy: text })) });
function readResourceObjects(value: unknown) {
  const result = readObjectResult(value, 'ResourceObjects');
  if (result.available) return { ...result, available: true as const };
  if (result.reason === null) throw new Error('ResourceObjects 缺少不可用原因');
  return { ...result, available: false as const, reason: result.reason };
}
export type ResourceObjects = ReturnType<typeof readResourceObjects>;
const readTransaction = object({ changes: array(object({ path: text, before: nullable(text), after: nullable(text) })) });
export type AuthoringDataTransaction = ReturnType<typeof readTransaction>;
const resultFields = { revision: nullable(text), files: array(object({ path: text, content: text })), runtimeState: choice('pending') };
const registrationFields = { ...resultFields,
  reference: readResourceReference,
  reused: boolean, instanceCapacity: nullable(number), transaction: readTransaction };
const readScopeRegistration = object({ ...registrationFields, revision: text });
const readScopeReplay = object({ ...resultFields, revision: text });
const readScope = object({ scope: readResourceReference, adapter: text, revision: text, resourceDeclarations: array(readResourceDeclaration) });
export type AuthoringScope = ReturnType<typeof readScope>;
export type ScopeQuery = Pick<AuthoringScope, 'scope' | 'adapter'>;
export const scopeQueryKey = (query: ScopeQuery) => JSON.stringify([resourceReferenceKey(query.scope), query.adapter]);
const readProfile = object({ package: text, adapter: text, assetToken: text, domain: text, context: text,
  revision: text, configurationJson: text, resourceDeclarations: array(readResourceDeclaration), resources: array(readProfileResource),
  operations: array(object({ command: text, inputType: text, resultType: text })) });
export type AuthoringProfile = ReturnType<typeof readProfile>;
export type ProfileQuery = Pick<AuthoringProfile, 'package' | 'adapter' | 'assetToken'>;
export const profileQueryKey = (query: ProfileQuery) => JSON.stringify([query.package, query.adapter, query.assetToken]);
const readProfileResult = object({ reference: nullable(readResourceReference), reused: boolean, instanceCapacity: nullable(number),
  transaction: readTransaction, profile: readProfile, runtimeState: choice('pending') });
export type ProfileOperationResult = ReturnType<typeof readProfileResult>;
export type ResourceRegistrationResult = ReturnType<typeof readScopeRegistration>;
export type ResourceRegistrationInput =
  | { kind: 'asset'; key: string; token: string }
  | { kind: 'instance-resource'; key: string; token: string; instanceCapacity: number }
  | { kind: 'camera-effect'; key: string; token: string; effectKind: string };
export const resourceRegistrationApi = {
  readScope: (query: ScopeQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/scopes/read', query, signal).then(value => readScope(value)),
  registerScope: (source: AuthoringScope, keyKind: string, value: ResourceRegistrationInput) => {
    const { kind, ...input } = value;
    return postJson<unknown>('/api/authoring/scopes/resources/register', { scope: source.scope, adapter: source.adapter,
      package: source.scope.package, context: source.scope.context, expectedRevision: source.revision, keyKind, input: { kind, ...input } })
      .then(value => readScopeRegistration(value));
  },
  replayScope: (query: ScopeQuery, direction: 'undo' | 'redo', transaction: AuthoringDataTransaction) =>
    postJson<unknown>(`/api/authoring/scopes/data/${direction}`, { ...query, transaction }).then(value => readScopeReplay(value)),
  readProfile: (query: ProfileQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/profiles/read', query, signal).then(value => readProfile(value)),
  registerProfile: (profile: AuthoringProfile, keyKind: string, value: ResourceRegistrationInput) => {
    const { kind, ...input } = value;
    return postJson<unknown>('/api/authoring/profiles/resources/register', { package: profile.package, adapter: profile.adapter,
      context: profile.context, keyKind, input: { kind, ...input }, profile: { assetToken: profile.assetToken, expectedRevision: profile.revision } })
      .then(value => readProfileResult(value));
  },
  profileCapacity: (profile: AuthoringProfile, keyKind: string, key: string, instanceCapacity: number) =>
    postJson<unknown>('/api/authoring/profiles/resources/capacity', { package: profile.package, adapter: profile.adapter,
      context: profile.context, keyKind, key, instanceCapacity, profile: { assetToken: profile.assetToken, expectedRevision: profile.revision } })
      .then(value => readProfileResult(value)),
  configureProfile: (profile: AuthoringProfile, configurationJson: string) =>
    postJson<unknown>('/api/authoring/profiles/configure', { package: profile.package, adapter: profile.adapter,
      profile: { assetToken: profile.assetToken, expectedRevision: profile.revision }, configurationJson }).then(value => readProfileResult(value)),
  configureProfileResource: (profile: AuthoringProfile, reference: ResourceReference, configurationJson: string) =>
    postJson<unknown>('/api/authoring/profiles/resources/configure', { package: profile.package, adapter: profile.adapter,
      context: reference.context, keyKind: reference.keyKind, key: reference.key,
      profile: { assetToken: profile.assetToken, expectedRevision: profile.revision }, configurationJson }).then(value => readProfileResult(value)),
  replayProfile: (query: ProfileQuery, direction: 'undo' | 'redo', transaction: AuthoringDataTransaction) =>
    postJson<unknown>(`/api/authoring/profiles/data/${direction}`, { ...query, transaction }).then(value => readProfileResult(value)),
  objects: (query: ResourceObjectQuery, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/resources/objects', query, signal).then(readResourceObjects),
};

const readFailure = object({ success: boolean, code: choice('resource-registration', 'resource-registration-conflict', 'resource-registration-io'),
  message: text, authorState: choice('not-completed', 'recovery-required'), runtimeState: choice('not-applied') });
export function resourceOperationError(error: unknown) {
  if (error instanceof ApiError && [400, 409, 500].includes(error.status)) {
    const result = readFailure(JSON.parse(error.raw));
    return `${result.message}；作者状态：${result.authorState === 'recovery-required' ? '需要恢复' : '未完成'}，运行未应用。`;
  }
  return error instanceof Error ? error.message : String(error);
}
