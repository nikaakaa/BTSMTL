import { useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { isAbort } from '../../api/http';
import { authoringActions, graphAuthoring, useAuthoring, useSkillElements } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { projectContents, type ContentNode, type ContentTarget } from '../../state/projectContents';
import { referenceValueKey, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { SkillContentCreation, type SkillContentKind } from './SkillContentCreation';
import { SkillItemDetails } from './SkillItemDetails';
import styles from './project.module.css';

export function ContentProject() {
  const { documents, target, author } = useAuthoring(useShallow(state => ({ documents: state.documents, target: state.target, author: state.author })));
  const contents = useMemo(() => projectContents(documents), [documents]);
  const elements = useSkillElements(author && target?.kind === 'skill');
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
  return <div className={styles.contentGrid}>
    <div className={styles.contentTree}>
      <PanelBar wrap><TextInput aria-label="搜索作者内容" placeholder="搜索技能、状态、Timeline 或片段" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
        <MenuButton label="新建作者内容" caret entries={[
          { label: '技能', onSelect: () => create('skill') },
          { label: '在所属技能下新建 Timeline', disabled: target?.kind !== 'skill', onSelect: () => create('timeline') },
          { label: '在所属技能下新建共享子图', disabled: target?.kind !== 'skill', onSelect: () => create('macro') },
          { label: '在所属技能下新建输入方案', disabled: target?.kind !== 'skill', onSelect: () => create('input') },
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
    <div className={styles.contentDetails}>
      {creating !== null ? <SkillContentCreation key={`${creating}/${target?.documentPath}`} kind={creating} skill={target?.kind === 'skill' ? target.documentPath : null}
        elements={elements} onDone={() => setCreating(null)} /> : node === undefined ? <Empty text="单击查看检查器，双击打开图或 Timeline；拖动内容设置作者引用" /> : <>
        {node.target?.kind === 'item' ? <SkillItemDetails item={node.target.item} items={contents.items}
          stamp={JSON.stringify(Object.entries(documents).map(([key, document]) => [key, document.revision, document.version]))} onChanged={() => setSelected(null)} />
          : node.target !== null && <ContentObject key={node.key} node={node} target={node.target} perform={perform} />}
        {node.target?.kind === 'item' && node.target.item.kind === 'graph' && <div className={styles.note}>图的显示名称由所属节点、片段或 Timeline 推出，可从引用者进入对应对象修改。</div>}
        {node.referrers.length > 0 && <><div className={styles.heading}>当前打开文档中的引用者 · {node.referrers.length}</div>
          {node.referrers.map(reference => <button key={reference.key} className={styles.referrer}
            onClick={() => void perform(() => projectActions.inspect(reference.target))}>{reference.label}</button>)}</>}
      </>}
    </div>
  </div>;
}

function ContentObject({ node, target, perform }: { node: ContentNode; target: Exclude<ContentTarget, { kind: 'item' }>;
  perform(action: () => Promise<void>): Promise<void> }) {
  const [name, setName] = useState(node.label);
  return <div className={styles.facts}><strong>{node.label}</strong><span>{node.description}</span>
    <Button onClick={() => void perform(() => projectActions.openContent(target))}>打开编辑器</Button>
    {target.kind !== 'clip' && <>
      <TextInput aria-label="对象名称" value={name} onChange={event => setName(event.currentTarget.value)} />
      <Button disabled={!name.trim()} onClick={() => void perform(async () => {
        await projectActions.openOwner(target);
        if (target.kind === 'node') graphAuthoring.setNodeValues(target.graphId, target.nodeId, [{ keys: ['_name'], value: name }]);
        else authoringActions.setTrackValue(target.timelineId, target.id, ['Name'], name);
      })}>应用名称</Button>
    </>}
  </div>;
}
