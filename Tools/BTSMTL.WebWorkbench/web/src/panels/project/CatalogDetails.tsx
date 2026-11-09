import { useMemo, useState } from 'react';
import { authoringApi, authoringTarget, type AdapterCatalogItem, type ReferenceReplacementCompleted, type ResourceTarget } from '../../api/authoring';
import { executionScopeDomain } from '../../api/resourceRegistration';
import { useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringActions, referenceReplacementConflicts, useAuthoring } from '../../state/authoring';
import { executionScopeNames } from '../../state/projectTree';
import { animationReferenceKind, useAnimationLibrary } from '../../state/resources';
import { consoleActions } from '../../state/store';
import { Facts, Fold, PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { AnimationDetails } from './AnimationDetails';
import { ReferenceReplacementImpact, ResourceReferenceReplacement, type ReferenceReplacementRefresh } from './ResourceReferenceReplacement';
import { ScopeEditor } from './ScopeEditor';
import { TargetReferrersFold } from './TargetReferrers';
import styles from './project.module.css';

// 检查器中的目录资源：同一路径（同一角色范围）可同时属于多个类型，逐个列出用途、引用者与引用替换。
export function CatalogDetails({ items }: { items: AdapterCatalogItem[] }) {
  const catalog = useAdapterCatalog();
  const scopes = useMemo(() => catalog.kind === 'ready' ? executionScopeNames(catalog.snapshot.domains) : new Map<string, string>(), [catalog]);
  const context = items[0].context;
  return <>
    <Fold title="资源" defaultOpen><Facts fields={[
      { displayName: '路径', value: items[0].value },
      { displayName: '可用范围', value: context === '' ? '所有角色' : `只属于 ${scopes.get(context) ?? context}` },
      { displayName: '类型', value: items.map(item => item.domain).join('、') },
    ]} /></Fold>
    {items.map(item => <CatalogUse key={item.domain} item={item} />)}
  </>;
}

function CatalogUse({ item }: { item: AdapterCatalogItem }) {
  const catalog = useAdapterCatalog();
  const authoring = useAuthoring();
  const [receipt, setReceipt] = useState<{ result: ReferenceReplacementCompleted; refresh: ReferenceReplacementRefresh | null } | null>(null);
  const animation = useAnimationLibrary(item.domain === animationReferenceKind);
  const clips = animation.kind === 'ready' ? animation.library.clips.filter(clip => clip.value === item.value) : [];
  const candidates = useMemo(() => catalog.kind === 'ready'
    ? catalog.snapshot.domains.flatMap(group => group.items.map(entry => ({ ...entry, adapter: group.adapter, domain: group.domain }))) : [], [catalog]);
  const target: ResourceTarget = { kind: 'Resource', domain: item.domain, value: item.value, context: item.context === '' ? null : item.context };
  const skill = authoring.target?.kind === 'skill';
  const openSkill = (name: string) => { void authoringActions.open(authoringTarget('skill', name)).catch(error => consoleActions.error('检查器', error)); };
  return <>
    {item.domain === animationReferenceKind && <Fold title="动画" defaultOpen>{animation.kind === 'ready'
      ? clips.length === 1 ? <AnimationDetails library={animation.library} clip={clips[0]} />
        : <div className={styles.note}>动画素材库里有 {clips.length} 段对应这个引用，无法确定预览哪一段。</div>
      : <div className={styles.note}>{animation.kind === 'loading' ? '正在读取动画素材库。' : animation.reason}</div>}</Fold>}
    {item.domain === executionScopeDomain &&<Fold title="角色" defaultOpen>
      <PanelBar wrap>
        <Button disabled={!skill} tip={skill ? undefined : '先打开一个技能'} onClick={() => authoringActions.selectExecutionScope('previewScope', item.value)}>设为当前技能的预览角色</Button>
        <Button disabled={!skill} tip={skill ? undefined : '先打开一个技能'} onClick={() => authoringActions.selectExecutionScope('runtimeScope', item.value)}>设为实际运行角色</Button>
      </PanelBar>
      <ScopeEditor query={{ adapter: item.adapter, scope: item.value }} />
    </Fold>}
    <TargetReferrersFold target={target} />
    <Fold title={`替换所有引用（${item.domain}）`}>
      {receipt !== null && <div className={styles.referenceDetails}>
        <PanelBar><span>已保存的引用替换</span><Button onClick={() => setReceipt(null)}>关闭</Button></PanelBar>
        <ReferenceReplacementImpact plan={receipt.result} onOpenSkill={openSkill} />
        {receipt.refresh?.failures.map((failure, index) => <ErrorLine key={index} {...failure} />)}
        {receipt.refresh?.conflicts.map((conflict, index) => <div key={index} className={styles.issue}>{conflict.owner} · {conflict.path}：{conflict.reason}</div>)}
      </div>}
      <ResourceReferenceReplacement source={{ domain: item.domain, value: item.value, display: item.display }}
        candidates={candidates} currentContext={target.context}
        conflicts={plan => referenceReplacementConflicts(plan, authoring)} onPrepare={authoringApi.prepareReferenceReplacement}
        onCommit={async plan => {
          const result = await authoringActions.commitReferenceReplacement(plan);
          setReceipt({ result, refresh: null }); return result;
        }} onRefresh={async result => {
          const refresh = await authoringActions.refreshReferenceReplacement(result);
          setReceipt(previous => previous?.result.revision === result.revision ? { ...previous, refresh } : previous);
          return refresh;
        }} onOpenSkill={openSkill} />
    </Fold>
  </>;
}
