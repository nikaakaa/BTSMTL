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
export const readWorkspaceDocumentElements = array(readElement);
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
const readChanged = object({ success: boolean, document: optional(readWorkspaceDocumentSnapshot) });
const readElements = object({ success: boolean, elements: readWorkspaceDocumentElements });
export const readWorkspaceDocumentView = object({ success: boolean, documents: array(readWorkspaceDocumentSnapshot), elements: readWorkspaceDocumentElements });
const readView = readWorkspaceDocumentView;
const readEditingView = object({
  writes: array(object({ kind: choice(...workspaceDocumentKinds), documentPath: text, expectedRevision: text, files: readFiles })),
  reads: array(object({ documentPath: text, revision: text })), execution: readExecutionScopeSelection,
  // Host 的 Scene 可空，未选场景时写出 null。
  product: nullable(object({ value: text, revision: text })), scene: optional(nullable(object({ path: text, revision: text }))),
});
export function readWorkspaceEditingView(value: unknown): WorkspaceEditingView {
  const { scene, ...view } = readEditingView(value, 'EditingView');
  return { ...view, ...(scene == null ? {} : { scene }) };
}

export const workspaceDocumentsApi = {
  list: (kind?: WorkspaceDocumentInfo['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents${query({ kind })}`, signal).then(value => readList(value).documents),
  open: (documentPath: string, signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/content${query({ documentPath })}`, signal).then(value => readOpen(value).document),
  // 修订未变时宿主只回执未变化，返回 null；变了才带回完整正文。带 waitSeconds 时宿主等到修订变化或到时再答复。
  openIfChanged: (documentPath: string, revision: string, signal?: AbortSignal, waitSeconds?: number) =>
    getJson<unknown>(`/api/authoring/documents/content${query({ documentPath, revision, wait: waitSeconds })}`, signal)
      .then(value => readChanged(value).document ?? null),
  elements: (kind?: WorkspaceDocumentElement['kind'], signal?: AbortSignal) =>
    getJson<unknown>(`/api/authoring/documents/elements${query({ kind })}`, signal).then(value => readElements(value).elements),
  editingView: (editingView: WorkspaceEditingView, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/documents/editing-view', editingView, signal).then(value => readView(value)),
  save: (editingView: Pick<WorkspaceEditingView, 'writes' | 'reads'> & Partial<Omit<WorkspaceEditingView, 'writes' | 'reads'>>) =>
    putJson<unknown>('/api/authoring/documents', editingView).then(value => readView(value)),
};
