import { postJson } from './http';
import { array, boolean, choice, nullable, number, object, text } from './playerRecords';

export const readResourceRegistrationMetadata = object({ owner: choice('Authoring'), command: choice('register-resource'),
  keyKind: text, inputKind: choice('asset', 'instance-resource', 'camera-effect'), inputType: text, referenceType: text });
export type ResourceRegistrationMetadata = ReturnType<typeof readResourceRegistrationMetadata>;
const readTransaction = object({ changes: array(object({ path: text, before: nullable(text), after: nullable(text) })) });
export type AuthoringDataTransaction = ReturnType<typeof readTransaction>;
const resultFields = { revision: nullable(text), files: array(object({ path: text, content: text })), runtimeState: choice('pending') };
const readRegistration = object({ ...resultFields,
  reference: object({ package: text, keyKind: text, key: text, context: text }),
  reused: boolean, instanceCapacity: nullable(number), transaction: readTransaction });
const readReplay = object(resultFields);
export type ResourceRegistrationResult = ReturnType<typeof readRegistration>;
export type ResourceRegistrationInput =
  | { kind: 'asset'; key: string; token: string }
  | { kind: 'instance-resource'; key: string; token: string; instanceCapacity: number }
  | { kind: 'camera-effect'; key: string; token: string; effectKind: string };
export type ResourceRegistrationRequest = {
  package: string; adapter: string; context: string; keyKind: string; file: string; memberPath: string[];
  input: ResourceRegistrationInput; expectedRevision: string | null;
  files: { path: string; content: string }[]; removedFiles: string[];
};
const root = (skill: string) => `/api/authoring/skills/${encodeURIComponent(skill)}`;
export const resourceRegistrationApi = {
  register: (skill: string, request: ResourceRegistrationRequest) =>
    postJson<unknown>(`${root(skill)}/resources/register`, request).then(value => readRegistration(value)),
  replay: (skill: string, direction: 'undo' | 'redo', transaction: AuthoringDataTransaction) =>
    postJson<unknown>(`${root(skill)}/data/${direction}`, transaction).then(value => readReplay(value)),
};
