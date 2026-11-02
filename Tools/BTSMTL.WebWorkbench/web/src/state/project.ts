import { authoringApi, authoringTargetKey, type ProjectItem, type ReferrersResult } from '../api/authoring';
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
  if (!graph) throw new Error(document.documentError ?? `${target.ownerName} 的当前作者文档中不存在图 ${target.id}`);
  const node = nodeId === undefined ? null : graph.nodes.find(node => node.id === nodeId);
  if (node === undefined) throw new Error(`${target.ownerName} 的图 ${target.id} 中不存在引用节点 ${nodeId}`);
  selectionActions.showGraph(graphKey(graph), null, origin);
  selectionActions.select(node === null ? null : { kind: 'node', id: nodeSelectId(graph, node) });
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
        if (currentDocument(target.file) === null)
          throw new Error(`无法打开 Timeline「${target.displayName ?? target.id}」：${target.ownerName} 的当前作者文档中不存在 ${target.file}`);
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

  async removeSkill(item: Extract<ProjectItem, { kind: 'skill' }>) {
    const target = ownerOf(item);
    const document = useAuthoring.getState().documents[authoringTargetKey(target)];
    const expectedRevision = document === undefined ? (await authoringApi.read(target)).revision : document.revision;
    if (expectedRevision === null) throw new Error('此技能尚未保存，请使用丢弃修改移除草稿。');
    const result = await authoringApi.deleteSkill(item.owner, { expectedRevision, editingView: authoringActions.editingView() });
    if (result.deleted) authoringActions.acceptSkillDeletion(result);
    return result;
  },
};
