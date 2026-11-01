import { useEffect, useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type ProjectItem, type ProjectReferrer } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { openPanel } from '../../shell/layout';
import { authoringActions, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { referenceValueKey, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

const kindNames: Record<ProjectItem['kind'], string> = { skill: '技能', graph: '节点图', timeline: 'Timeline', 'input-plan': '输入方案' };
const keyOf = (item: ProjectItem) => JSON.stringify([item.owner, item.kind, item.identity]);

export function AuthoringProject() {
  const { skill, stamp, available, reason } = useAuthoring(useShallow(state => ({
    skill: state.target?.kind === 'skill' ? state.target.name : state.skills?.[0] ?? null,
    stamp: JSON.stringify(Object.entries(state.documents).map(([key, document]) => [key, document.revision, document.version])),
    available: state.available, reason: state.unavailableReason,
  })));
  const [items, setItems] = useState<ProjectItem[] | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState<ProjectItem['kind'] | null>(null);
  const [ownership, setOwnership] = useState<ProjectItem['ownership'] | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const field = useReferenceSelection(state => state.field);
  useEffect(() => { setSearch(''); setKind(null); setOwnership(null); }, [field?.id]);
  useEffect(() => {
    setItems(null);
    setFailure(null);
    if (skill === null || available === false) return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      authoringApi.project({ kind: 'skill', name: skill }, authoringActions.editingView(), controller.signal).then(setItems, error => {
        if (!isAbort(error)) setFailure({ text: '作者目录读取失败', entryId: consoleActions.error('Project', error) });
      });
    }, 150);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [skill, stamp, refresh, available]);
  const shown = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return (items ?? []).filter(item => (kind === null || kind === item.kind) && (ownership === null || ownership === item.ownership) &&
      `${item.name} ${item.ownerName} ${item.file} ${item.identity}`.toLocaleLowerCase().includes(query));
  }, [items, search, kind, ownership]);
  const item = items?.find(value => keyOf(value) === selected) ?? null;
  const referencedItem = field?.catalog.kind === 'ready' && field.catalog.items.some(choice => referenceValueKey(choice.value) === referenceValueKey(field.value))
    ? items?.find(value => referenceValueKey(value.identity) === referenceValueKey(field.value)) : undefined;
  const referencedIndex = referencedItem ? shown.findIndex(value => keyOf(value) === keyOf(referencedItem)) : -1;
  if (available === false) return <Empty text={reason ?? '作者目录不可用'} />;
  if (skill === null) return <Empty text="技能目录为空" />;
  return <div className={styles.catalog}>
    <div className={styles.browser}>
      <PanelBar>
        <TextInput aria-label="搜索作者内容" placeholder="搜索名称、所属技能" value={search} className={styles.search}
          onChange={event => setSearch(event.currentTarget.value)} />
        <Button variant="ghost" onClick={() => setRefresh(value => value + 1)}>刷新</Button>
      </PanelBar>
      <PanelBar>
        <MenuButton label="作者内容类型" caret entries={[
          { label: '全部类型', onSelect: () => setKind(null) },
          ...Object.entries(kindNames).map(([value, label]) => ({ label, onSelect: () => setKind(value as ProjectItem['kind']) })),
        ]}>{kind === null ? '全部类型' : kindNames[kind]}</MenuButton>
        <MenuButton label="内容所有权" caret entries={[
          { label: '私有与共享', onSelect: () => setOwnership(null) },
          { label: '私有', onSelect: () => setOwnership('Private') },
          { label: '共享', onSelect: () => setOwnership('Shared') },
        ]}>{ownership === null ? '私有与共享' : ownership === 'Shared' ? '共享' : '私有'}</MenuButton>
      </PanelBar>
      {failure ? <ErrorLine {...failure} /> : items === null ? <Empty text="正在读取作者目录" /> :
        <VirtualList className={styles.list} count={shown.length} rowHeight={48} label="作者内容"
          revealKey={field?.id} revealIndex={referencedIndex < 0 ? undefined : referencedIndex} render={index => {
          const value = shown[index];
          const choices = field?.catalog.kind === 'ready' ? field.catalog.items.filter(choice => referenceValueKey(choice.value) === referenceValueKey(value.identity)) : [];
          return <button className={styles.row} aria-selected={referencedItem ? keyOf(referencedItem) === keyOf(value) : selected === keyOf(value)} draggable={choices.length === 1 || value.kind === 'graph'}
            onDragStart={event => {
              if (field?.catalog.kind === 'ready' && choices.length === 1) writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choices[0]);
              else if (value.kind === 'graph') writeReferenceDrag(event.dataTransfer, value.kind, { value: value.identity, source: `${value.owner}/${value.file}` });
            }}
            onClick={event => { setSelected(keyOf(value)); if (event.detail < 2 && choices.length === 1) field!.choose(choices[0]); }}
            onDoubleClick={() => { void projectActions.open(value).then(openPanel, error => consoleActions.error('Project', error)); }}>
            <span>{value.name}</span>
            <small>{kindNames[value.kind]} · {value.ownerName} · {value.ownership === 'Shared' ? '共享' : '私有'} · {value.referenceCount} 项引用</small>
          </button>;
        }} />}
      <div className={styles.note}>{shown.length} / {items?.length ?? 0} 项 · 双击打开</div>
    </div>
    {item ? <ProjectItemDetails key={keyOf(item)} item={item} items={items!} stamp={stamp} onChanged={() => setRefresh(value => value + 1)} />
      : <Empty text="选择内容查看名称、引用者及编辑操作" />}
  </div>;
}

function ProjectItemDetails({ item, items, stamp, onChanged }: { item: ProjectItem; items: ProjectItem[]; stamp: string; onChanged(): void }) {
  const [referrers, setReferrers] = useState<ProjectReferrer[] | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const [renaming, setRenaming] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setReferrers(null);
    authoringApi.referrers({ kind: 'skill', name: item.owner }, item.identity, authoringActions.editingView(), controller.signal).then(setReferrers, error => {
      if (!isAbort(error)) setFailure({ text: '引用者读取失败', entryId: consoleActions.error('Project', error) });
    });
    return () => controller.abort();
  }, [item.owner, item.identity, stamp]);
  const perform = async (action: () => Promise<void>) => {
    setBusy(true);
    setFailure(null);
    try { await action(); onChanged(); }
    catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) }); }
    finally { setBusy(false); }
  };
  return <div className={styles.details}>
    <PanelBar>
      <Button variant="ghost" disabled={busy} onClick={() => void perform(async () => { openPanel(await projectActions.open(item)); })}>打开</Button>
      <Button variant="ghost" disabled={busy} onClick={() => setRenaming(!renaming)}>重命名</Button>
      {item.kind !== 'skill' && <Button variant="ghost" disabled={busy || referrers === null || referrers.length !== 0}
        tip={referrers?.length ? '仍有引用，先在引用者中解除引用' : '删除加入所属技能的未保存修改，可撤销'}
        onClick={() => void perform(async () => { setReferrers(await projectActions.remove(item)); })}>删除</Button>}
    </PanelBar>
    {failure && <ErrorLine {...failure} />}
    <div className={styles.facts}>
      <strong>{item.name}</strong>
      <span>{kindNames[item.kind]} · {item.ownerName} · {item.ownership === 'Shared' ? '共享' : '私有'}</span>
      <code>{item.file}</code>
      <small>{item.identity}</small>
    </div>
    {renaming && <RenameItem item={item} busy={busy} onRename={(name, scheme) => void perform(async () => {
      await projectActions.rename(item, name, scheme); setRenaming(false);
    })} />}
    {item.kind === 'input-plan' && <div className={styles.facts}>
      {item.schemes.map(scheme => <Button key={scheme.index} variant="ghost" disabled={busy}
        onClick={() => void perform(async () => { openPanel(await projectActions.open(item, scheme.index)); })}>打开 {scheme.name}</Button>)}
    </div>}
    <div className={styles.heading}>引用者{referrers === null ? ' · 读取中' : ` · ${referrers.length}`}</div>
    {referrers?.length === 0 && <div className={styles.note}>没有引用者</div>}
    {referrers?.map((reference, index) => {
      const source = items.find(value => value.owner === reference.owner && value.file === reference.file);
      return <button key={index} className={styles.referrer} disabled={busy || !source}
        onClick={() => { if (source) void perform(async () => { openPanel(await projectActions.open(source)); }); }}>
        <span>{reference.ownerName} · {source?.name ?? reference.file}</span><small>{reference.path}</small>
      </button>;
    })}
  </div>;
}

function RenameItem({ item, busy, onRename }: { item: ProjectItem; busy: boolean; onRename(name: string, scheme: number | null): void }) {
  const [scheme, setScheme] = useState<number | null>(item.kind === 'input-plan' && item.schemes.length === 1 ? item.schemes[0].index : null);
  const [name, setName] = useState(item.kind === 'input-plan' ? item.schemes.find(value => value.index === scheme)?.name ?? '' : item.name);
  return <div className={styles.facts}>
    {item.kind === 'input-plan' && <Select label="要重命名的输入方案" placeholder="选择方案" value={scheme === null ? null : String(scheme)}
      options={item.schemes.map(value => ({ value: String(value.index), label: value.name }))} onChange={value => {
        const index = Number(value); setScheme(index); setName(item.schemes.find(entry => entry.index === index)!.name);
      }} />}
    <TextInput autoFocus aria-label="内容名称" value={name} onChange={event => setName(event.currentTarget.value)} />
    <Button disabled={busy || !name.trim() || (item.kind === 'input-plan' && scheme === null)} onClick={() => onRename(name, scheme)}>应用名称</Button>
  </div>;
}
