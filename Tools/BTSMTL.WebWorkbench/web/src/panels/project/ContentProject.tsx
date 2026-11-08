import { useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { isAbort } from '../../api/http';
import { useAuthoring, useSkillElements } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { projectContents, type ContentNode } from '../../state/projectContents';
import { referenceValueKey, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { MenuButton, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { SkillContentCreation, type SkillContentKind } from './SkillContentCreation';
import styles from './project.module.css';

export function ContentProject() {
  const { documents, target, author } = useAuthoring(useShallow(state => ({ documents: state.documents, target: state.target, author: state.author })));
  const contents = useMemo(() => projectContents(documents), [documents]);
  const composition = target?.kind === 'skill' || target?.kind === 'timeline';
  const elements = useSkillElements(author && composition);
  const field = useReferenceSelection(state => state.field);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [selected, setSelected] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState<SkillContentKind | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const all = new Map<string, ContentNode>();
  const rows: { node: ContentNode; depth: number; open: boolean }[] = [];
  const query = search.toLocaleLowerCase();
  const matches = (node: ContentNode): boolean => `${node.label} ${node.description}`.toLocaleLowerCase().includes(query) || node.children.some(matches);
  const visit = (node: ContentNode, depth: number, visible: boolean) => {
    all.set(node.key, node);
    const open = expanded[node.key] ?? depth < 2;
    if (visible && (!query || matches(node))) rows.push({ node, depth, open });
    for (const child of node.children) visit(child, depth + 1, visible && (open || query !== ''));
  };
  for (const root of contents.tree) visit(root, 0, true);
  const node = selected === null ? undefined : all.get(selected);
  const owner = node?.target?.kind === 'item' ? node.target.item : node?.target;
  const perform = async (action: () => Promise<void>) => {
    setFailure(null);
    try { await action(); }
    catch (error) { if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) }); }
  };
  const create = (kind: SkillContentKind) => void perform(async () => {
    if (kind !== 'skill' && owner != null) await projectActions.openOwner(owner);
    setCreating(kind);
  });
  // 单击在检查器中查看并操作所选内容，Project 只负责找到和创建。
  return <div className={styles.contentGrid} data-creating={creating !== null || undefined}>
    <div className={styles.contentTree}>
      <PanelBar wrap><TextInput aria-label="搜索作者内容" placeholder="搜索技能、状态、Timeline 或片段" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
        <MenuButton label="新建作者内容" caret entries={[
          { label: '技能', onSelect: () => create('skill') },
          { label: '在当前文档中新建 Timeline', disabled: !composition, onSelect: () => create('timeline') },
          { label: '在当前文档中新建共享子图', disabled: !composition, onSelect: () => create('macro') },
          { label: '在当前文档中新建输入方案', disabled: !composition, onSelect: () => create('input') },
          ...(node?.target?.kind === 'node' ? [{ label: '在所选节点的引用字段中创建拥有内容',
            onSelect: () => void perform(() => projectActions.inspect(node.target!)) }] : []),
          ...(node?.target?.kind === 'item' && node.target.item.kind === 'timeline' ? [{ label: '在所选 Timeline 中添加轨道或片段',
            onSelect: () => void perform(() => projectActions.openContent(node.target!)) }] : []),
        ]}>新建</MenuButton>
      </PanelBar>
      {failure && <ErrorLine {...failure} />}
      {Object.entries(documents).filter(([, document]) => document.documentError !== null).map(([key, document]) =>
        <ErrorLine key={key} text={`${key}：${document.documentError}`} entryId={null} />)}
      {contents.tree.length === 0 ? <Empty text="从顶部选择技能，或新建技能后查看作者内容" /> : <VirtualList className={styles.contentList}
        count={rows.length} rowHeight={38} label="作者内容所有权树" render={index => {
          const { node, depth, open } = rows[index];
          const item = node.target?.kind === 'item' ? node.target.item : null;
          const choices = item !== null && field?.domain === null && field.catalog.kind === 'ready'
            ? field.catalog.items.filter(choice => referenceValueKey(choice.value) === referenceValueKey(item.identity)) : [];
          const choice = choices.length === 1 ? choices[0] : undefined;
          return <div className={styles.contentRow} style={{ paddingLeft: depth * 14 }} aria-selected={selected === node.key}>
            <button className={styles.arrow} aria-label={`${open ? '收起' : '展开'} ${node.label}`} disabled={node.children.length === 0}
              onClick={() => setExpanded({ ...expanded, [node.key]: !open })}>{node.children.length === 0 ? '·' : open ? '▾' : '▸'}</button>
            <button className={styles.contentName} draggable={choice !== undefined || item?.kind === 'graph' || item?.kind === 'timeline'}
              onDragStart={event => {
                if (choice !== undefined && field?.catalog.kind === 'ready') writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choice);
                else if (item?.kind === 'graph' || item?.kind === 'timeline') writeReferenceDrag(event.dataTransfer, item.kind,
                  { value: item.identity, source: `${item.owner}/${item.file}`, targets: [], associated: [] });
              }}
              onClick={() => { setSelected(node.key); setCreating(null); if (node.target !== null) void perform(() => projectActions.inspect(node.target!)); }}
              onDoubleClick={() => { if (node.target !== null) void perform(() => projectActions.openContent(node.target!)); }}>
              <span>{node.label}</span><small>{node.description}</small>
            </button>
          </div>;
        }} />}
    </div>
    {creating !== null && <div className={styles.contentDetails}>
      <SkillContentCreation key={`${creating}/${target?.documentPath}`} kind={creating} skill={composition && target !== null ? target.documentPath : null}
        elements={elements} onDone={() => setCreating(null)} />
    </div>}
  </div>;
}
