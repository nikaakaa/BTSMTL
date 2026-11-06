import { useEffect, useState } from 'react';
import { isAbort } from '../../api/http';
import { scopeQueryKey, type ScopeQuery } from '../../api/resourceRegistration';
import { authoringActions, scopeDocument, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

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
    <div className={styles.facts}><strong>执行 Scope · {source.scope}</strong>
      <small>{source.adapter} · 上下文 {source.context || '公共'}</small></div>
    {failure && <ErrorLine {...failure} />}
    <PanelBar wrap>
      <Button onClick={async () => {
        try { await authoringActions.openScope(query, undefined, true); setFailure(null); }
        catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) }); }
      }}>重读 Scope 来源</Button>
    </PanelBar>
    <Fold title="资源领域声明" defaultOpen>{source.resourceDeclarations.map(item => <div key={item.domain} className={styles.facts}>
      <strong>{item.domain}</strong><small>{item.source === 'AuthoringDocument' ? '工作区作者文档' : `实际对象类型：${item.objectType}`}</small>
      {!item.availability.available && <span className={styles.issue}>{item.availability.reason}</span>}
    </div>)}</Fold>
    <div className={styles.note}>执行范围提供当前载体的资源上下文；映射与容量通过实际 Profile 编辑并保存。</div>
    <Fold title="Scope 身份"><div className={styles.facts}><small>来源修订：{source.revision}</small></div></Fold>
  </div>;
}
