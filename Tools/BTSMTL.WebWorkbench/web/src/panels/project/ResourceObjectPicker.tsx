import { useEffect, useRef, useState } from 'react';
import type { BusinessKeyMetadata } from '../../api/authoring';
import type { ProjectFileEntry } from '../../api/project';
import { resourceOperationError, resourceRegistrationApi, type ResourceObjectQuery, type ResourceObjects } from '../../api/resourceRegistration';
import type { ReferenceFieldSelection, ResourceAssetSelection, ResourceRegistrationSelection } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

type Failure = { text: string; entryId: number | null };

export function ResourceObjectPicker({ file, field, registration, businessKeys, failure }: {
  file: ProjectFileEntry; field: Pick<ReferenceFieldSelection, 'id' | 'name'>; registration: ResourceRegistrationSelection;
  businessKeys: BusinessKeyMetadata[] | null; failure: Failure | null;
}) {
  const [open, setOpen] = useState(false);
  const [adapter, setAdapter] = useState<string | null>(null);
  const capability = registration.objects;
  const reason = file.guid === null ? '所选文件没有正式资源 GUID，无法查询其对象。'
    : capability.kind === 'unavailable' ? capability.reason : null;
  const packageId = capability.kind === 'available' ? capability.scope.package : null;
  const adapters = [...new Set(businessKeys?.filter(item => item.target.package === packageId)
    .flatMap(item => item.bindings.map(binding => binding.adapter)) ?? [])];
  return <div className={styles.resourceObjects}>
    <PanelBar wrap>
      <Button variant="ghost" pressed={open} disabled={reason !== null} tip={reason ?? `选择${file.name}中的具体资源对象`}
        onClick={() => setOpen(!open)}>选择资源对象</Button>
      {open && <Select label="对象来源适配者" value={adapter} placeholder="选择正式适配者"
        options={adapters.map(value => ({ value, label: value }))} onChange={setAdapter} />}
    </PanelBar>
    {reason !== null && <div className={styles.note}>{reason}</div>}
    {open && reason === null && <>
      {failure !== null && <ErrorLine {...failure} />}
      {businessKeys === null && failure === null && <div className={styles.note}>正在读取作者包的正式绑定适配者。</div>}
      {businessKeys !== null && adapters.length === 0 && <div className={styles.issue}>当前作者包没有提供正式绑定适配者候选。</div>}
      {adapter !== null && file.guid !== null && capability.kind === 'available' && <ResourceObjectQueryView
        key={JSON.stringify([field.id, capability.scope, adapter, file.guid])}
        query={{ ...capability.scope, adapter, assetGuid: file.guid }} file={file.path} fieldName={field.name} onChoose={registration.choose} />}
    </>}
  </div>;
}

function ResourceObjectQueryView({ query, file, fieldName, onChoose }: {
  query: ResourceObjectQuery; file: string; fieldName: string; onChoose(asset: ResourceAssetSelection): void;
}) {
  const boundQuery = useRef(query).current;
  const [refresh, setRefresh] = useState(0);
  const [state, setState] = useState<{ kind: 'loading' } | { kind: 'loaded'; result: ResourceObjects } | { kind: 'failed'; failure: Failure }>({ kind: 'loading' });
  const [selectedToken, setSelectedToken] = useState<string | null>(null);
  const [chosen, setChosen] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setState({ kind: 'loading' }); setSelectedToken(null); setChosen(false);
    resourceRegistrationApi.objects(boundQuery, controller.signal).then(result => {
      if (!controller.signal.aborted) setState({ kind: 'loaded', result });
    }, error => {
      if (!controller.signal.aborted) setState({ kind: 'failed', failure: {
        text: resourceOperationError(error), entryId: consoleActions.error('资源对象候选', error),
      } });
    });
    return () => controller.abort();
  }, [boundQuery, refresh]);
  const result = state.kind === 'loaded' ? state.result : null;
  const selected = result?.available ? result.items.find(item => item.token === selectedToken) : undefined;
  return <>
    <PanelBar wrap><span>用于 {fieldName} 的素材对象</span>
      <Button variant="ghost" disabled={state.kind === 'loading'} onClick={() => setRefresh(value => value + 1)}>重读候选</Button>
    </PanelBar>
    {state.kind === 'loading' && <div role="status" className={styles.note}>正在读取所选素材的真实对象候选。</div>}
    {state.kind === 'failed' && <ErrorLine {...state.failure} />}
    {result !== null && !result.available && <div role="status" className={styles.note}>{result.reason}</div>}
    {result?.available && <>
      {result.items.length === 0 ? <div className={styles.note}>该素材没有符合当前字段能力的对象。</div>
        : <VirtualList className={styles.candidateList} count={result.items.length} rowHeight={44} label="所选素材的真实对象与组件"
          render={index => {
            const item = result.items[index];
            return <button className={styles.row} aria-selected={item.token === selectedToken}
              onClick={() => { setSelectedToken(item.token); setChosen(false); }}>
              <span>{item.hierarchy ? `${item.hierarchy} / ${item.name}` : item.name}</span><small>{item.type}</small>
            </button>;
          }} />}
      {selected !== undefined && <div className={styles.facts}>
        <strong>{selected.name}</strong><span>{selected.hierarchy}</span><small>{selected.type}</small>
        <Button onClick={() => {
          onChoose({ token: selected.token, adapter: boundQuery.adapter,
            source: `${file} · ${selected.hierarchy ? `${selected.hierarchy} / ` : ''}${selected.name} · ${selected.type}` });
          setChosen(true);
        }}>将所选对象交给 {fieldName} 登记</Button>
        {chosen && <span role="status">已选择具体对象，请回到该字段完成资源键与绑定登记。</span>}
      </div>}
      <Fold title="候选身份"><div className={styles.facts}>
        <small>候选修订：{result.revision}</small>
        {selected !== undefined && <small>资源令牌：{selected.token}</small>}
      </div></Fold>
    </>}
  </>;
}
