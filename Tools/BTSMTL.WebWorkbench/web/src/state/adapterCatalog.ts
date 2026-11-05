import { create } from 'zustand';
import type { AdapterCatalogDomain, AdapterCatalogItem, readAdapterCatalog } from '../api/authoring';
import { resourceReferenceChoice, type ReferenceCatalog } from './referenceSelection';
import { consoleActions } from './store';

export type AdapterCatalogSnapshot = { revision: string; domains: AdapterCatalogDomain[] };
type CatalogResult = { kind: 'ready'; snapshot: AdapterCatalogSnapshot }
  | { kind: 'failed'; failure: { text: string; entryId: number | null } };
type CatalogState = CatalogResult | { kind: 'unread' | 'loading' };
type CatalogReader = (revision: string | null) => Promise<ReturnType<typeof readAdapterCatalog>>;

export const useAdapterCatalog = create<CatalogState>(() => ({ kind: 'unread' }));

export function resourceCatalogChoices(snapshot: AdapterCatalogSnapshot, domain: string, context: string): ReferenceCatalog {
  const values = new Map<string, AdapterCatalogItem[]>();
  for (const group of snapshot.domains.filter(group => group.domain === domain))
    for (const item of group.items.filter(item => item.context === context || item.context === ''))
      values.set(item.value, [...values.get(item.value) ?? [], { ...item, adapter: group.adapter, domain: group.domain }]);
  const items = [...values.values()].flatMap(entries => {
    const exact = entries.filter(item => item.context === context);
    const selected = exact.length !== 0 ? exact : entries;
    const conflict = selected.length > 1 ? `领域 ${domain}、引用 ${selected[0].value}、上下文 ${context} 命中多条正式目录。` : null;
    return selected.map(item => resourceReferenceChoice(item, conflict));
  });
  return { kind: 'ready', referenceKind: domain, items,
    invalidReason: `当前引用不在领域 ${domain}、上下文 ${context} 的正式目录中，原字符串已保留。` };
}

let snapshot: AdapterCatalogSnapshot | null = null;
let pending: Promise<CatalogResult> | null = null;

function refresh(read: CatalogReader): Promise<CatalogResult> {
  const run = async (): Promise<CatalogResult> => {
    useAdapterCatalog.setState({ kind: 'loading' }, true);
    try {
      const response = await read(snapshot?.revision ?? null);
      if (response.changed) snapshot = { revision: response.revision, domains: response.domains };
      const result = { kind: 'ready' as const, snapshot: snapshot! };
      useAdapterCatalog.setState(result, true);
      return result;
    } catch (error) {
      const result = { kind: 'failed' as const, failure: { text: error instanceof Error ? error.message : String(error),
        entryId: consoleActions.error('资源目录', error) } };
      useAdapterCatalog.setState(result, true);
      return result;
    }
  };
  const request = pending === null ? run() : pending.then(run);
  pending = request;
  void request.then(() => { if (pending === request) pending = null; });
  return request;
}

export const adapterCatalogCache = {
  ensure(read: CatalogReader): Promise<CatalogResult> {
    if (pending !== null) return pending;
    const current = useAdapterCatalog.getState();
    return current.kind === 'ready' ? Promise.resolve(current) : refresh(read);
  },
  refresh,
};
