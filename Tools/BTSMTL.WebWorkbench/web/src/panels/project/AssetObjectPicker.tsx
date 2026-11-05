import { useEffect, useState } from 'react';
import { isAbort } from '../../api/http';
import { projectApi, type ProjectFolderListing } from '../../api/project';
import { resourceOperationError, resourceRegistrationApi, type ResourceObjects, type ResourceObjectQuery } from '../../api/resourceRegistration';
import type { ResourceObjectFieldSelection } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Empty, Fold, PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

type Failure = { text: string; entryId: number | null };
export function AssetObjectPicker({ fieldName, selection }: { fieldName: string; selection: ResourceObjectFieldSelection }) {
  const [folder, setFolder] = useState('');
  const [listing, setListing] = useState<ProjectFolderListing | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [file, setFile] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    setListing(null); setFailure(null);
    projectApi.folder(folder, controller.signal).then(setListing, error => {
      if (!isAbort(error)) setFailure({ text: resourceOperationError(error), entryId: consoleActions.error('选择资源文件', error) });
    });
    return () => controller.abort();
  }, [folder]);
  const capability = selection;
  return <Fold title={`为 ${fieldName} 选择实际资源对象`} defaultOpen>
    {capability.kind === 'unavailable' ? <div className={styles.issue}>{capability.reason}</div> : <>
      <PanelBar wrap><Button disabled={folder === ''} onClick={() => setFolder(folder.includes('/') ? folder.slice(0, folder.lastIndexOf('/')) : '')}>上级</Button>
        <code>{folder || '项目资源文件'}</code></PanelBar>
      {failure && <ErrorLine {...failure} />}
      {listing === null ? <Empty text="正在读取文件夹" /> : <VirtualList className={styles.candidateList} rowHeight={36}
        count={listing.folders.length + listing.files.length} label="选择资源文件" render={index => {
          if (index < listing.folders.length) {
            const item = listing.folders[index];
            return <button className={styles.row} onClick={() => { setFolder(item.path); setFile(null); }}><span>{item.name}/</span></button>;
          }
          const item = listing.files[index - listing.folders.length];
          return <button className={styles.row} aria-selected={file === item.path} onClick={() => setFile(item.path)}><span>{item.name}</span></button>;
        }} />}
      {file !== null && <ObjectCandidates key={JSON.stringify([file, capability.scope])}
        query={{ ...capability.scope, asset: file }} selection={capability} fieldName={fieldName} />}
    </>}
  </Fold>;
}

function ObjectCandidates({ query, selection, fieldName }: { query: ResourceObjectQuery; selection: Extract<ResourceObjectFieldSelection, { kind: 'available' }>; fieldName: string }) {
  const [state, setState] = useState<{ kind: 'loading' } | { kind: 'ready'; result: ResourceObjects } | { kind: 'failed'; failure: Failure }>({ kind: 'loading' });
  const [selected, setSelected] = useState<string | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    resourceRegistrationApi.objects(query, controller.signal).then(result => setState({ kind: 'ready', result }), error => {
      if (!isAbort(error)) setState({ kind: 'failed', failure: { text: resourceOperationError(error), entryId: consoleActions.error('实际资源对象', error) } });
    });
    return () => controller.abort();
  }, [query.adapter, query.domain, query.context, query.asset, query.objectType]);
  if (state.kind === 'loading') return <Empty text="正在读取目标 Editor 的实际对象候选" />;
  if (state.kind === 'failed') return <ErrorLine {...state.failure} />;
  const result = state.result;
  if (!result.available) return <div className={styles.issue}>{result.reason}</div>;
  const item = result.items.find(item => item.value === selected);
  return <>
    <VirtualList className={styles.candidateList} count={result.items.length} rowHeight={42} label="实际对象与组件" render={index => {
      const candidate = result.items[index];
      return <button className={styles.row} aria-selected={candidate.value === selected} onClick={() => setSelected(candidate.value)}>
        <span>{candidate.hierarchy ? `${candidate.hierarchy} / ${candidate.name}` : candidate.name}</span><small>{candidate.type}</small>
      </button>;
    }} />
    {failure && <ErrorLine {...failure} />}
    {item !== undefined && <div className={styles.facts}><code>{item.value}</code><Button onClick={() => {
      try { selection.choose(item); setFailure(null); }
      catch (error) { setFailure({ text: resourceOperationError(error), entryId: consoleActions.error('选择实际对象', error) }); }
    }}>将所选对象填入 {fieldName}</Button></div>}
    <small>实际候选修订：{result.revision}</small>
  </>;
}
