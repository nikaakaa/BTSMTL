import { create } from 'zustand';
import type { AdapterCatalogDomain, readAdapterCatalog } from '../api/authoring';
import { consoleActions } from './store';

export type AdapterCatalogSnapshot = { revision: string; domains: AdapterCatalogDomain[] };
type CatalogResult = { kind: 'ready'; snapshot: AdapterCatalogSnapshot }
  | { kind: 'failed'; failure: { text: string; entryId: number | null } };
type CatalogState = CatalogResult | { kind: 'unread' | 'loading' };
type CatalogReader = (revision: string | null) => Promise<ReturnType<typeof readAdapterCatalog>>;

export const useAdapterCatalog = create<CatalogState>(() => ({ kind: 'unread' }));
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
