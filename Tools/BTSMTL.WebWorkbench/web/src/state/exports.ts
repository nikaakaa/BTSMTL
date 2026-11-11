import { create } from 'zustand';
import { authoringApi, type AuthoringEditingView, type DocumentExport, type DocumentExportKind } from '../api/authoring';
import { consoleActions } from './store';

interface ExportRequest {
  id: string;
  documentPath: string;
  purpose: DocumentExportKind;
  product: AuthoringEditingView['product'];
  startedAt: number;
}

export type ExportOperation = ExportRequest & (
  | { kind: 'preparing' }
  | { kind: 'ready'; result: DocumentExport }
  | { kind: 'failed'; reason: string; entryId: number | null }
);

export const useExports = create<{ operations: ExportOperation[] }>()(() => ({ operations: [] }));

function update(operation: ExportOperation) {
  useExports.setState(state => ({ operations: state.operations.map(current => current.id === operation.id ? operation : current) }));
}

export const exportActions = {
  async prepare({ documentPath, purpose, editingView }: { documentPath: string; purpose: DocumentExportKind; editingView: AuthoringEditingView }) {
    const request = { id: crypto.randomUUID(), documentPath, purpose, product: editingView.product, startedAt: Date.now() };
    useExports.setState(state => ({ operations: [...state.operations, { ...request, kind: 'preparing' }] }));
    try {
      const result = await authoringApi.export(documentPath, purpose, editingView);
      update({ ...request, kind: 'ready', result });
    } catch (error) {
      const entryId = consoleActions.error('导出', error);
      update({ ...request, kind: 'failed', reason: error instanceof Error ? error.message : String(error), entryId });
    }
  },
};
