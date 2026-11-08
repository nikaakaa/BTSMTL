import { getJson, postJson, putJson, query } from './http';
import { array, boolean, choice, hosted, nullable, object, optional, text } from './playerRecords';
import type { AuthoringProduct } from './authoring';
import { readExecutionScopeSelection, type ExecutionScopeSelection } from './resourceRegistration';
import type { SourceFile } from './types';

export const workspaceDocumentKinds = ['skill', 'timeline', 'curve', 'profile', 'scenario', 'variables', 'graph'] as const;
const documentFields = {
  kind: choice(...workspaceDocumentKinds), identity: text, documentPath: text,
  rootFile: nullable(text), revision: text, sourceIdentity: text, contentDomain: nullable(text), scope: nullable(text),
  authoringType: optional(text),
};
export const readWorkspaceDocumentInfo = object(documentFields);
const readDocument = readWorkspaceDocumentInfo;
export type WorkspaceDocumentInfo = ReturnType<typeof readDocument>;

const readFiles = (value: unknown, path = 'WorkspaceDocument.files'): Record<string, string> =>
  Object.fromEntries(Object.entries(hosted({})(value, path)).map(([name, content]) => [name, text(content, `${path}.${name}`)]));
export const readWorkspaceDocumentSnapshot = object({ ...documentFields, files: readFiles });
export type WorkspaceDocumentSnapshot = ReturnType<typeof readWorkspaceDocumentSnapshot>;

const readElement = object({ kind: text, identity: text, name: text,
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
export const readWorkspaceDocumentView = object({ success: boolean, documents: array(readWorkspaceDocumentSnapshot), elements: array(readElement) });
const readView = readWorkspaceDocumentView;
const readEditingView = object({
  writes: array(object({ kind: choice(...workspaceDocumentKinds), documentPath: text, expectedRevision: text, files: readFiles })),
  reads: array(object({ documentPath: text, revision: text })), execution: readExecutionScopeSelection,
  product: nullable(object({ value: text, revision: text })), scene: optional(object({ path: text, revision: text })),
});
export function readWorkspaceEditingView(value: unknown): WorkspaceEditingView {
  const { scene, ...view } = readEditingView(value, 'EditingView');
  return { ...view, ...(scene === undefined ? {} : { scene }) };
}

export const workspaceDocumentsApi = {
  list: (kind?: WorkspaceDocumentInfo['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents${query({ kind })}`, signal).then(value => readList(value).documents),
  open: (documentPath: string, signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/content${query({ documentPath })}`, signal).then(value => readOpen(value).document),
  elements: (kind?: WorkspaceDocumentElement['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/elements${query({ kind })}`, signal).then(value => readElements(value).elements),
  editingView: (editingView: WorkspaceEditingView, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/documents/editing-view', editingView, signal).then(value => readView(value)),
  save: (editingView: Pick<WorkspaceEditingView, 'writes' | 'reads'> & Partial<Omit<WorkspaceEditingView, 'writes' | 'reads'>>) =>
    putJson<unknown>('/api/authoring/documents', editingView).then(value => readView(value)),
};
