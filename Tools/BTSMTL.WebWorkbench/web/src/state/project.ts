import { authoringApi, type ProjectItem, type ReferrersResult } from '../api/authoring';
import { authoringActions, authoringDocument, currentDocument, layoutPath, useAuthoring } from './authoring';
import { graphKey, nodeSelectId, type GraphNavigationTarget, type NodeNavigationTarget } from './graphModel';
import type { JsonObject } from './skillJson';
import { numberOf } from './skillJson';
import { selectionActions, type GraphNavigationOrigin } from './store';

const ownerOf = (item: Pick<ProjectItem, 'owner'>) => ({ kind: 'skill', name: item.owner } as const);

async function openOwner(item: Pick<ProjectItem, 'owner' | 'ownerName'>) {
  const target = ownerOf(item);
  if (!await authoringActions.open(target)) throw new Error(`未打开 ${item.ownerName}，具体原因见作者读取记录`);
  return authoringDocument(useAuthoring.getState(), target);
}

async function openGraph(target: Pick<GraphNavigationTarget, 'id' | 'owner' | 'ownerName'>, nodeId?: string,
  origin: GraphNavigationOrigin | null = null): Promise<void> {
  const document = await openOwner(target);
  const graph = document.documentGraphs?.find(value => value.id === target.id);
  if (!graph) throw new Error(document.documentError ?? '此作者图引用已失效，原身份已保留');
  selectionActions.showGraph(graphKey(graph), null, origin);
  selectionActions.select(nodeId === undefined ? null : { kind: 'node', id: nodeSelectId(graph, graph.nodes.find(node => node.id === nodeId)!) });
}

export const projectActions = {
  openGraph,
  async openReference(target: NodeNavigationTarget, origin: GraphNavigationOrigin): Promise<'graph' | 'timeline'> {
    switch (target.kind) {
      case 'graph':
        await openGraph(target, undefined, origin);
        return 'graph';
      case 'timeline':
        await openOwner(target);
        selectionActions.select(null);
        selectionActions.showTimeline(target.id, null);
        return 'timeline';
    }
  },
  async open(item: ProjectItem, schemeIndex?: number): Promise<'graph' | 'timeline' | 'inspector'> {
    const document = await openOwner(item);
    selectionActions.select(null);
    if (item.kind === 'skill' || item.kind === 'graph') {
      const id = item.kind === 'skill' ? document.programId : item.identity;
      if (item.kind === 'skill' && id === null && document.documentError === null) return 'inspector';
      const graph = document.documentGraphs?.find(value => value.id === id);
      if (!graph) throw new Error(document.documentError ?? '此作者图引用已失效，目录中的原身份已保留');
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

  async remove(item: Exclude<ProjectItem, { kind: 'skill' }>): Promise<ReferrersResult> {
    const referrers = await authoringApi.referrers(ownerOf(item), { identity: item.identity }, authoringActions.editingView());
    if (!referrers.complete || referrers.items.length !== 0) return referrers;
    await openOwner(item);
    authoringActions.remove([item.file]);
    if (item.kind === 'graph' && currentDocument(layoutPath))
      authoringActions.edit(layoutPath, value => { delete (value as JsonObject)[item.identity]; });
    return referrers;
  },
};
