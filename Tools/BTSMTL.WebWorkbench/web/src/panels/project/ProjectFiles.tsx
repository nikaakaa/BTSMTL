import { useEffect, useMemo, useState } from 'react';
import { isAbort } from '../../api/http';
import { projectApi, type ProjectFileEntry, type ProjectFolderListing } from '../../api/project';
import { useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { projectContents } from '../../state/projectContents';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

export function ProjectFiles() {
  const [folder, setFolder] = useState('Skills');
  const [listing, setListing] = useState<ProjectFolderListing | null>(null);
  const [selected, setSelected] = useState<ProjectFileEntry | null>(null);
  const [search, setSearch] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const documents = useAuthoring(state => state.documents);
  const items = useMemo(() => projectContents(documents).items, [documents]);
  useEffect(() => {
    const controller = new AbortController();
    setListing(null); setSelected(null); setFailure(null);
    projectApi.folder(folder, controller.signal).then(setListing, error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('文件排查', error) });
    });
    return () => controller.abort();
  }, [folder, refresh]);
  const query = search.toLocaleLowerCase();
  const folders = listing?.folders.filter(item => item.name.toLocaleLowerCase().includes(query)) ?? [];
  const files = listing?.files.filter(item => item.name.toLocaleLowerCase().includes(query)) ?? [];
  const itemOf = (file: string) => items.find(item => file === `Skills/${item.owner}/${item.file}`);
  const open = (file: ProjectFileEntry) => {
    const item = itemOf(file.path);
    if (item !== undefined) void projectActions.open(item).catch(error => setFailure({ text: String(error), entryId: consoleActions.error('文件排查', error) }));
  };
  return <div className={styles.contentGrid}><div className={styles.contentTree}>
    <PanelBar wrap><Button disabled={folder === ''} onClick={() => setFolder(folder.includes('/') ? folder.slice(0, folder.lastIndexOf('/')) : '')}>上级</Button>
      <TextInput aria-label="筛选排查文件" placeholder="搜索当前目录" value={search} onChange={event => setSearch(event.currentTarget.value)} />
      <Button onClick={() => setRefresh(value => value + 1)}>刷新</Button></PanelBar>
    <code className={styles.path}>{folder || 'Project'}</code>{failure && <ErrorLine {...failure} />}
    {listing === null ? <Empty text="正在读取目录" /> : <VirtualList className={styles.contentList} count={folders.length + files.length}
      rowHeight={38} label="排查目录" render={index => {
        if (index < folders.length) {
          const entry = folders[index];
          return <button className={styles.row} onDoubleClick={() => { setFolder(entry.path); setSearch(''); }}><span>{entry.name}/</span><small>文件夹 · 双击进入</small></button>;
        }
        const entry = files[index - folders.length], item = itemOf(entry.path);
        return <button className={styles.row} aria-selected={selected?.path === entry.path} onClick={() => setSelected(entry)} onDoubleClick={() => open(entry)}>
          <span>{item?.name ?? entry.name}</span><small>{entry.extension} · {entry.length} B</small></button>;
      }} />}
  </div><div className={styles.contentDetails}>{selected === null ? <Empty text="文件视图用于排查；作者内容请从内容视图打开。" />
    : <div className={styles.facts}><strong>{selected.name}</strong><code>{selected.path}</code><small>{selected.length} B</small>
      {selected.guid !== null && <small>文件 GUID：{selected.guid}</small>}
      {itemOf(selected.path) !== undefined && <Button onClick={() => open(selected)}>打开作者编辑器</Button>}
    </div>}</div></div>;
}
