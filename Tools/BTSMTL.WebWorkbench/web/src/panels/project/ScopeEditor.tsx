import { useEffect, useState } from 'react';
import type { BusinessKeyMetadata } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { scopeQueryKey, type ResourceReference, type ScopeQuery } from '../../api/resourceRegistration';
import { authoringActions, scopeDocument, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

export function ScopeResourceSources({ scope, businessKeys }: { scope: ResourceReference; businessKeys: BusinessKeyMetadata[] }) {
  const [adapter, setAdapter] = useState<string | null>(null);
  const adapters = [...new Set(businessKeys.filter(item => item.target.package === scope.package)
    .flatMap(item => item.bindings.map(binding => binding.adapter)))];
  return <Fold title="独立 Scope 资源" defaultOpen><PanelBar wrap>
    <Select label="Scope 绑定适配者" value={adapter} placeholder="选择正式适配者"
      options={adapters.map(value => ({ value, label: value }))} onChange={setAdapter} />
    <Button disabled={adapter === null} onClick={() => authoringActions.activateScope({ scope, adapter: adapter! })}>查看 Scope 声明</Button>
    {adapters.length === 0 && <span className={styles.note}>该作者包尚未提供正式绑定适配者。</span>}
  </PanelBar></Fold>;
}

export function ScopeEditor({ query }: { query: ScopeQuery }) {
  const document = useAuthoring(state => scopeDocument(state, query));
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const key = scopeQueryKey(query);
  useEffect(() => {
    const controller = new AbortController();
    authoringActions.openScope(query, controller.signal).catch(error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) });
    });
    return () => { controller.abort(); authoringActions.releaseScope(query); };
  }, [key]);
  if (document === undefined) return failure === null ? <div className={styles.note}>正在读取实际 Scope 的资源声明与来源修订。</div> : <ErrorLine {...failure} />;
  const source = document.source;
  return <div onFocusCapture={() => authoringActions.activateScope(query)} onPointerDownCapture={() => authoringActions.activateScope(query)}>
    <div className={styles.facts}><strong>执行 Scope · {source.scope.key}</strong>
      <small>{source.scope.package} · {source.adapter} · 上下文 {source.scope.context || '公共'}</small></div>
    {failure && <ErrorLine {...failure} />}
    <PanelBar wrap>
      <Button onClick={async () => {
        try { await authoringActions.openScope(query, undefined, true); setFailure(null); }
        catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) }); }
      }}>重读 Scope 来源</Button>
    </PanelBar>
    <Fold title="资源领域声明" defaultOpen>{source.resourceDeclarations.map(item => <div key={item.domain} className={styles.facts}>
      <strong>{item.domain}</strong><small>实际对象类型：{item.objectType}</small>
      {!item.availability.available && <span className={styles.issue}>{item.availability.reason}</span>}
    </div>)}</Fold>
    <div className={styles.note}>执行范围提供当前载体的资源上下文；映射与容量通过实际 Profile 编辑并保存。</div>
    <Fold title="Scope 身份"><div className={styles.facts}><small>来源修订：{source.revision}</small></div></Fold>
  </div>;
}
