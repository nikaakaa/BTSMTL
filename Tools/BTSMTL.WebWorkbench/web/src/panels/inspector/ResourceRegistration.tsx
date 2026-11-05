import { createContext, useContext, useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type BusinessKeyMetadata, type ReferenceChoice } from '../../api/authoring';
import { resourceOperationError, type ResourceObjectCapability, type ResourceRegistrationInput, type ResourceRegistrationMetadata, type ResourceRegistrationResult } from '../../api/resourceRegistration';
import { authoringActions, authoringDocument, useAuthoring, useCurrentDocument } from '../../state/authoring';
import type { ReferenceCatalog, ResourceAssetSelection } from '../../state/referenceSelection';
import { getIn, type JsonObject, type JsonValue } from '../../state/skillJson';
import { consoleActions } from '../../state/store';
import { Fold } from '../../ui/blocks';
import { Button, FieldLabel, NumberInput, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { ReferencePicker } from './ReferencePicker';
import styles from './inspector.module.css';

export const AuthoringResourceLocation = createContext<{ file: string; memberPath: string[] } | null>(null);

export function ResourceRegistrationField({ id, label, value, configuration, catalog, onChoose, metadata, memberPath }: {
  id: string; label: string; value: JsonValue | undefined; configuration: JsonObject; catalog: ReferenceCatalog;
  onChoose(choice: ReferenceChoice): void; metadata: ResourceRegistrationMetadata; memberPath: string[];
}) {
  const location = useContext(AuthoringResourceLocation);
  const revision = useAuthoring(state => authoringDocument(state).revision);
  const { document } = useCurrentDocument('skill.json');
  const packageId = getIn(document ?? undefined, ['m_AuthorPackage']);
  const candidates = metadata.candidates;
  const objects: ResourceObjectCapability = typeof packageId !== 'string' ? { kind: 'unavailable', reason: '技能没有正式作者包声明。' }
    : !candidates.availability.available ? { kind: 'unavailable', reason: candidates.availability.reason }
    : { kind: 'available', scope: { package: packageId, keyKind: candidates.keyKind, domain: candidates.domain, objectType: candidates.objectType } };
  const [asset, setAsset] = useState<ResourceAssetSelection | null>(null);
  const [completed, setCompleted] = useState<ResourceRegistrationResult | null>(null);
  return <>
    <ReferencePicker id={id} label={label} value={value} configuration={configuration} catalog={catalog} onChoose={onChoose}
      resourceRegistration={location === null ? undefined : { keyKind: metadata.keyKind, objects,
        choose: asset => { setAsset(asset); setCompleted(null); } }} />
    {asset !== null && location !== null && <ResourceRegistrationForm key={JSON.stringify([asset, location, memberPath])}
      asset={asset} metadata={metadata} file={location.file} memberPath={[...location.memberPath, ...memberPath]}
      onComplete={result => { setCompleted(result); setAsset(null); }} />}
    {completed !== null && completed.revision === revision && <div role="status" className={styles.note}>
      {completed.reused ? '已复用' : '已登记'}资源键 {completed.reference.key}
      {completed.instanceCapacity === null ? '' : `，实际实例容量 ${completed.instanceCapacity}`}；作者数据已保存，运行内容待发布和采用。
    </div>}
    {location === null && <div className={styles.note}>当前字段尚未提供正式作者文档的写入位置。</div>}
  </>;
}

function ResourceRegistrationForm({ asset, metadata, file, memberPath, onComplete }: {
  asset: ResourceAssetSelection; metadata: ResourceRegistrationMetadata; file: string; memberPath: string[];
  onComplete(result: ResourceRegistrationResult): void;
}) {
  const { target, writing, version } = useAuthoring(useShallow(state => ({ target: state.target,
    writing: authoringDocument(state).saving || authoringDocument(state).loading, version: authoringDocument(state).version })));
  const { document } = useCurrentDocument('skill.json');
  const packageId = getIn(document ?? undefined, ['m_AuthorPackage']);
  const [keys, setKeys] = useState<BusinessKeyMetadata[] | null>(null);
  const [adapter, setAdapter] = useState<string>(asset.adapter);
  const [contextMode, setContextMode] = useState<'common' | 'specific' | null>(null);
  const [context, setContext] = useState('');
  const [key, setKey] = useState('');
  const [capacity, setCapacity] = useState<number | null>(null);
  const [effectKind, setEffectKind] = useState('');
  const [status, setStatus] = useState('');
  const [error, setError] = useState<{ text: string; entryId: number | null } | null>(null);
  useEffect(() => {
    if (target?.kind !== 'skill') return;
    const controller = new AbortController();
    authoringApi.project(target, authoringActions.editingView(), controller.signal).then(result => setKeys(result.businessKeys))
      .catch(error => { if (!controller.signal.aborted) setError({ text: String(error), entryId: consoleActions.error('资源登记', error) }); });
    return () => controller.abort();
  }, [target, version]);
  const packageKeys = keys?.filter(item => item.target.package === packageId) ?? [];
  const adapters = [...new Set([asset.adapter, ...packageKeys.flatMap(item => item.bindings.map(binding => binding.adapter))])];
  const actualContext = contextMode === 'common' ? '' : context;
  const matches = contextMode === null || adapter === null ? [] : packageKeys.filter(item => item.target.keyKind === metadata.keyKind &&
    item.bindings.some(binding => binding.adapter === adapter && binding.context === actualContext && binding.token === asset.token));
  const actualKey = matches.length === 1 ? matches[0].target.key : key;
  const selectedExisting = matches.length === 1 ? matches[0] : matches.find(item => item.target.key === key);
  const binding = selectedExisting?.bindings.find(binding => binding.adapter === adapter && binding.context === actualContext && binding.token === asset.token);
  const actualCapacity = selectedExisting === undefined ? capacity : binding!.instanceCapacity;
  const inputReady = metadata.inputKind === 'asset' || metadata.inputKind === 'instance-resource' && actualCapacity !== null && Number.isInteger(actualCapacity) && actualCapacity > 0 ||
    metadata.inputKind === 'camera-effect' && effectKind.trim() !== '';
  const canRegister = target?.kind === 'skill' && typeof packageId === 'string' && keys !== null && adapter !== null &&
    contextMode !== null && (contextMode === 'common' || context.trim() !== '') && actualKey.trim() !== '' &&
    (matches.length < 2 || selectedExisting !== undefined) && inputReady && !writing;
  const register = async () => {
    let input: ResourceRegistrationInput;
    switch (metadata.inputKind) {
      case 'asset': input = { kind: 'asset', key: actualKey, token: asset.token }; break;
      case 'instance-resource': input = { kind: 'instance-resource', key: actualKey, token: asset.token, instanceCapacity: actualCapacity! }; break;
      case 'camera-effect': input = { kind: 'camera-effect', key: actualKey, token: asset.token, effectKind }; break;
    }
    setError(null); setStatus('正在登记资源并保存当前作者编辑…');
    try {
      const result = await authoringActions.registerResource({ package: String(packageId), adapter: adapter!, context: actualContext,
        keyKind: metadata.keyKind, file, memberPath, input });
      onComplete(result);
    } catch (error) { setError({ text: resourceOperationError(error), entryId: consoleActions.error('资源登记', error) }); setStatus('登记未完成，当前编辑仍保留。'); }
  };
  return <Fold title="登记项目素材" defaultOpen>
    <div className={styles.note}>{asset.source}</div>
    {error && <ErrorLine {...error} />}
    {typeof packageId !== 'string' ? <div className={styles.issue}>技能没有正式作者包声明。</div> : <>
      <div className={styles.field}><FieldLabel label="绑定适配者" /><Select value={adapter} label="绑定适配者" placeholder="选择正式适配者"
        options={adapters.map(value => ({ value, label: value }))} onChange={value => { setAdapter(value); setKey(''); }} disabled={writing} /></div>
      {keys !== null && adapters.length === 0 && <div className={styles.issue}>当前正式目录没有提供该作者包的适配者候选。</div>}
      <div className={styles.field}><FieldLabel label="绑定上下文" /><Select value={contextMode} label="绑定上下文" placeholder="明确选择作用范围"
        options={[{ value: 'common', label: '公共（空上下文）' }, { value: 'specific', label: '指定上下文' }]}
        onChange={value => { setContextMode(value); setKey(''); }} disabled={writing} /></div>
      {contextMode === 'specific' && <div className={styles.field}><FieldLabel label="上下文" /><TextInput aria-label="绑定上下文名称"
        value={context} disabled={writing} onChange={event => { setContext(event.target.value); setKey(''); }} /></div>}
      <div className={styles.field}><FieldLabel label="资源键" />{matches.length > 1
        ? <Select value={selectedExisting?.target.key ?? null} label="选择实际资源键" placeholder="该素材绑定了多个键，明确选择"
          options={matches.map(item => ({ value: item.target.key, label: item.target.key }))} onChange={setKey} disabled={writing} />
        : matches.length === 1 ? <span>{actualKey}（复用已有绑定）</span>
          : <TextInput aria-label="新资源业务键" value={key} disabled={writing} onChange={event => setKey(event.target.value)} />}</div>
      {metadata.inputKind === 'instance-resource' && <div className={styles.field}><FieldLabel label="实例容量" tip="复用资源时保留正式绑定的实际容量" />
        {selectedExisting === undefined ? <NumberInput label="新资源实例容量" value={capacity} min={1} step={1} disabled={writing} onChange={setCapacity} />
          : <span>{binding!.instanceCapacity === null ? '已有绑定未配置容量，请在实际 Profile 中先配置' : binding!.instanceCapacity}</span>}</div>}
      {metadata.inputKind === 'camera-effect' && <div className={styles.field}><FieldLabel label="效果类别" />
        <TextInput aria-label="正式相机效果类别" value={effectKind} disabled={writing} onChange={event => setEffectKind(event.target.value)} /></div>}
      <Button disabled={!canRegister} onClick={() => void register()}>登记资源并保存当前编辑</Button>
    </>}
    {status && <div role="status" className={styles.note}>{status}</div>}
  </Fold>;
}
