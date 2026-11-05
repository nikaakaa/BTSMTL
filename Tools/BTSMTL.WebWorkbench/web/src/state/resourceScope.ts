import { useEffect, useState } from 'react';
import { resourceRegistrationApi, scopeQueryKey, type AuthoringScope, type ScopeQuery } from '../api/resourceRegistration';
import { authoringApi } from '../api/authoring';
import { adapterCatalogCache, resourceCatalogChoices, useAdapterCatalog } from './adapterCatalog';
import { timelinePath, useCurrentDocument } from './authoring';
import type { JsonObject } from './skillJson';
import type { ReferenceCatalog } from './referenceSelection';
import { consoleActions } from './store';

export type ResourceScopeState = { kind: 'ready'; scope: AuthoringScope } | { kind: 'loading' } | { kind: 'unavailable'; reason: string };
let scopeCache: { revision: string; requests: Map<string, Promise<ResourceScopeState>> } | null = null;

function readResourceScope(revision: string, query: ScopeQuery): Promise<ResourceScopeState> {
  if (scopeCache?.revision !== revision) scopeCache = { revision, requests: new Map() };
  const key = scopeQueryKey(query);
  let request = scopeCache.requests.get(key);
  if (request === undefined) {
    request = resourceRegistrationApi.readScope(query).then<ResourceScopeState, ResourceScopeState>(
      scope => ({ kind: 'ready', scope }),
      error => {
        consoleActions.error('资源执行范围', error);
        return { kind: 'unavailable', reason: error instanceof Error ? error.message : String(error) };
      });
    scopeCache.requests.set(key, request);
  }
  return request;
}

export function useResourceScope(value: string | null): ResourceScopeState {
  const catalog = useAdapterCatalog();
  useEffect(() => { void adapterCatalogCache.ensure(authoringApi.catalog); }, []);
  const items = catalog.kind === 'ready' ? catalog.snapshot.domains.filter(group => group.domain === 'execution-scope')
    .flatMap(group => group.items.filter(item => item.value === value).map(item => ({ adapter: group.adapter, scope: item.value }))) : [];
  const query = items.length === 1 ? items[0] : null;
  const key = JSON.stringify([catalog.kind === 'ready' ? catalog.snapshot.revision : null, query === null ? null : scopeQueryKey(query)]);
  const [result, setResult] = useState<{ key: string; state: ResourceScopeState } | null>(null);
  useEffect(() => {
    if (query === null || catalog.kind !== 'ready') return;
    let active = true;
    void readResourceScope(catalog.snapshot.revision, query).then(state => {
      if (active) setResult({ key, state });
    });
    return () => { active = false; };
  }, [key]);
  if (catalog.kind === 'failed') return { kind: 'unavailable', reason: catalog.failure.text };
  if (catalog.kind !== 'ready') return { kind: 'loading' };
  if (value === null) return { kind: 'unavailable', reason: '当前编辑内容尚未选择执行 Scope。' };
  if (items.length === 0) return { kind: 'unavailable', reason: `执行 Scope 不在正式目录中，原字符串已保留：${value}` };
  if (items.length > 1) return { kind: 'unavailable', reason: `执行 Scope 命中多个适配者目录条目：${value}` };
  return result?.key === key ? result.state : { kind: 'loading' };
}

export function useTimelineResourceCatalog(documentId: string, domain: string): ReferenceCatalog {
  const { document } = useCurrentDocument(timelinePath(documentId));
  const scope = useResourceScope((document as JsonObject | null)?.m_AuthoringScope as string | null ?? null);
  const catalog = useAdapterCatalog();
  if (scope.kind !== 'ready') return { kind: 'unavailable', reason: scope.kind === 'loading' ? '正在读取 Timeline 的执行 Scope。' : scope.reason };
  if (catalog.kind === 'failed') return { kind: 'unavailable', reason: catalog.failure.text };
  if (catalog.kind !== 'ready') return { kind: 'unavailable', reason: '正在读取正式适配者资源目录。' };
  return resourceCatalogChoices(catalog.snapshot, domain, scope.scope.context);
}
