import { useEffect, useMemo, useState } from 'react';
import { authoringApi, authoringTarget, type AdapterCatalogItem, type ReferenceReplacementCompleted, type ResourceTarget } from '../../api/authoring';
import { scopeQueryKey } from '../../api/resourceRegistration';
import { adapterCatalogCache, useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringActions, authoringDocument, referenceReplacementConflicts, useAuthoring } from '../../state/authoring';
import { useResourceScope } from '../../state/resourceScope';
import { useReferenceSelection } from '../../state/referenceSelection';
import { animationReferenceKind, useAnimationLibrary } from '../../state/resources';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { AssetObjectPicker } from './AssetObjectPicker';
import { AnimationDetails } from './AnimationDetails';
import { ProfileSources } from './ProfileEditor';
import { ResourceCatalogView, type ResourceCatalogSelection } from './ResourceCatalogView';
import { ReferenceReplacementImpact, ResourceReferenceReplacement, type ReferenceReplacementRefresh } from './ResourceReferenceReplacement';
import { ScopeEditor } from './ScopeEditor';
import { ProductScope } from './ProductScope';
import { TargetReferrers } from './TargetReferrers';
import styles from './project.module.css';

export function ResourceProject() {
  const catalog = useAdapterCatalog();
  const field = useReferenceSelection(state => state.field);
  const authoring = useAuthoring();
  const document = authoringDocument(authoring);
  const owner = authoring.resourceTarget;
  const scope = useResourceScope(document.execution.previewScope);
  const [selected, setSelected] = useState<ResourceCatalogSelection | null>(null);
  const [receipt, setReceipt] = useState<{ source: string; result: ReferenceReplacementCompleted; refresh: ReferenceReplacementRefresh | null } | null>(null);
  useEffect(() => { void adapterCatalogCache.ensure(authoringApi.catalog); }, []);
  const currentScope = field?.domain != null ? field.resourceContext ?? null
    : scope.kind === 'ready' ? { adapter: scope.scope.adapter, context: scope.scope.context } : null;
  const items = useMemo(() => catalog.kind === 'ready'
    ? catalog.snapshot.domains.flatMap(group => group.items.map(item => ({ ...item, adapter: group.adapter, domain: group.domain }))) : [], [catalog]);
  const scopeChoices = useMemo(() => {
    const groups = new Map<string, AdapterCatalogItem[]>();
    for (const item of items.filter(item => item.domain === 'execution-scope')) groups.set(item.value, [...groups.get(item.value) ?? [], item]);
    return [...groups].map(([value, entries]) => ({ value, label: entries.map(item => `${item.display} · ${item.adapter}`).join(' / ') }));
  }, [items]);
  const item = selected?.kind === 'catalog' ? selected.item : null;
  const animation = useAnimationLibrary(item?.domain === animationReferenceKind);
  const clips = animation.kind === 'ready' && item?.domain === animationReferenceKind
    ? animation.library.clips.filter(clip => clip.value === item.value) : [];
  const target: ResourceTarget | null = selected === null ? null : { kind: 'Resource',
    domain: selected.kind === 'catalog' ? selected.item.domain : selected.domain,
    value: selected.kind === 'catalog' ? selected.item.value : selected.value,
    context: item !== null && item.context !== '' ? item.context : currentScope?.context ?? null };
  const sourceKey = target === null ? null : JSON.stringify(target);
  const openSkill = (name: string) => { void authoringActions.open(authoringTarget('skill', name)).catch(error => consoleActions.error('Project', error)); };
  return <div className={styles.root}>
    <PanelBar wrap><Select label="当前技能预览载体" value={document.execution.previewScope} placeholder="明确选择当前编辑载体"
      disabled={authoring.target?.kind !== 'skill'} options={scopeChoices} onChange={value => authoringActions.selectExecutionScope('previewScope', value)} />
      <Button variant="ghost" onClick={() => void adapterCatalogCache.refresh(authoringApi.catalog)}>刷新资源目录</Button></PanelBar>
    {scope.kind === 'unavailable' && <div className={styles.note}>{scope.reason}</div>}
    {field?.resourceObjects !== undefined && <AssetObjectPicker key={field.id} fieldName={field.name} selection={field.resourceObjects} />}
    {owner?.kind === 'scope' && <div className={styles.referenceDetails}>
      <PanelBar><span>Scope 声明</span><Button onClick={() => authoringActions.releaseScope(owner.query)}>关闭</Button></PanelBar>
      <ScopeEditor key={scopeQueryKey(owner.query)} query={owner.query} />
    </div>}
    {receipt !== null && receipt.source !== sourceKey && <div className={styles.referenceDetails}>
      <PanelBar><span>已保存的引用替换</span><Button onClick={() => setReceipt(null)}>关闭回执</Button></PanelBar>
      <ReferenceReplacementImpact plan={receipt.result} onOpenSkill={openSkill} />
      {receipt.refresh?.failures.map((failure, index) => <ErrorLine key={index} {...failure} />)}
      {receipt.refresh?.conflicts.map((conflict, index) => <div key={index} className={styles.issue}>{conflict.owner} · {conflict.path}：{conflict.reason}</div>)}
    </div>}
    {catalog.kind === 'failed' ? <ErrorLine {...catalog.failure} /> : catalog.kind !== 'ready' ? <Empty text="正在读取适配者资源目录" />
      : <ResourceCatalogView domains={catalog.snapshot.domains} revision={catalog.snapshot.revision} currentScope={currentScope}
        field={field} selected={selected} onSelect={setSelected} details={target === null || selected === null ? null : <>
          {item?.domain === animationReferenceKind && (animation.kind === 'ready'
            ? clips.length === 1 ? <AnimationDetails library={animation.library} clip={clips[0]} />
              : <div className={styles.note}>当前素材库对该引用提供 {clips.length} 个片段，无法确定预览对象；作者引用值仍保留。</div>
            : <div className={styles.note}>{animation.kind === 'loading' ? '正在读取动画素材库。' : animation.reason}</div>)}
          {item?.domain === 'execution-scope' && <PanelBar wrap>
            <Button disabled={authoring.target?.kind !== 'skill'} onClick={() => authoringActions.selectExecutionScope('previewScope', item.value)}>设为预览 Scope</Button>
            <Button disabled={authoring.target?.kind !== 'skill'} onClick={() => authoringActions.selectExecutionScope('runtimeScope', item.value)}>设为运行 Scope</Button>
            <Button onClick={() => authoringActions.activateScope({ adapter: item.adapter, scope: item.value })}>查看 Scope 声明</Button>
          </PanelBar>}
          <TargetReferrers key={sourceKey} target={target} />
          <ResourceReferenceReplacement key={sourceKey} source={{ domain: target.domain, value: target.value,
            display: item?.display ?? target.value }} candidates={items} currentContext={target.context}
            conflicts={plan => referenceReplacementConflicts(plan, authoring)} onPrepare={authoringApi.prepareReferenceReplacement}
            onCommit={async plan => {
              const result = await authoringActions.commitReferenceReplacement(plan);
              setReceipt({ source: sourceKey!, result, refresh: null }); return result;
            }} onRefresh={async result => {
              const refresh = await authoringActions.refreshReferenceReplacement(result);
              setReceipt(previous => previous?.result.revision === result.revision ? { ...previous, refresh } : previous);
              return refresh;
            }} onOpenSkill={openSkill} />
        </>} />}
    {authoring.target?.kind === 'skill' && <ProductScope />}
    <ProfileSources />
  </div>;
}
