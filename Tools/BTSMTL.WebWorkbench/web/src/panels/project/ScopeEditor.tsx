import { useEffect, useState } from 'react';
import type { BusinessKeyMetadata } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { scopeQueryKey, type ResourceReference, type ScopeQuery } from '../../api/resourceRegistration';
import { authoringActions, scopeDocument, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { ResourceRegistrationEditor, type RegistrationResource } from './ResourceRegistrationEditor';
import styles from './project.module.css';

export function ScopeResourceSources({ scope, businessKeys }: { scope: ResourceReference; businessKeys: BusinessKeyMetadata[] }) {
  const [adapter, setAdapter] = useState<string | null>(null);
  const adapters = [...new Set(businessKeys.filter(item => item.target.package === scope.package)
    .flatMap(item => item.bindings.map(binding => binding.adapter)))];
  return <Fold title="独立 Scope 资源" defaultOpen><PanelBar wrap>
    <Select label="Scope 绑定适配者" value={adapter} placeholder="选择正式适配者"
      options={adapters.map(value => ({ value, label: value }))} onChange={setAdapter} />
    <Button disabled={adapter === null} onClick={() => authoringActions.activateScope({ scope, adapter: adapter! })}>打开 Scope 资源登记</Button>
    {adapters.length === 0 && <span className={styles.note}>该作者包尚未提供正式绑定适配者。</span>}
  </PanelBar></Fold>;
}

export function ScopeEditor({ query, businessKeys }: { query: ScopeQuery; businessKeys: BusinessKeyMetadata[] }) {
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
  if (document === undefined) return failure === null ? <div className={styles.note}>正在读取实际 Scope 的资源声明与绑定修订。</div> : <ErrorLine {...failure} />;
  const source = document.source;
  const resources: RegistrationResource[] = businessKeys.filter(item => item.target.package === source.scope.package)
    .flatMap(item => item.bindings.filter(binding => binding.adapter === source.adapter && binding.context === source.scope.context)
      .map(binding => ({ reference: { package: item.target.package, keyKind: item.target.keyKind, key: item.target.key, context: binding.context },
        token: binding.token, instanceCapacity: binding.instanceCapacity })));
  return <div onFocusCapture={() => authoringActions.activateScope(query)} onPointerDownCapture={() => authoringActions.activateScope(query)}>
    <div className={styles.facts}><strong>执行 Scope · {source.scope.key}</strong>
      <small>{source.scope.package} · {source.adapter} · 上下文 {source.scope.context || '公共'}</small></div>
    {failure && <ErrorLine {...failure} />}{document.failure && <ErrorLine {...document.failure} />}
    <PanelBar wrap>
      <Button disabled={document.writing} onClick={async () => {
        try { await authoringActions.openScope(query, undefined, true); setFailure(null); }
        catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) }); }
      }}>重读 Scope 来源</Button>
      <Button disabled={document.writing || !document.undo.length} onClick={() => authoringActions.undo()}>撤销</Button>
      <Button disabled={document.writing || !document.redo.length} onClick={() => authoringActions.redo()}>重做</Button>
    </PanelBar>
    <ResourceRegistrationEditor id={key} label="Scope" packageId={source.scope.package} adapter={source.adapter}
      declarations={source.resourceDeclarations} resources={resources} businessKeys={businessKeys} writing={document.writing}
      onRegister={(keyKind, input) => authoringActions.registerScopeResource(query, keyKind, input)} />
    {document.message && <div role="status" className={styles.note}>{document.message}</div>}
    <Fold title="Scope 身份"><div className={styles.facts}><small>类别：{source.scope.keyKind}</small><small>绑定修订：{source.revision}</small></div></Fold>
  </div>;
}
