import type { AdapterCatalogDomain, AdapterCatalogItem } from '../api/authoring';
import type { WorkspaceDocumentInfo } from '../api/workspaceDocuments';
import type { ContentNode } from './projectContents';
import type { PreviewContentChoice } from './store';

// Project 树：作者内容、引擎资源、角色映射与场景四个根；文件夹没有 entry，叶子与可展开的对象带 entry。
export type ProjectEntry =
  | { kind: 'document'; document: WorkspaceDocumentInfo }
  | { kind: 'content'; node: ContentNode }
  | { kind: 'catalog'; items: AdapterCatalogItem[] }
  | { kind: 'scene'; choice: PreviewContentChoice };

export interface ProjectNode {
  key: string;
  label: string;
  detail: string;
  color: string;
  entry: ProjectEntry | null;
  children: ProjectNode[];
  // 所在位置，搜索结果与底部路径栏显示。
  location: string;
}

const colors = {
  folder: 'var(--text-disabled)', skill: 'var(--node-skill)', timeline: 'var(--track-anim)', graph: 'var(--node-flow)',
  curve: 'var(--track-motion)', profile: 'var(--node-cmp)', resource: 'var(--node-anim)', mapping: 'var(--node-cam)', scene: 'var(--track-act)',
};

export const catalogEntryKey = (item: Pick<AdapterCatalogItem, 'value' | 'context'>) => JSON.stringify([item.value, item.context]);
const isFilePath = (value: string) => /^(Assets|Packages)\//.test(value);
const folder = (key: string, label: string, location: string, children: ProjectNode[], detail = ''): ProjectNode =>
  ({ key, label, detail, color: colors.folder, entry: null, children, location });

function contentNode(node: ContentNode, location: string): ProjectNode {
  return { key: `content:${node.key}`, label: node.label, detail: node.description, color: colors.graph,
    entry: { kind: 'content', node }, children: node.children.map(child => contentNode(child, `${location}/${node.label}`)), location };
}

function documentNode(document: WorkspaceDocumentInfo, opened: ContentNode | undefined, location: string, color: string): ProjectNode {
  return { key: `document:${document.documentPath}`, label: document.name, detail: '', color,
    entry: { kind: 'document', document }, location,
    children: opened?.children.map(child => contentNode(child, `${location}/${document.name}`)) ?? [] };
}

function byDomain(documents: WorkspaceDocumentInfo[], location: string, color: string, opened: Map<string, ContentNode>): ProjectNode[] {
  const groups = new Map<string, WorkspaceDocumentInfo[]>();
  for (const document of documents) groups.set(document.contentDomain ?? '', [...groups.get(document.contentDomain ?? '') ?? [], document]);
  if (groups.size === 1) return documents.map(document => documentNode(document, opened.get(document.documentPath), location, color));
  return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([domain, items]) =>
    folder(`${location}/${domain}`, domain || '未分类', location,
      items.map(document => documentNode(document, opened.get(document.documentPath), `${location}/${domain}`, color)), `${items.length}`));
}

export function authoringRoot(directory: readonly WorkspaceDocumentInfo[], contents: readonly ContentNode[]): ProjectNode {
  const opened = new Map(contents.flatMap(root => root.target?.kind === 'item' ? [[root.target.item.owner, root] as const] : []));
  const sorted = (kind: WorkspaceDocumentInfo['kind']) => directory.filter(document => document.kind === kind)
    .sort((a, b) => a.name.localeCompare(b.name));
  const group = (kind: WorkspaceDocumentInfo['kind'], label: string, color: string, split: boolean) => {
    const documents = sorted(kind);
    const location = `作者内容/${label}`;
    return folder(location, label, '作者内容', split ? byDomain(documents, location, color, opened)
      : documents.map(document => documentNode(document, opened.get(document.documentPath), location, color)), `${documents.length}`);
  };
  return folder('作者内容', '作者内容', '', [
    group('skill', '技能', colors.skill, false),
    group('timeline', 'Timeline', colors.timeline, false),
    group('graph', '共享图', colors.graph, false),
    group('curve', '曲线', colors.curve, true),
    group('profile', 'Profile', colors.profile, true),
  ]);
}

// 同名资源（如多个 ActorDefinition）补上能区分它们的最短上级目录。
function distinctNames(values: readonly { value: string; display: string }[]): Map<string, string> {
  const names = new Map<string, string>();
  const byDisplay = new Map<string, string[]>();
  for (const item of values) byDisplay.set(item.display, [...new Set([...byDisplay.get(item.display) ?? [], item.value])]);
  for (const [display, group] of byDisplay) {
    if (group.length === 1) { names.set(group[0], display); continue; }
    const parts = group.map(value => value.split('/').slice(0, -1));
    for (const [index, value] of group.entries()) {
      let depth = 1;
      while (depth < parts[index].length && parts.some((other, at) => at !== index &&
        other.slice(-depth).join('/') === parts[index].slice(-depth).join('/'))) depth++;
      names.set(value, `${display}（${parts[index].slice(-depth).join('/')}）`);
    }
  }
  return names;
}

// 角色（执行范围）的作者可读名字：值 → 名字。
export const executionScopeNames = (domains: readonly AdapterCatalogDomain[]) =>
  distinctNames(domains.filter(group => group.domain === 'execution-scope').flatMap(group => group.items));

// 引擎资源：文件路径按文件夹展开，文件内对象挂在文件下；按角色配置的映射名按“角色 → 类型”分组。
export function catalogRoots(domains: readonly AdapterCatalogDomain[]): { files: ProjectNode; mappings: ProjectNode; scopes: Map<string, string> } {
  const items = domains.filter(group => group.domain !== 'resource-objects')
    .flatMap(group => group.items.map(item => ({ ...item, adapter: group.adapter, domain: group.domain })));
  const scopes = executionScopeNames(domains);
  const scopeName = (context: string) => context === '' ? '通用' : scopes.get(context) ?? context;
  const entries = new Map<string, AdapterCatalogItem[]>();
  for (const item of items) entries.set(catalogEntryKey(item), [...entries.get(catalogEntryKey(item)) ?? [], item]);

  type File = { own: AdapterCatalogItem[][]; parts: Map<string, AdapterCatalogItem[]> };
  type Folder = { folders: Map<string, Folder>; files: Map<string, File> };
  const tree: Folder = { folders: new Map(), files: new Map() };
  const mapping = new Map<string, Map<string, AdapterCatalogItem[][]>>();
  for (const group of entries.values()) {
    const { value, context } = group[0];
    if (!isFilePath(value)) {
      const domainsOf = mapping.get(context) ?? new Map<string, AdapterCatalogItem[][]>();
      mapping.set(context, domainsOf);
      for (const domain of new Set(group.map(item => item.domain)))
        domainsOf.set(domain, [...domainsOf.get(domain) ?? [], group.filter(item => item.domain === domain)]);
      continue;
    }
    const hash = value.indexOf('#');
    const file = hash < 0 ? value : value.slice(0, hash);
    const segments = file.split('/');
    let at = tree;
    for (const segment of segments.slice(0, -1)) {
      let next = at.folders.get(segment);
      if (next === undefined) at.folders.set(segment, next = { folders: new Map(), files: new Map() });
      at = next;
    }
    const name = segments[segments.length - 1];
    const entry: File = at.files.get(name) ?? { own: [], parts: new Map() };
    at.files.set(name, entry);
    if (hash < 0) entry.own.push(group);
    else entry.parts.set(JSON.stringify([value, context]), group);
  }
  const catalogNode = (group: AdapterCatalogItem[], label: string, location: string): ProjectNode => ({
    key: `catalog:${catalogEntryKey(group[0])}`, label: group[0].context === '' ? label : `${label}（${scopeName(group[0].context)}）`,
    detail: [...new Set(group.map(item => item.domain))].join(' · '), color: colors.resource,
    entry: { kind: 'catalog', items: group }, children: [], location });
  const build = (node: Folder, path: string): ProjectNode[] => [
    ...[...node.folders].sort(([a], [b]) => a.localeCompare(b)).map(([name, child]) =>
      folder(`files:${path}/${name}`, name, path, build(child, `${path}/${name}`))),
    ...[...node.files].sort(([a], [b]) => a.localeCompare(b)).flatMap(([name, file]) => {
      const location = `${path}/${name}`;
      const parts = [...file.parts.values()].map(group => catalogNode(group, group[0].display, location))
        .sort((a, b) => a.label.localeCompare(b.label));
      if (file.own.length === 0) return [folder(`files:${location}`, name, path, parts)];
      return file.own.map((group, index) => ({ ...catalogNode(group, index === 0 ? name : group[0].display, path),
        children: index === 0 ? parts : [] }));
    }),
  ];
  const files = folder('engine', '引擎资源', '', build(tree, '引擎资源'));
  const mappings = folder('mapping', '角色映射', '', [...mapping].sort(([a], [b]) => scopeName(a).localeCompare(scopeName(b)))
    .map(([context, domainsOf]) => {
      const location = `角色映射/${scopeName(context)}`;
      return folder(`mapping:${context}`, scopeName(context), '角色映射', [...domainsOf].sort(([a], [b]) => a.localeCompare(b))
        .map(([domain, groups]) => folder(`mapping:${context}:${domain}`, domain, location,
          groups.map(group => ({ ...catalogNode(group, group[0].display, `${location}/${domain}`), label: group[0].display, color: colors.mapping }))
            .sort((a, b) => a.label.localeCompare(b.label)), `${groups.length}`)));
    }));
  return { files, mappings, scopes };
}

export function scenesRoot(choices: readonly PreviewContentChoice[]): ProjectNode {
  const node = (choice: PreviewContentChoice, location: string): ProjectNode => ({ key: `scene:${choice.value}`, label: choice.label,
    detail: choice.current ? '' : '历史发布', color: colors.scene, entry: { kind: 'scene', choice }, children: [], location });
  const older = choices.filter(choice => !choice.current);
  return folder('scenes', '场景', '', [
    ...choices.filter(choice => choice.current).map(choice => node(choice, '场景')),
    ...older.length ? [folder('scenes:history', '历史发布', '场景', older.map(choice => node(choice, '场景/历史发布')), `${older.length}`)] : [],
  ]);
}

// 树中可被搜索与选中的全部节点，按深度优先顺序。
export function flatten(roots: readonly ProjectNode[]): ProjectNode[] {
  const result: ProjectNode[] = [];
  const visit = (node: ProjectNode) => { result.push(node); node.children.forEach(visit); };
  roots.forEach(visit);
  return result;
}
