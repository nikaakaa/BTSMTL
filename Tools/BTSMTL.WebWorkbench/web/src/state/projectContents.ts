import type { ProjectItem, SkillDocumentRelation } from '../api/authoring';
import { timelineOf, type AuthoringState } from './authoring';
import { getIn, parseSkillJson, type JsonObject } from './skillJson';

export type ContentTarget = { kind: 'item'; item: ProjectItem }
  | { kind: 'node'; owner: string; ownerName: string; graphId: string; nodeId: string }
  | { kind: 'track' | 'clip'; owner: string; ownerName: string; timelineId: string; id: string };
export interface ContentUse { key: string; label: string; target: ContentTarget }
export interface ContentNode {
  key: string; label: string; description: string; target: ContentTarget | null; children: ContentNode[]; referrers: ContentUse[];
}
const itemKey = (item: Pick<ProjectItem, 'owner' | 'file'>) => JSON.stringify([item.owner, item.file]);
const graphLabels: Record<string, string> = { Ability: '技能图', StateMachine: '状态机', StateBody: '状态内容',
  Condition: '条件图', Subgraph: '子图', TimelineEvent: '事件图' };

export function projectContents(documents: AuthoringState['documents']) {
  const items = new Map<string, ProjectItem>();
  const values = new Map<string, JsonObject>();
  const relations = new Map<string, SkillDocumentRelation>();
  const uses = new Map<string, ContentUse[]>();
  const children = new Map<string, string[]>();
  const roots: ProjectItem[] = [];
  for (const [key, document] of Object.entries(documents)) {
    if (document.kind === 'scenario') continue;
    const owner = key;
    const source = new Map([...new Set([...Object.keys(document.files), ...Object.keys(document.edited)])]
      .filter(path => !document.removed.includes(path) && (path === document.rootFile ||
        path.startsWith('graphs/') || path.startsWith('timelines/') || path.startsWith('inputs/'))).map(path => [path,
        document.edited[path] ?? parseSkillJson(document.files[path])]));
    const root = document.rootFile === null ? undefined : source.get(document.rootFile) as JsonObject | undefined;
    if (root === undefined) continue;
    const ownerName = document.kind === 'skill' ? root.m_DisplayName as string : document.kind === 'timeline' ? root.m_Name as string : owner;
    for (const [file, value] of source) {
      const data = value as JsonObject;
      const base = { owner, ownerName, file, referenceCount: 0 };
      let item: ProjectItem;
      if (file === 'skill.json') item = { ...base, kind: 'skill', identity: data.m_AbilityId as string,
        name: ownerName, ownership: 'Private', asset: data };
      else if (file.startsWith('graphs/')) item = { ...base, kind: 'graph', identity: data.AuthoringId as string,
        name: graphLabels[data.Role as string] ?? data.Role as string, role: data.Role as string,
        ownership: data.Ownership === 'Shared' ? 'Shared' : 'Private' };
      else if (file === 'timeline.json' || file.startsWith('timelines/')) item = { ...base, kind: 'timeline', identity: data.m_AuthoringId as string,
        name: data.m_Name as string, ownership: 'Private' };
      else if (file === document.rootFile && (document.kind === 'curve' || document.kind === 'profile'))
        item = { ...base, kind: document.kind, identity: data.m_AuthoringId as string, name: ownerName, ownership: 'Shared', asset: data };
      else if (file.startsWith('inputs/')) {
        const schemes = (data.Schemes as JsonObject[]).map((scheme, index) => ({ index, name: scheme.Name as string }));
        item = { ...base, kind: 'input-plan', identity: file.slice('inputs/'.length, -'.json'.length),
          name: schemes.map(scheme => scheme.name).join(' / '), ownership: 'Private', schemes };
      } else continue;
      items.set(itemKey(item), item);
      values.set(itemKey(item), data);
      if (file === document.rootFile) roots.push(item);
    }
    for (const relation of document.documentRelations) relations.set(JSON.stringify(relation), relation);
  }
  const byIdentity = new Map([...items].filter(([, item]) => item.kind === 'graph' || item.kind === 'timeline' || item.kind === 'input-plan').map(([key, item]) => [item.identity, key]));
  const addUse = (identity: string, use: ContentUse, parent = use.key) => {
    const key = byIdentity.get(identity);
    if (key === undefined) return;
    const current = uses.get(key) ?? [];
    if (!current.some(item => item.key === use.key)) current.push(use);
    uses.set(key, current);
    const targets = children.get(parent) ?? [];
    if (!targets.includes(key)) targets.push(key);
    children.set(parent, targets);
  };
  for (const relation of relations.values()) {
    const source = values.get(JSON.stringify([relation.source.owner, relation.source.file]));
    const node = (source?.allNodes as JsonObject[] | undefined)?.find(node => node.UID === relation.source.nodeId);
    if (node === undefined) continue;
    const identity = getIn(node, relation.source.serializedMemberPath);
    if (typeof identity !== 'string' || identity === '') continue;
    const target = byIdentity.get(identity);
    if (target !== undefined && items.get(target)!.kind === 'timeline' && node.m_Ownership === 'Shared') items.get(target)!.ownership = 'Shared';
    const key = JSON.stringify([relation.source.owner, relation.source.graphId, relation.source.nodeId]);
    const label = node._name as string ?? node.type as string;
    addUse(identity, { key: JSON.stringify([key, relation.source.serializedMemberPath]), label: `${relation.source.ownerName} / ${label} / ${relation.source.fieldId}`,
      target: { kind: 'node', owner: relation.source.owner, ownerName: relation.source.ownerName,
        graphId: relation.source.graphId, nodeId: relation.source.nodeId } }, key);
  }
  for (const [key, item] of items) {
    const data = values.get(key)!;
    if (item.kind === 'skill') addUse(data.m_AbilityGraph as string,
      { key, label: `${item.name} / 技能图`, target: { kind: 'item', item } });
    if (item.kind === 'input-plan') for (const [index, scheme] of (data.Schemes as JsonObject[]).entries())
      addUse(scheme.InputTimeline as string, { key: JSON.stringify([key, index]), label: `${item.ownerName} / ${item.schemes[index].name}`,
        target: { kind: 'item', item } }, key);
    if (item.kind !== 'timeline') continue;
    for (const track of timelineOf(item.identity, data).tracks) {
      for (const [index, clip] of track.clips.entries()) if (clip.graphId !== null)
        addUse(clip.graphId, { key: JSON.stringify([item.owner, item.identity, clip.id]),
          label: `${item.ownerName} / ${item.name} / ${track.displayName} / ${clip.displayName ?? clip.contract} ${index + 1}`,
          target: { kind: 'clip', owner: item.owner, ownerName: item.ownerName, timelineId: item.identity, id: clip.id } });
      for (const marker of track.markers) addUse(marker.graphId, { key: JSON.stringify([item.owner, item.identity, marker.id]),
        label: `${item.ownerName} / ${item.name} / ${track.displayName} / 标记 ${marker.seconds.toFixed(3)} 秒`,
        target: { kind: 'clip', owner: item.owner, ownerName: item.ownerName, timelineId: item.identity, id: marker.id } });
    }
  }
  for (const [key, item] of items) {
    item.referenceCount = uses.get(key)?.length ?? 0;
    if (item.referenceCount > 1) item.ownership = 'Shared';
    if (item.kind === 'graph') {
      const references = uses.get(key) ?? [];
      const names = [...new Set(references.map(reference => reference.label.split(' / ').slice(1, -1).join(' / ')).filter(Boolean))];
      if (names.length > 0) item.name = `${names.join('、')} · ${graphLabels[item.role] ?? item.role}`;
    }
  }
  const assigned = new Set<string>();
  const build = (key: string, path: ReadonlySet<string> = new Set()): ContentNode => {
    const item = items.get(key)!;
    const references = uses.get(key) ?? [];
    const node: ContentNode = { key, label: item.name, description: item.kind === 'graph' ? graphLabels[item.role] ?? item.role :
      item.kind === 'timeline' ? 'Timeline' : item.kind === 'input-plan' ? '输入方案' : item.kind === 'curve' ? '曲线文档' : item.kind === 'profile' ? 'Profile 文档' : '技能', target: { kind: 'item', item }, children: [], referrers: references };
    if (path.has(key)) return node;
    assigned.add(key);
    const next = new Set([...path, key]);
    const childItems = (sourceKey: string) => (children.get(sourceKey) ?? []).filter(key => items.get(key)!.ownership !== 'Shared').map(key => build(key, next));
    node.children.push(...childItems(key));
    if (item.kind === 'graph') {
      const data = values.get(key)!;
      for (const raw of data.allNodes as JsonObject[]) {
        const id = raw.UID as string;
        const key = JSON.stringify([item.owner, item.identity, id]);
        const label = raw._name as string ?? raw.type as string;
        node.children.push({ key, label, description: item.role === 'StateMachine' ? '状态' : '节点',
          target: { kind: 'node', owner: item.owner, ownerName: item.ownerName, graphId: item.identity, nodeId: id },
          children: childItems(key), referrers: [] });
      }
    }
    if (item.kind === 'timeline') for (const track of timelineOf(item.identity, values.get(key)!).tracks) {
      const trackNode: ContentNode = { key: JSON.stringify([key, track.id]), label: track.displayName, description: '轨道',
        target: { kind: 'track', owner: item.owner, ownerName: item.ownerName, timelineId: item.identity, id: track.id }, children: [], referrers: [] };
      for (const [index, clip] of track.clips.entries()) {
        const key = JSON.stringify([item.owner, item.identity, clip.id]);
        trackNode.children.push({ key, label: clip.displayName ?? `${clip.contract} ${index + 1}`, description: `${clip.startSeconds.toFixed(3)}–${clip.endSeconds.toFixed(3)} 秒`,
          target: { kind: 'clip', owner: item.owner, ownerName: item.ownerName, timelineId: item.identity, id: clip.id }, children: childItems(key), referrers: [] });
      }
      for (const marker of track.markers) {
        const key = JSON.stringify([item.owner, item.identity, marker.id]);
        trackNode.children.push({ key, label: `标记 ${marker.seconds.toFixed(3)} 秒`, description: '标记',
          target: { kind: 'clip', owner: item.owner, ownerName: item.ownerName, timelineId: item.identity, id: marker.id }, children: childItems(key), referrers: [] });
      }
      node.children.push(trackNode);
    }
    return node;
  };
  const tree = roots.map(item => build(itemKey(item)));
  for (const root of tree) {
    const owner = root.target!.kind === 'item' ? root.target!.item.owner : '';
    const owned = [...items].filter(([key, item]) => item.owner === owner && key !== root.key);
    const shared = owned.filter(([, item]) => item.ownership === 'Shared').map(([key]) => build(key));
    const unreferenced: ContentNode[] = [];
    for (const [key] of owned) if (!assigned.has(key) && !uses.has(key)) unreferenced.push(build(key));
    for (const [key] of owned) if (!assigned.has(key)) unreferenced.push(build(key));
    if (shared.length > 0) root.children.push({ key: `${root.key}/shared`, label: '共享', description: `${shared.length} 项`, target: null, children: shared, referrers: [] });
    if (unreferenced.length > 0) root.children.push({ key: `${root.key}/unreferenced`, label: '其他内容', description: `${unreferenced.length} 项`, target: null, children: unreferenced, referrers: [] });
  }
  const nameSiblings = (nodes: ContentNode[], parent: string) => {
    const counts = new Map<string, number>();
    for (const node of nodes) counts.set(node.label, (counts.get(node.label) ?? 0) + 1);
    const order = new Map<string, number>();
    for (const node of nodes) {
      const name = node.label;
      order.set(name, (order.get(name) ?? 0) + 1);
      if (counts.get(name)! > 1) node.label = `${name} · ${parent} / ${order.get(name)}`;
      nameSiblings(node.children, `${parent} / ${name}`);
    }
  };
  nameSiblings(tree, '已打开内容');
  return { tree, items: [...items.values()] };
}
