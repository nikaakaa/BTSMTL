import { authoringApi, type ProjectItem, type ProjectReferrer } from '../api/authoring';
import { authoringActions, authoringDocument, currentDocument, layoutPath, useAuthoring } from './authoring';
import { graphKey } from './graphModel';
import type { JsonObject } from './skillJson';
import { numberOf } from './skillJson';
import { selectionActions } from './store';

const ownerOf = (item: ProjectItem) => ({ kind: 'skill', name: item.owner } as const);

async function openOwner(item: ProjectItem) {
  const target = ownerOf(item);
  if (!await authoringActions.open(target)) throw new Error(`未打开 ${item.ownerName}，具体原因见作者读取记录`);
  return authoringDocument(useAuthoring.getState(), target);
}

export const projectActions = {
  async open(item: ProjectItem, schemeIndex?: number): Promise<'graph' | 'timeline'> {
    const document = await openOwner(item);
    selectionActions.select(null);
    if (item.kind === 'skill' || item.kind === 'graph') {
      const id = item.kind === 'skill' ? document.programId : item.identity;
      const graph = document.documentGraphs?.find(value => value.id === id);
      if (!graph) throw new Error(document.documentError ?? '当前作者图查询尚未提供此图，无法读取其正式节点和端口');
      selectionActions.showGraph(graphKey(graph), null);
      return 'graph';
    }
    if (item.kind === 'timeline') selectionActions.showTimeline(item.identity, null);
    else {
      const plan = currentDocument(item.file) as JsonObject;
      await authoringActions.openInput(ownerOf(item), item.file, schemeIndex ?? numberOf(plan.Selected), plan.Actor as string);
    }
    return 'timeline';
  },

  async rename(item: ProjectItem, name: string, schemeIndex: number | null) {
    await openOwner(item);
    authoringActions.edit(item.file, value => {
      const document = value as JsonObject;
      switch (item.kind) {
        case 'skill': document.m_DisplayName = name; break;
        case 'graph': document.name = name; break;
        case 'timeline': document.m_Name = name; break;
        case 'input-plan': (document.Schemes as JsonObject[])[schemeIndex!].Name = name; break;
      }
    });
  },

  async remove(item: Exclude<ProjectItem, { kind: 'skill' }>): Promise<ProjectReferrer[]> {
    const referrers = await authoringApi.referrers(ownerOf(item), item.identity, authoringActions.editingView());
    if (referrers.length !== 0) return referrers;
    await openOwner(item);
    authoringActions.remove([item.file]);
    if (item.kind === 'graph' && currentDocument(layoutPath))
      authoringActions.edit(layoutPath, value => { delete (value as JsonObject)[item.identity]; });
    return [];
  },
};
