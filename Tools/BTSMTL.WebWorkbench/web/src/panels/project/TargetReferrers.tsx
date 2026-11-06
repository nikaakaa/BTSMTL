import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type ReferenceChoiceTarget, type ReferrersResult } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions, selectedScene, useWorkbench } from '../../state/store';
import { Empty } from '../../ui/blocks';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

type Result = { key: string } & (
  | { kind: 'loading' }
  | { kind: 'failed'; text: string; entryId: number | null }
  | { kind: 'ready'; value: ReferrersResult }
);

export function TargetReferrers({ target }: { target: ReferenceChoiceTarget }) {
  const { skill, stamp } = useAuthoring(useShallow(state => ({
    skill: state.target?.kind === 'skill' ? state.target.documentPath : null,
    stamp: JSON.stringify([state.carrierRevision, Object.entries(state.documents).map(([key, document]) =>
      [key, document.revision, document.version, document.execution])]),
  })));
  const scene = useWorkbench(state => selectedScene(state));
  const key = JSON.stringify([skill, target, stamp, scene]);
  const [result, setResult] = useState<Result | null>(null);
  useEffect(() => {
    if (skill === null) return;
    const controller = new AbortController();
    setResult({ key, kind: 'loading' });
    const timer = setTimeout(() => {
      authoringApi.referrers({ kind: 'skill', documentPath: skill }, { target }, authoringActions.editingView(), controller.signal)
        .then(value => {
          controller.signal.throwIfAborted();
          setResult({ key, kind: 'ready', value });
        }).catch(error => {
          if (!isAbort(error)) setResult({ key, kind: 'failed', text: '引用者读取失败', entryId: consoleActions.error('Project', error) });
        });
    }, 150);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [skill, key]);
  if (skill === null) return <Empty text="打开技能后查看此目标的引用者" />;
  if (result === null || result.key !== key || result.kind === 'loading') return <Empty text="正在读取引用者" />;
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
