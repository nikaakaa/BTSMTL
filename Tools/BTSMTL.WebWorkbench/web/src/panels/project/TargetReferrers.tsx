import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type AuthoringTarget, type ReferenceChoiceTarget, type ReferrersResult } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions, selectedScene, useWorkbench } from '../../state/store';
import { Empty, Fold } from '../../ui/blocks';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

export function TargetReferrersFold({ target }: { target: ReferenceChoiceTarget }) {
  const { document, stamp } = useAuthoring(useShallow(state => ({
    document: state.target,
    stamp: JSON.stringify([state.carrierRevision, Object.entries(state.documents).map(([key, document]) =>
      [key, document.revision, document.version, document.execution])]),
  })));
  const scene = useWorkbench(state => selectedScene(state));
  const key = JSON.stringify([document, target, stamp, scene]);
  return <Fold key={key} title="引用者">
    {document === null ? <Empty text="打开作者文档后查看此目标的引用者" />
      : <TargetReferrers document={document} target={target} />}
  </Fold>;
}

type Result =
  | { kind: 'loading' }
  | { kind: 'failed'; text: string; entryId: number | null }
  | { kind: 'ready'; value: ReferrersResult };

function TargetReferrers({ document, target }: { document: AuthoringTarget; target: ReferenceChoiceTarget }) {
  const [request] = useState(() => ({ document, target }));
  const [result, setResult] = useState<Result>({ kind: 'loading' });
  useEffect(() => {
    const controller = new AbortController();
    const timer = setTimeout(() => {
      authoringApi.referrers(request.document, { target: request.target }, authoringActions.editingView(), controller.signal)
        .then(value => {
          controller.signal.throwIfAborted();
          setResult({ kind: 'ready', value });
        }).catch(error => {
          if (!isAbort(error)) setResult({ kind: 'failed', text: '引用者读取失败', entryId: consoleActions.error('Project', error) });
        });
    }, 150);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [request]);
  if (result.kind === 'loading') return <Empty text="正在读取引用者" />;
  if (result.kind === 'failed') return <ErrorLine text={result.text} entryId={result.entryId} />;
  const { items, complete, unresolved } = result.value;
  return <>
    <div className={styles.heading}>已确认 {items.length} 项引用{complete ? '' : ' · 查询未完整'}</div>
    {complete && items.length === 0 && <div className={styles.note}>没有引用者</div>}
    {unresolved.map((issue, index) => <div key={index} className={styles.facts}>
      <span className={styles.issue}>{issue.reason}</span>
      {'owner' in issue && <small>{issue.ownerName} · {issue.file} · {issue.path}</small>}
    </div>)}
    {items.length !== 0 && <VirtualList className={styles.referrerList} count={items.length} rowHeight={56} label="目标引用者" render={index => {
      const reference = items[index];
      return <button className={styles.row} disabled={!('graphId' in reference)} onClick={() => {
        if ('graphId' in reference) void projectActions.openGraph({ owner: reference.owner, ownerName: reference.ownerName, id: reference.graphId }, reference.nodeId)
          .catch(error => consoleActions.error('Project', error));
      }}>
        <span>{reference.ownerName} · {reference.file}</span><small>{reference.path}</small>
        {'contextOwnerName' in reference && <small>调用技能：{reference.contextOwnerName} · 字段 {reference.fieldId}</small>}
      </button>;
    }} />}
  </>;
}
