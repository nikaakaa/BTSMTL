import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { adapterCatalogCache, useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringActions, useAuthoring, useSkillElements } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { projectContents } from '../../state/projectContents';
import { session } from '../../state/session';
import { authoringRoot, catalogEntryKey, catalogRoots, flatten, scenesRoot, type ProjectNode } from '../../state/projectTree';
import { referenceValueKey, resourceReferenceChoice, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions, contentActions, previewContentChoices, selectionActions, useWorkbench } from '../../state/store';
import { usePanelApi } from '../../shell/panel';
import { panelInfo } from '../../shell/panelTypes';
import { openPanel } from '../../shell/layout';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, TextInput, Tip } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { AssetObjectPicker } from './AssetObjectPicker';
import { ProjectFiles } from './ProjectFiles';
import { SkillContentCreation, type SkillContentKind } from './SkillContentCreation';
import styles from './project.module.css';

type Failure = { text: string; entryId: number | null };
type Row = { node: ProjectNode; depth: number; open: boolean };

// 照 Unity Project：左边文件夹树，右边当前文件夹的内容；面板窄时合成一列树。
// 单击在检查器查看，双击进入编辑器；从引用字段点“浏览”时按该字段筛选、定位当前值，双击填入。
export function ProjectPanel() {
  const panel = usePanelApi();
  useEffect(() => panel.setTitle(panelInfo.resources.title), [panel]);
  const root = useRef<HTMLDivElement>(null);
  const [wide, setWide] = useState(false);
  useLayoutEffect(() => {
    const element = root.current!;
    const owner = element.ownerDocument.defaultView as (Window & { ResizeObserver: typeof ResizeObserver }) | null;
    if (!owner) return;
    const observer = new owner.ResizeObserver(() => setWide(element.clientWidth >= 520));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const { documents, directory, target, author } = useAuthoring(useShallow(state => ({
    documents: state.documents, directory: state.directory, target: state.target, author: state.author })));
  useEffect(() => { if (directory === null) void authoringActions.listDocuments(); }, [directory]);
  const catalog = useAdapterCatalog();
  useEffect(() => { void adapterCatalogCache.ensure(authoringApi.catalog); }, []);
  const { sources, hostAvailable } = useWorkbench(useShallow(state => ({ sources: state.sources, hostAvailable: state.hostAvailable })));
  const { field, browsing } = useReferenceSelection(useShallow(state => ({ field: state.field, browsing: state.browsing })));
  const picking = browsing && field !== null && field.domain !== null ? field : null;

  const contents = useMemo(() => projectContents(documents).tree, [documents]);
  const author_ = useMemo(() => authoringRoot(directory ?? [], contents), [directory, contents]);
  const engine = useMemo(() => catalog.kind === 'ready' ? catalogRoots(catalog.snapshot.domains) : null, [catalog]);
  const scenes = useMemo(() => scenesRoot(sources ? previewContentChoices(sources) : null,
    hostAvailable === false ? '未接入运行宿主' : '正在读取'), [sources, hostAvailable]);
  const roots = useMemo(() => {
    const all = [author_, ...engine ? [engine.files, engine.mappings] : [], scenes];
    if (picking === null) return all;
    const keep = (node: ProjectNode): ProjectNode | null => {
      if (node.entry?.kind === 'catalog') return node.entry.items.some(item => item.domain === picking.domain) ? { ...node, children: [] } : null;
      const children = node.children.map(keep).filter((child): child is ProjectNode => child !== null);
      return node.entry === null && children.length ? { ...node, children } : null;
    };
    return all.map(keep).filter((node): node is ProjectNode => node !== null);
  }, [author_, engine, scenes, picking?.domain]);
  const nodes = useMemo(() => flatten(roots), [roots]);
  const parents = useMemo(() => {
    const map = new Map<string, string>();
    const visit = (node: ProjectNode) => node.children.forEach(child => { map.set(child.key, node.key); visit(child); });
    roots.forEach(visit);
    return map;
  }, [roots]);

  const [expanded, setExpanded] = useState<Set<string>>(() => new Set(['作者内容', '作者内容/技能']));
  const [selected, setSelected] = useState<string | null>(null);
  const [current, setCurrent] = useState('作者内容/技能');
  const [search, setSearch] = useState('');
  const [files, setFiles] = useState(false);
  const [creating, setCreating] = useState<SkillContentKind | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const composition = target?.kind === 'skill' || target?.kind === 'timeline';
  const elements = useSkillElements(author && composition);
  const byKey = useMemo(() => new Map(nodes.map(node => [node.key, node])), [nodes]);

  // 浏览引用字段时展开到当前值并选中它。
  const pickingValue = picking === null ? null : referenceValueKey(picking.value);
  useEffect(() => {
    if (picking === null) return;
    setSearch('');
    const hit = nodes.find(node => node.entry?.kind === 'catalog' && node.entry.items.some(item =>
      item.domain === picking.domain && referenceValueKey(item.value) === pickingValue));
    const ancestors: string[] = [];
    for (let key = hit ? parents.get(hit.key) : undefined; key !== undefined; key = parents.get(key)) ancestors.push(key);
    setExpanded(previous => new Set([...previous, ...ancestors]));
    if (hit) { setSelected(hit.key); setCurrent(ancestors[0] ?? hit.key); }
    else if (roots.length) setCurrent(roots[0].key);
  }, [picking?.id, picking?.domain, pickingValue, nodes]);

  const perform = async (action: () => Promise<void>) => {
    setFailure(null);
    try { await action(); }
    catch (error) { if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) }); }
  };
  const toggle = (key: string) => setExpanded(previous => {
    const next = new Set(previous);
    if (!next.delete(key)) next.add(key);
    return next;
  });
  const choose = (node: ProjectNode) => {
    if (picking === null || node.entry?.kind !== 'catalog' || picking.catalog.kind !== 'ready') return false;
    const value = node.entry.items.find(item => item.domain === picking.domain)?.value;
    const choices = picking.catalog.items.filter(choice => referenceValueKey(choice.value) === referenceValueKey(value));
    if (choices.length !== 1 || choices[0].conflict !== null) {
      setFailure({ text: choices.length === 0 ? '这一项不能填入当前字段。' : choices[0]?.conflict ?? '这一项对应多条目录，无法确定用哪一条。', entryId: null });
      return true;
    }
    picking.choose(choices[0]);
    useReferenceSelection.setState({ browsing: false });
    return true;
  };
  const select = (node: ProjectNode) => {
    setSelected(node.key);
    setCreating(null);
    const entry = node.entry;
    if (entry === null) return;
    switch (entry.kind) {
      case 'document': void perform(() => projectActions.openDocument(entry.document.documentPath)); return;
      case 'content': if (entry.node.target !== null) void perform(() => projectActions.inspect(entry.node.target!)); return;
      case 'catalog':
        selectionActions.select({ kind: 'catalog', id: catalogEntryKey(entry.items[0]) });
        openPanel('inspector');
        return;
      case 'scene':
        contentActions.selectContent(entry.choice.selection);
        selectionActions.select({ kind: 'scene', id: entry.choice.value });
        openPanel('inspector');
        return;
    }
  };
  const open = (node: ProjectNode) => {
    if (choose(node)) return;
    if (node.entry?.kind === 'content' && node.entry.node.target !== null) void perform(() => projectActions.openContent(
      (node.entry as Extract<ProjectNode['entry'], { kind: 'content' }>).node.target!));
    else if (node.children.length) { if (wide) setCurrent(node.key); else toggle(node.key); }
  };
  const drag = (node: ProjectNode, transfer: DataTransfer) => {
    const entry = node.entry;
    if (entry?.kind === 'catalog') {
      const item = picking === null ? entry.items[0] : entry.items.find(value => value.domain === picking.domain) ?? entry.items[0];
      writeReferenceDrag(transfer, item.domain, resourceReferenceChoice(item, null));
    } else if (entry?.kind === 'content' && entry.node.target?.kind === 'item') {
      const item = entry.node.target.item;
      if (item.kind === 'graph' || item.kind === 'timeline')
        writeReferenceDrag(transfer, item.kind, { value: item.identity, source: `${item.owner}/${item.file}`, targets: [], associated: [] });
    }
  };
  const create = (kind: SkillContentKind) => { setFiles(false); setCreating(kind); };

  const query = search.trim().toLocaleLowerCase();
  const found = useMemo(() => query === '' ? [] : nodes.filter(node => node.entry !== null &&
    `${node.label} ${node.detail}`.toLocaleLowerCase().includes(query)), [nodes, query]);
  const treeRows = (folders: boolean): Row[] => {
    const rows: Row[] = [];
    const visit = (node: ProjectNode, depth: number) => {
      if (folders && node.entry !== null && node.children.length === 0) return;
      const open = expanded.has(node.key);
      rows.push({ node, depth, open });
      if (open) for (const child of node.children) visit(child, depth + 1);
    };
    roots.forEach(root => visit(root, 0));
    return rows;
  };
  const folderRows = wide && query === '' ? treeRows(true) : [];
  const folder = byKey.get(current) ?? roots[0];
  const listRows: Row[] = query !== '' ? found.map(node => ({ node, depth: 0, open: false }))
    : wide ? (folder?.children ?? []).map(node => ({ node, depth: 0, open: false })) : treeRows(false);
  const row = (item: Row, list: boolean) => {
    const { node, depth } = item;
    const expandable = node.children.length > 0 && !(list && (wide || query !== ''));
    return <div className={styles.treeRow} style={{ paddingLeft: 4 + depth * 12 }}
      aria-selected={list ? selected === node.key : current === node.key && wide}>
      <button className={styles.arrow} tabIndex={-1} aria-label={`${item.open ? '收起' : '展开'} ${node.label}`} disabled={!expandable}
        onClick={() => toggle(node.key)}>{expandable ? item.open ? '▾' : '▸' : ''}</button>
      <button className={styles.treeName} draggable={node.entry?.kind === 'catalog' || node.entry?.kind === 'content'}
        onDragStart={event => drag(node, event.dataTransfer)}
        onClick={() => { if (!list) { setCurrent(node.key); if (!expanded.has(node.key)) toggle(node.key); } else if (node.entry === null && !wide) toggle(node.key); else select(node); }}
        onDoubleClick={() => list ? open(node) : undefined}>
        <i className={node.entry === null ? styles.folderIcon : styles.itemIcon} style={{ ['--kind' as string]: node.color }} />
        <span>{node.label}</span>
        {query !== '' ? <small>{node.location}</small> : node.detail && <small>{node.detail}</small>}
      </button>
    </div>;
  };
  const selectedNode = selected === null ? undefined : byKey.get(selected);
  const status = selectedNode ? `${selectedNode.location ? `${selectedNode.location}/` : ''}${selectedNode.label}`
    : query !== '' ? `找到 ${found.length} 项` : folder ? folder.key.startsWith('files:') ? folder.key.slice(6) : folder.label : '';
  const loading = directory === null ? '正在读取作者内容' : catalog.kind === 'loading' || catalog.kind === 'unread' ? '正在读取资源目录' : null;

  return <div ref={root} className={styles.root}>
    <PanelBar>
      <TextInput aria-label="搜索 Project" placeholder="搜索技能、Timeline、资源…" value={search} onChange={event => setSearch(event.currentTarget.value)} />
      <MenuButton label="新建" caret entries={[
        { label: '技能', onSelect: () => create('skill') },
        { label: '在当前技能中新建 Timeline', disabled: !composition, onSelect: () => create('timeline') },
        { label: '在当前技能中新建共享子图', disabled: !composition, onSelect: () => create('macro') },
        { label: '在当前技能中新建输入方案', disabled: !composition, onSelect: () => create('input') },
      ]}>新建</MenuButton>
      <MenuButton label="Project 更多操作" variant="ghost" iconOnly entries={[
        { label: '刷新资源目录', onSelect: () => void adapterCatalogCache.refresh(authoringApi.catalog) },
        { label: '刷新场景列表', disabled: !hostAvailable, onSelect: () => void session.refresh() },
        { label: files ? '返回 Project' : '查看工作区文件（排查）', onSelect: () => setFiles(!files) },
      ]}>⋮</MenuButton>
    </PanelBar>
    {picking !== null && <div className={styles.picking}>
      <span>正在为「{picking.name}」选择 · 双击填入，或拖到字段上</span>
      <Button variant="ghost" onClick={() => useReferenceSelection.setState({ browsing: false })}>取消</Button>
    </div>}
    {browsing && field?.resourceObjects !== undefined && <AssetObjectPicker key={field.id} fieldName={field.name} selection={field.resourceObjects} />}
    {failure && <ErrorLine {...failure} />}
    {catalog.kind === 'failed' && <ErrorLine {...catalog.failure} />}
    {Object.entries(documents).filter(([, document]) => document.documentError !== null).map(([key, document]) =>
      <ErrorLine key={key} text={`${key}：${document.documentError}`} entryId={null} />)}
    {creating !== null ? <div className={styles.contentDetails}>
      <SkillContentCreation key={`${creating}/${target?.documentPath}`} kind={creating} skill={composition && target !== null ? target.documentPath : null}
        elements={elements} onDone={() => setCreating(null)} />
    </div> : files ? <div className={styles.body}><ProjectFiles /></div>
      : <div className={styles.columns} data-wide={wide || undefined}>
        {wide && query === '' && <VirtualList className={styles.folders} count={folderRows.length} rowHeight={22} label="Project 文件夹"
          render={index => row(folderRows[index], false)} />}
        {listRows.length === 0 ? <Empty text={loading ?? (query !== '' ? '没有匹配的内容' : '这个文件夹是空的')} />
          : <VirtualList className={styles.items} count={listRows.length} rowHeight={22} label="Project 内容"
            revealIndex={Math.max(0, listRows.findIndex(item => item.node.key === selected))} revealKey={`${picking?.id}/${selected}/${current}`}
            render={index => row(listRows[index], true)} />}
      </div>}
    <Tip name={status}><div className={styles.status} data-name>{status}</div></Tip>
  </div>;
}
