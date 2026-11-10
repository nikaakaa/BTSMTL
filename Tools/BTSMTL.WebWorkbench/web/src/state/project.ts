import { authoringApi, authoringOwner, authoringTargetKey, type ProjectItem, type ReferrersResult } from '../api/authoring';
import { workspaceDocumentsApi, type WorkspaceDocumentElement, type WorkspaceDocumentInfo } from '../api/workspaceDocuments';
import { authoringActions, authoringDocument, currentDocument, dropOrphans, layoutPath, referencedFiles, useAuthoring } from './authoring';
import { graphKey, nodeSelectId, type GraphNavigationTarget, type NodeNavigationTarget } from './graphModel';
import type { JsonObject } from './skillJson';
import { numberOf } from './skillJson';
import { contentSelectionKey, getState, selectionActions, type GraphNavigationOrigin, type SelectionSlice } from './store';
import { openPanel } from '../shell/layout';
import { clipSelectId, trackSelectId } from './timelineModel';
import { projectContents, type ContentTarget } from './projectContents';

const ownerOf = (item: Pick<ProjectItem, 'owner'>) => authoringOwner(item.owner);

async function openOwner(item: Pick<ProjectItem, 'owner' | 'ownerName'>) {
  const target = ownerOf(item);
  const result = await authoringActions.open(target);
  switch (result) {
    case 'opened': return authoringDocument(useAuthoring.getState(), target);
    case 'cancelled': throw new DOMException('作者文档打开已取消', 'AbortError');
    case 'failed': throw new Error(`未打开 ${item.ownerName}，具体原因见作者读取记录`);
    default: { const unhandled: never = result; return unhandled; }
  }
}

type GraphNavigation = Pick<NonNullable<SelectionSlice['graphRequest']>, 'origin' | 'observation' | 'caller'> & { panelId: string | null };

export const documentElementKey = (element: WorkspaceDocumentElement) =>
  JSON.stringify([element.documentPath, element.relativePath, element.jsonPath, element.sourceIdentity]);

async function openGraph(target: Pick<GraphNavigationTarget, 'id' | 'owner' | 'ownerName'>, nodeId?: string,
  navigation: GraphNavigation | null = null): Promise<void> {
  const document = await openOwner(target);
  const graph = document.documentGraphs?.find(value => value.id === target.id);
  if (!graph) throw new Error(document.documentError ?? `${target.ownerName} 的当前作者文档中不存在图 ${target.id}`);
  const node = nodeId === undefined ? null : graph.nodes.find(node => node.id === nodeId);
  if (node === undefined) throw new Error(`${target.ownerName} 的图 ${target.id} 中不存在引用节点 ${nodeId}`);
  const panel = openPanel('graph', navigation?.panelId ?? undefined);
  selectionActions.showGraph({ key: graphKey(graph), panelId: panel.id, observation: navigation?.observation ?? null,
    caller: navigation?.caller ?? null, origin: navigation?.origin ?? null });
  selectionActions.select(node === null ? null : { kind: 'node', id: nodeSelectId(graph, node),
    call: navigation?.observation?.call, panelId: panel.id });
}

export const projectActions = {
  openGraph,
  openOwner,
  async openDocumentElement(element: WorkspaceDocumentElement): Promise<void> {
    await openOwner({ owner: element.documentPath, ownerName: element.documentPath });
    selectionActions.select({ kind: 'content', id: documentElementKey(element) });
    openPanel('inspector');
  },
  async openDocument(documentPath: string): Promise<void> {
    const document = await openOwner({ owner: documentPath, ownerName: documentPath });
    if (document.rootFile === null) {
      selectionActions.select({ kind: 'scene', id: contentSelectionKey(getState().contentSelection) ?? '' });
      openPanel('inspector');
      return;
    }
    const item = projectContents(useAuthoring.getState().documents).items.find(item => item.owner === documentPath && item.file === document.rootFile);
    if (item === undefined) throw new Error(`当前文档没有可打开的根文件：${documentPath}`);
    await projectActions.open(item);
  },
  async inspect(target: ContentTarget): Promise<void> {
    const owner = target.kind === 'item' ? target.item : target;
    const document = await openOwner(owner);
    if (target.kind === 'item') selectionActions.select({ kind: 'content', id: JSON.stringify([target.item.owner, target.item.file]) });
    else if (target.kind === 'node') {
      const graph = document.documentGraphs?.find(graph => graph.id === target.graphId);
      if (graph === undefined) throw new Error(document.documentError ?? '所选节点的正式作者图尚未读取完成。');
      const node = graph.nodes.find(node => node.id === target.nodeId);
      if (node === undefined) throw new Error('所选节点已不在当前作者图中。');
      selectionActions.select({ kind: 'node', id: nodeSelectId(graph, node) });
    } else selectionActions.select({ kind: target.kind, id: target.kind === 'clip'
      ? clipSelectId(target.timelineId, target.id) : trackSelectId(target.timelineId, target.id) });
    openPanel('inspector');
  },
  async openContent(target: ContentTarget): Promise<void> {
    if (target.kind === 'item') return projectActions.open(target.item);
    if (target.kind === 'node') return openGraph({ owner: target.owner, ownerName: target.ownerName, id: target.graphId }, target.nodeId);
    const item = projectContents(useAuthoring.getState().documents).items.find(item => item.kind === 'timeline' &&
      item.owner === target.owner && item.identity === target.timelineId)!;
    await projectActions.open(item);
    selectionActions.select({ kind: target.kind, id: target.kind === 'clip'
      ? clipSelectId(target.timelineId, target.id) : trackSelectId(target.timelineId, target.id) });
  },
  async openReference(target: NodeNavigationTarget, origin: GraphNavigationOrigin, panelId: string | null = null): Promise<void> {
    switch (target.kind) {
      case 'graph':
        await openGraph(target, undefined, { origin, caller: origin.observation, observation: null, panelId });
        return;
      case 'timeline':
        await openOwner(target);
        if (currentDocument(target.file) === null)
          throw new Error(`无法打开 Timeline「${target.displayName ?? target.id}」：${target.ownerName} 的当前作者文档中不存在 ${target.file}`);
        selectionActions.select(null);
        selectionActions.showTimeline(target.id, origin.observation, origin);
        openPanel('timeline');
        return;
    }
  },
  async open(item: ProjectItem, schemeIndex?: number): Promise<void> {
    const document = await openOwner(item);
    selectionActions.select(null);
    if (item.kind === 'skill' || item.kind === 'graph') {
      const id = item.kind === 'skill' ? document.programId : item.identity;
      if (item.kind === 'skill' && id === null && document.documentError === null) {
        openPanel('inspector');
        return;
      }
      const graph = document.documentGraphs?.find(value => value.id === id);
      if (!graph) throw new Error(document.documentError ?? '此作者图引用已失效，目录中的原身份已保留');
      selectionActions.showGraph({ key: graphKey(graph), panelId: openPanel('graph').id, observation: null, caller: null, origin: null });
      return;
    }
    if (item.kind === 'timeline') selectionActions.showTimeline(item.identity, null, null);
    else if (item.kind === 'input-plan') {
      const plan = currentDocument(item.file) as JsonObject;
      await authoringActions.openInput(ownerOf(item), item.file, schemeIndex ?? numberOf(plan.Selected), plan.Actor as string);
    } else {
      selectionActions.select({ kind: 'content', id: JSON.stringify([item.owner, item.file]) });
      openPanel('inspector');
      return;
    }
    openPanel('timeline');
  },

  async rename(item: Extract<ProjectItem, { kind: 'skill' | 'timeline' | 'input-plan' }>, name: string, schemeIndex: number | null) {
    await openOwner(item);
    authoringActions.edit(item.file, value => {
      const document = value as JsonObject;
      switch (item.kind) {
        case 'skill': document.m_DisplayName = name; break;
        case 'timeline': document.m_Name = name; break;
        case 'input-plan': (document.Schemes as JsonObject[])[schemeIndex!].Name = name; break;
      }
    });
  },

  async remove(item: Exclude<ProjectItem, { kind: 'skill' }>): Promise<ReferrersResult> {
    const referrers = await authoringApi.referrers(ownerOf(item), { identity: item.identity }, authoringActions.editingView());
    if (!referrers.complete || referrers.items.length !== 0) return referrers;
    await openOwner(item);
    // 与删除节点、片段一致：条目引用的专属图与 Timeline 不再被引用时随之删除。
    const seeds = referencedFiles(currentDocument(item.file) ?? null);
    authoringActions.remove([item.file]);
    if (item.kind === 'graph' && currentDocument(layoutPath))
      authoringActions.edit(layoutPath, value => { delete (value as JsonObject)[item.identity]; });
    dropOrphans(seeds);
    return referrers;
  },

  async removeSkill(item: Extract<ProjectItem, { kind: 'skill' }>) {
    const target = ownerOf(item);
    const document = useAuthoring.getState().documents[authoringTargetKey(target)];
    const expectedRevision = document === undefined ? (await workspaceDocumentsApi.open(target.documentPath)).revision : document.revision;
    if (expectedRevision === null) throw new Error('此技能尚未保存，请使用丢弃修改移除草稿。');
    const result = await authoringApi.deleteSkill(target.documentPath, { expectedRevision, editingView: authoringActions.editingView() });
    if (result.deleted) authoringActions.acceptSkillDeletion(result);
    return result;
  },
};

// 顶栏可直接切换的文档种类；曲线与 Profile 在 Project 中按用途分组，变量文档属于各自的图，从黑板进入。
export const openableDocumentGroups = [['skill', '技能'], ['timeline', '独立 Timeline'], ['graph', '共享图']] as const;

export const documentLabel = (document: WorkspaceDocumentInfo) => document.name;
