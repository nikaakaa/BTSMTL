import { getJson, postJson, putJson, query } from './http';
import { array, boolean, choice, hosted, nullable, object, optional, text } from './playerRecords';
import type { AuthoringProduct } from './authoring';
import type { ExecutionScopeSelection } from './resourceRegistration';
import type { SourceFile } from './types';

export const workspaceDocumentKinds = ['skill', 'timeline', 'curve', 'profile', 'scenario', 'variables', 'graph'] as const;
const documentFields = {
  kind: choice(...workspaceDocumentKinds), identity: text, documentPath: text,
  rootFile: nullable(text), revision: text, sourceIdentity: text, contentDomain: nullable(text), scope: nullable(text),
  authoringType: optional(text),
};
const readDocument = object(documentFields);
export type WorkspaceDocumentInfo = ReturnType<typeof readDocument>;

const readFiles = (value: unknown, path = 'WorkspaceDocument.files'): Record<string, string> =>
  Object.fromEntries(Object.entries(hosted({})(value, path)).map(([name, content]) => [name, text(content, `${path}.${name}`)]));
export const readWorkspaceDocumentSnapshot = object({ ...documentFields, files: readFiles });
export type WorkspaceDocumentSnapshot = ReturnType<typeof readWorkspaceDocumentSnapshot>;

const readElement = object({ kind: text, identity: text,
  contentDomain: nullable(text), sourceIdentity: nullable(text), documentPath: text, relativePath: text,
  jsonPath: text, revision: text, authoringType: nullable(text) });
export type WorkspaceDocumentElement = ReturnType<typeof readElement>;
export type WorkspaceDocumentWrite = Pick<WorkspaceDocumentSnapshot, 'kind' | 'documentPath' | 'files'> & { expectedRevision: string };
export type WorkspaceDocumentRead = Pick<WorkspaceDocumentInfo, 'documentPath' | 'revision'>;
export type WorkspaceEditingView = {
  writes: WorkspaceDocumentWrite[];
  reads: WorkspaceDocumentRead[];
  execution: ExecutionScopeSelection;
  product: Pick<AuthoringProduct, 'value' | 'revision'> | null;
  scene?: Pick<SourceFile, 'path' | 'revision'>;
};

const readList = object({ success: boolean, documents: array(readDocument) });
const readOpen = object({ success: boolean, document: readWorkspaceDocumentSnapshot });
const readElements = object({ success: boolean, elements: array(readElement) });
const readView = object({ success: boolean, documents: array(readWorkspaceDocumentSnapshot), elements: array(readElement) });

export const workspaceDocumentsApi = {
  list: (kind?: WorkspaceDocumentInfo['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents${query({ kind })}`, signal).then(value => readList(value).documents),
  open: (documentPath: string, signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/content${query({ documentPath })}`, signal).then(value => readOpen(value).document),
  elements: (kind?: WorkspaceDocumentElement['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/elements${query({ kind })}`, signal).then(value => readElements(value).elements),
  editingView: (writes: WorkspaceDocumentWrite[], signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/documents/editing-view', { writes }, signal).then(value => readView(value)),
  save: (writes: WorkspaceDocumentWrite[], reads: WorkspaceDocumentRead[]) =>
    putJson<unknown>('/api/authoring/documents', { writes, reads }).then(value => readView(value)),
};
