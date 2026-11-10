import { useEffect, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringActions, authoringDocument, isDocumentDirty, useAuthoring, variableRequests } from '../state/authoring';
import { PanelBar } from '../ui/blocks';
import { Button } from '../ui/controls';
import { finishAuthoringInputs } from './layout';
import { shellBridge } from './shellBridge';
import styles from './shell.module.css';

const unsaved = (state: ReturnType<typeof useAuthoring.getState>) => Object.entries(state.documents)
  .filter(([, document]) => isDocumentDirty(document))
  .map(([path]) => path);

const saving = (state: ReturnType<typeof useAuthoring.getState>) => Object.values(state.documents).some(document => document.saving);

function resolveExit(close: boolean) {
  shellBridge?.postMessage({ type: 'author-close-resolved', close });
}

export function AuthoringExit() {
  const dialog = useRef<HTMLDialogElement>(null);
  const [requested, setRequested] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const paths = useAuthoring(useShallow(unsaved));
  const busy = useAuthoring(saving);

  useEffect(() => {
    const bridge = shellBridge;
    if (bridge === null) {
      const beforeUnload = (event: BeforeUnloadEvent) => {
        const state = useAuthoring.getState();
        if (!saving(state) && unsaved(state).length === 0 && variableRequests.size === 0) return;
        event.preventDefault();
        event.returnValue = '';
      };
      window.addEventListener('beforeunload', beforeUnload);
      return () => window.removeEventListener('beforeunload', beforeUnload);
    }
    const request = async (event: MessageEvent) => {
      if (event.data?.type !== 'author-close-request') return;
      finishAuthoringInputs();
      await Promise.allSettled(variableRequests.values());
      const state = useAuthoring.getState();
      if (!saving(state) && unsaved(state).length === 0) { resolveExit(true); return; }
      setFailure(null);
      setRequested(true);
    };
    bridge.addEventListener('message', request);
    bridge.postMessage({ type: 'author-close-ready' });
    return () => bridge.removeEventListener('message', request);
  }, []);

  useEffect(() => {
    if (!requested) return;
    const current = dialog.current!;
    current.showModal();
    return () => current.close();
  }, [requested]);

  useEffect(() => {
    if (requested && !busy && !submitting && paths.length === 0) resolveExit(true);
  }, [busy, paths, requested, submitting]);

  const continueEditing = () => { setRequested(false); resolveExit(false); };
  const saveAndExit = async () => {
    setSubmitting(true);
    setFailure(null);
    try {
      await authoringActions.save();
      const state = useAuthoring.getState();
      if (unsaved(state).length > 0)
        setFailure(authoringDocument(state).lastSave?.message ?? '仍有未保存的修改，请处理后重试。');
    } finally {
      setSubmitting(false);
    }
  };

  return <dialog ref={dialog} className={styles.exitDialog} aria-labelledby="author-exit-title"
    onCancel={event => { event.preventDefault(); if (!submitting) continueEditing(); }}>
    <h2 id="author-exit-title">关闭工作台</h2>
    {busy || submitting ? <p>正在保存作者内容…</p> : <p>这些文档有未保存的修改。</p>}
    {paths.length > 0 && <ul>{paths.map(path => <li key={path}>{path}</li>)}</ul>}
    {failure && <p role="alert">{failure}</p>}
    <PanelBar>
      <Button disabled={busy || submitting} onClick={() => void saveAndExit()}>保存并退出</Button>
      <Button variant="ghost" disabled={busy || submitting} onClick={() => resolveExit(true)}>放弃并退出</Button>
      <Button variant="ghost" disabled={submitting} onClick={continueEditing} autoFocus>继续编辑</Button>
    </PanelBar>
  </dialog>;
}
