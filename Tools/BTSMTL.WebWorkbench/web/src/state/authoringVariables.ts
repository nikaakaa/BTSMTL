// 图变量：按变量所有者读取宿主变量文档，编辑经宿主变量接口串行提交并进入撤销。
import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringOwner } from '../api/authoring';
import { isAbort } from '../api/http';
import { variablesApi, type VariableOperation, type VariablesQuery } from '../api/variables';
import { workspaceDocumentsApi } from '../api/workspaceDocuments';
import {
  authoringActions, authoringDocument, captureEditingView, get, historyLimit, newDocument, set, setDocument, snapshot, useAuthoring, variableRequests,
  type AuthoringState,
} from './authoring';
import { parseSkillJson } from './skillJson';
import { consoleActions } from './store';

export function graphVariableOwner(graphId: string): string | null {
  const graph = get().documentGraphs!.find(graph => graph.id === graphId)!;
  if (graph.role === null) return graph.variableOwnerId;
  return graph.role === 'StateMachine' ? null : graphId;
}

const variableDocument = (state: AuthoringState, ownerId: string) => Object.entries(state.documents)
  .find(([, document]) => document.kind === 'variables' && document.identity === ownerId);

async function loadVariables(ownerId: string, signal: AbortSignal): Promise<void> {
  const captured = variableDocument(useAuthoring.getState(), ownerId)?.[1];
  const result = await variablesApi.query(ownerId, authoringActions.editingView(), signal);
  signal.throwIfAborted();
  const key = result.document.documentPath;
  const current = useAuthoring.getState().documents[key];
  if (current?.version !== captured?.version || current?.revision !== captured?.revision) return;
  if (current === undefined) {
    const saved = await workspaceDocumentsApi.open(key, signal);
    signal.throwIfAborted();
    if (useAuthoring.getState().documents[key] !== undefined) return;
    set({ documents: { ...useAuthoring.getState().documents, [key]: { ...newDocument(), kind: saved.kind,
      rootFile: saved.rootFile, identity: saved.identity, authoringType: saved.authoringType ?? null,
      revision: saved.revision, sourceFiles: saved.files, files: saved.files } } });
    return loadVariables(ownerId, signal);
  }
  setDocument({ variables: result }, authoringOwner(key));
}

export function useVariables(ownerId: string | null) {
  const source = useAuthoring(useShallow(state => {
    const document = ownerId === null ? undefined : variableDocument(state, ownerId)?.[1];
    return { value: document?.variables ?? null, version: document?.version ?? null,
      revision: document?.revision ?? null, writing: document?.variablesWriting === true };
  }));
  const [failure, setFailure] = useState<{ ownerId: string; text: string } | null>(null);
  useEffect(() => {
    if (ownerId === null) return;
    const controller = new AbortController();
    setFailure(null);
    loadVariables(ownerId, controller.signal).catch((error: unknown) => {
      if (!isAbort(error)) {
        setFailure({ ownerId, text: error instanceof Error ? error.message : String(error) });
        consoleActions.error('变量', error);
      }
    });
    return () => controller.abort();
  }, [ownerId, source.version, source.revision]);
  return { value: source.value, writing: source.writing, failure: failure?.ownerId === ownerId ? failure.text : null };
}

async function applyVariableEdit(ownerId: string, operation: VariableOperation): Promise<string> {
  if (get().resourceTarget !== null) set({ resourceTarget: null });
  const [key, current] = variableDocument(useAuthoring.getState(), ownerId)!;
  const target = authoringOwner(key);
  const historyTarget = get().target!;
  const before = snapshot(target);
  const { editingView, isCurrentRequest } = captureEditingView();
  setDocument({ variablesWriting: true }, target);
  try {
    const result = await variablesApi.edit(ownerId, editingView, operation);
    const currentCatalog = isCurrentRequest();
    const document = authoringDocument(useAuthoring.getState(), target);
    const changed = result.value.m_Revision !== current.variables!.value.m_Revision;
    if (changed) {
      const write = result.editingView.writes.find(write => write.documentPath === key)!;
      const edited = { ...document.edited };
      for (const [path, content] of Object.entries(write.files)) {
        if (content === document.files[path]) delete edited[path];
        else edited[path] = parseSkillJson(content);
      }
      const history = authoringDocument(useAuthoring.getState(), historyTarget);
      setDocument({ undo: [...history.undo, before].slice(-historyLimit), redo: [] }, historyTarget);
      setDocument({ edited, version: document.version + 1 }, target);
    }
    setDocument({ variables: { ...current.variables!, document: result.document, value: result.value, availability: result.availability } }, target);
    if (currentCatalog) set({ elementCatalog: { kind: 'ready', items: result.elements } });
    else void authoringActions.listDocuments();
    return result.variableId;
  } finally {
    setDocument({ variablesWriting: false }, target);
  }
}

export function editVariable(ownerId: string, operation: VariableOperation | ((source: VariablesQuery['value']) => VariableOperation)): Promise<string> {
  const previous = variableRequests.get(ownerId);
  const apply = () => applyVariableEdit(ownerId, typeof operation === 'function'
    ? operation(variableDocument(useAuthoring.getState(), ownerId)![1].variables!.value) : operation);
  const request = (previous === undefined ? apply() : previous.then(apply, apply)).finally(() => {
    if (variableRequests.get(ownerId) === request) variableRequests.delete(ownerId);
  });
  variableRequests.set(ownerId, request);
  return request;
}
