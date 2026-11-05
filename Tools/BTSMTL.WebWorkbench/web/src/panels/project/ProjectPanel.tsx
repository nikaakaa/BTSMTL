import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { AnimationClip, AnimationLibrary } from '../../api/types';
import { authoringApi, type AuthoringCarrier, type AuthoringCarriers, type AuthoringProject, type BusinessKeyMetadata, type BusinessKeyTarget, type ProfileEntryTarget, type ProjectItem, type ReferenceChoiceTarget } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { projectApi, type ProjectFileEntry, type ProjectFolderListing } from '../../api/project';
import { usePanelApi } from '../../shell/panel';
import { panelInfo } from '../../shell/panelTypes';
import { animationChoice, animationReferenceKind, resourceCache } from '../../state/resources';
import { matchesReferenceChoice, referenceTargetKey, referenceValueKey, useReferenceSelection, writeReferenceDrag, type ReferenceFieldSelection } from '../../state/referenceSelection';
import { authoringActions, authoringDocument, useAuthoring, useSkillElements } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions, getState, resourceActions, selectedScene, useWorkbench } from '../../state/store';
import { Empty, Fold, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { AnimationDetails } from './AnimationDetails';
import { CarrierScopeDetails, carrierProfiles } from './CarrierProject';
import { ProfileSources, type ProfileLocation } from './ProfileEditor';
import { SkillContentCreation, type SkillContentKind } from './SkillContentCreation';
import { SkillItemDetails, kindNames } from './SkillItemDetails';
import { TargetReferrers } from './TargetReferrers';
import { BusinessKeyDetails, BusinessKeyList } from './BusinessKeys';
import { ResourceObjectPicker } from './ResourceObjectPicker';
import projectStyles from './project.module.css';

// Project 窗口：直接按磁盘文件夹浏览 Unity 工程与技能、场景方案目录；选中文件按其实际内容显示详情。

export function ProjectPanel() {
  const panel = usePanelApi();
  useEffect(() => panel.setTitle(panelInfo.resources.title), [panel]);
  const field = useReferenceSelection(state => state.field);
  const [profileTarget, setProfileTarget] = useState<ProfileEntryTarget | null>(null);
  const [referenceTarget, setReferenceTarget] = useState<ReferenceChoiceTarget | null>(null);
  const [refresh, setRefresh] = useState(0);
  const projectCatalog = useLibraryItems(refresh);
  const [carriers, setCarriers, carrierFailure] = useCarriers();
  const [keySource, setKeySource] = useState<'project' | 'resources'>('project');
  const [businessKey, setBusinessKey] = useState<{ source: 'field' | 'project' | 'resources'; target: BusinessKeyTarget } | null>(null);
  const selectedKeys = businessKey?.source === 'field' ? field?.catalog.kind === 'ready' ? field.catalog.items.flatMap(choice => choice.businessKeys) : []
    : businessKey?.source === 'resources' ? carriers?.businessKeys : projectCatalog.businessKeys;
  const metadata = businessKey === null ? null : selectedKeys?.find(item => referenceTargetKey(item.target) === referenceTargetKey(businessKey.target)) ?? null;
  const refreshProject = () => setRefresh(value => value + 1);
  return <div className={projectStyles.root}>
    {field && <ProjectFieldChoices key={field.id} field={field} onReferrers={setReferenceTarget} onProfileTarget={setProfileTarget}
      onBusinessKey={item => setBusinessKey({ source: 'field', target: item.target })} />}
    <div className={projectStyles.choices}><Fold title="业务键">
      <Select<'project' | 'resources'> label="业务键目录来源" value={keySource} onChange={setKeySource} options={[
        { value: 'project', label: '作者目录' }, { value: 'resources', label: '资源目录' },
      ]} />
      <BusinessKeyList items={keySource === 'project' ? projectCatalog.businessKeys : carriers?.businessKeys ?? null}
        failure={keySource === 'project' ? projectCatalog.failure : carrierFailure} selected={businessKey?.target ?? null}
        onSelect={item => setBusinessKey({ source: keySource, target: item.target })} />
    </Fold></div>
    {businessKey !== null && <div className={projectStyles.referenceDetails}>
      <PanelBar><span>业务键 · 绑定与投影</span><Button variant="ghost" onClick={() => setBusinessKey(null)}>关闭</Button></PanelBar>
      <BusinessKeyDetails key={referenceTargetKey(businessKey.target)} target={businessKey.target} metadata={metadata}
        onCatalog={catalog => { setCarriers(catalog); useAuthoring.setState({ carrierRevision: catalog.authoringRevision }); }} onChanged={refreshProject} />
    </div>}
    {referenceTarget && <div className={projectStyles.referenceDetails}>
      <PanelBar><span>所选目标的引用者</span><Button variant="ghost" onClick={() => setReferenceTarget(null)}>关闭</Button></PanelBar>
      <TargetReferrers target={referenceTarget} />
    </div>}
    <div className={projectStyles.body}><ProjectBrowser profileTarget={profileTarget} projectCatalog={projectCatalog} carriers={carriers} onRefresh={refreshProject} /></div>
  </div>;
}

function ProjectFieldChoices({ field, onProfileTarget, onReferrers, onBusinessKey }: {
  field: ReferenceFieldSelection; onProfileTarget(target: ProfileEntryTarget): void; onReferrers(target: ReferenceChoiceTarget): void;
  onBusinessKey(item: BusinessKeyMetadata): void;
}) {
  const [search, setSearch] = useState('');
  const query = search.toLocaleLowerCase();
  const items = field.catalog.kind === 'ready' ? field.catalog.items.filter(choice => `${choice.name} ${choice.detail} ${choice.source}`.toLocaleLowerCase().includes(query)) : [];
  const selected = items.findIndex(choice => matchesReferenceChoice(choice, field));
  return <div className={projectStyles.choices}><Fold title={`当前字段：${field.name}`} defaultOpen>
    {field.catalog.kind === 'unavailable' ? <div className={projectStyles.note}>{field.catalog.reason}</div> : <>
      {field.catalog.businessKeysAvailability !== null && !field.catalog.businessKeysAvailability.available &&
        <div role="status" className={projectStyles.issue}>{field.catalog.businessKeysAvailability.reason}</div>}
      <TextInput aria-label="筛选当前字段候选" placeholder="搜索名称、说明或来源" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
      <VirtualList className={projectStyles.candidateList} count={items.length} rowHeight={48} label={field.name}
        revealKey={field.id} revealIndex={selected < 0 ? undefined : selected} render={index => {
          const choice = items[index];
          return <div className={projectStyles.candidateRow}><button className={projectStyles.row} draggable
          aria-selected={matchesReferenceChoice(choice, field)}
          onClick={() => field.choose(choice)}
          onDragStart={event => { if (field.catalog.kind === 'ready') writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choice); }}>
          <span>{choice.name} · {choice.detail}</span><small>{choice.source}</small>
          {choice.conflict && <span className={projectStyles.issue}>{choice.conflict}</span>}
          </button>
          {choice.targets.length !== 0 && <MenuButton label={`打开${choice.name}的来源`} variant="ghost" entries={[...choice.targets.flatMap(target => {
            if (target.kind === 'ProfileEntry') return [{
              label: `打开 ${target.domain} · ${target.key} · ${target.carrierIdentity}`,
              onSelect: () => onProfileTarget(target),
            }, { label: `查看 ${target.domain} · ${target.key} 的引用者`, onSelect: () => onReferrers(target) }];
            if (target.kind === 'BusinessKey') return [{ label: `查看 ${target.package} · ${target.keyKind} · ${target.key} 的引用者`, onSelect: () => onReferrers(target) }];
            const graph = authoringDocument(useAuthoring.getState()).documentGraphs?.find(value => value.id === target.graphId);
            return [{
              label: graph ? `${graph.displayName ?? graph.id} · ${target.nodeId}` : `所属图尚未加载 · ${target.graphId}`,
              disabled: graph === undefined,
              onSelect: () => { if (graph) void projectActions.openGraph(graph, target.nodeId)
                .catch(error => consoleActions.error('Project', error)); },
            }, { label: `查看节点 ${target.nodeId} 的引用者`, onSelect: () => onReferrers(target) }];
          }), ...choice.businessKeys.map(item => ({ label: `业务键 ${item.target.package} · ${item.target.keyKind} · ${item.target.key}：绑定、投影与改名`,
            onSelect: () => onBusinessKey(item) }))]}>来源</MenuButton>}
        </div>;
      }} />
    </>}
  </Fold></div>;
}

type Failure = { text: string; entryId: number | null };
const parentOf = (path: string) => path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
const ancestorsOf = (path: string) => path.split('/').map((_, index, parts) => parts.slice(0, index + 1).join('/'));
const skillOf = (path: string) => { const parts = path.split('/'); return parts[0] === 'Skills' && parts.length > 1 ? parts[1] : null; };
const sizeText = (length: number) => length < 1024 ? `${length} B` : length < 1048576 ? `${(length / 1024).toFixed(1)} KB` : `${(length / 1048576).toFixed(1)} MB`;

function useAnimationLibrary() {
  const [library, setLibrary] = useState<AnimationLibrary | null>(null);
  useEffect(() => {
    let alive = true;
    resourceCache.library().then(value => { if (alive) setLibrary(value); }, error => consoleActions.error('resources', error));
    return () => { alive = false; };
  }, []);
  return library;
}

function useCarriers() {
  const [catalog, setCatalog] = useState<AuthoringCarriers | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const revision = useAuthoring(state => state.carrierRevision);
  useEffect(() => {
    if (catalog !== null && catalog.authoringRevision === revision) return;
    const controller = new AbortController();
    setFailure(null);
    authoringApi.carriers(controller.signal).then(value => {
      setCatalog(value);
      useAuthoring.setState({ carrierRevision: value.authoringRevision });
    }, error => { if (!isAbort(error)) setFailure({ text: '资源目录读取失败', entryId: consoleActions.error('Project', error) }); });
    return () => controller.abort();
  }, [revision]);
  return [catalog, setCatalog, failure] as const;
}

// 技能作者文件的名称、类型与所有权来自宿主作者目录（一次覆盖全部技能）；目录较慢，按编辑修订共享一份，
// 文件夹先按磁盘显示，到达后补上作者名称。
let libraryItems: { key: string; items: Promise<AuthoringProject> } | null = null;

function useLibraryItems(refresh: number) {
  const { skill, stamp } = useAuthoring(useShallow(state => ({
    skill: state.available === false ? null : state.skills?.[0] ?? null,
    stamp: JSON.stringify([state.carrierRevision, Object.entries(state.documents).map(([key, document]) => [key, document.revision, document.version])]),
  })));
  const scene = useWorkbench(state => selectedScene(state)?.path ?? null);
  const key = JSON.stringify([stamp, scene, refresh]);
  const [result, setResult] = useState<({ key: string; kind: 'ready'; value: AuthoringProject }
    | ({ key: string; kind: 'failed' } & Failure)) | null>(null);
  useEffect(() => {
    if (skill === null) return;
    let alive = true;
    const timer = setTimeout(() => {
      if (libraryItems?.key !== key) {
        const request = authoringApi.project({ kind: 'skill', name: skill }, authoringActions.editingView());
        libraryItems = { key, items: request };
        request.catch(() => { if (libraryItems?.items === request) libraryItems = null; });
      }
      libraryItems.items.then(value => { if (alive) setResult({ key, kind: 'ready', value }); },
        error => { if (alive && !isAbort(error)) setResult({ key, kind: 'failed', text: '作者目录读取失败', entryId: consoleActions.error('Project', error) }); });
    }, 150);
    return () => { alive = false; clearTimeout(timer); };
  }, [skill, key]);
  const current = result?.key === key ? result : null;
  return { items: current?.kind === 'ready' ? current.value.items : null,
    businessKeys: current?.kind === 'ready' ? current.value.businessKeys : null,
    failure: current?.kind === 'failed' ? current : null, stamp: JSON.stringify([stamp, scene]) };
}

function ProjectBrowser({ profileTarget, projectCatalog, carriers, onRefresh }: {
  profileTarget: ProfileEntryTarget | null; projectCatalog: ReturnType<typeof useLibraryItems>; carriers: AuthoringCarriers | null; onRefresh(): void;
}) {
  const [listings, setListings] = useState<Record<string, ProjectFolderListing>>({});
  const [failure, setFailure] = useState<Failure | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set(ancestorsOf('Skills')));
  const [folder, setFolder] = useState('Skills');
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState<SkillContentKind | null>(null);
  const [search, setSearch] = useState('');
  const requested = useRef(new Set<string>());
  const library = useAnimationLibrary();
  const field = useReferenceSelection(state => state.field);
  const { editable, target } = useAuthoring(useShallow(state => ({
    editable: state.author && state.target?.kind === 'skill' && !authoringDocument(state).loading,
    target: state.target?.kind === 'skill' ? state.target.name : null,
  })));
  const available = useAuthoring(state => state.available);
  const elements = useSkillElements(editable && available !== false);
  const { items: skillItems, stamp } = projectCatalog;

  const load = useCallback((path: string, force = false) => {
    if (!force && requested.current.has(path)) return;
    requested.current.add(path);
    projectApi.folder(path).then(listing => setListings(previous => ({ ...previous, [path]: listing })), error => {
      requested.current.delete(path);
      setFailure({ text: `读取目录 ${path || 'Project'} 失败`, entryId: consoleActions.error('Project', error) });
    });
  }, []);
  const open = useCallback((path: string) => {
    for (const ancestor of ['', ...ancestorsOf(path)]) load(ancestor);
    setExpanded(previous => new Set([...previous, ...ancestorsOf(path)]));
    setFolder(path);
    setSearch('');
    setFailure(null);
  }, [load]);
  const reveal = useCallback((file: string) => {
    open(parentOf(file));
    setSelected(file);
    setCreating(null);
  }, [open]);
  useEffect(() => { for (const path of ['', ...ancestorsOf('Skills')]) load(path); }, [load]);

  const clipsByAsset = useMemo(() => {
    const map = new Map<string, AnimationClip[]>();
    for (const clip of library?.clips ?? []) map.set(clip.unityAsset, [...map.get(clip.unityAsset) ?? [], clip]);
    return map;
  }, [library]);
  const carrierBySource = useMemo(() => {
    const values = new Map<string, AuthoringCarrier[]>();
    for (const carrier of carriers?.items ?? []) values.set(carrier.source, [...values.get(carrier.source) ?? [], carrier]);
    return values;
  }, [carriers]);
  const profilesBySource = useMemo(() => {
    const values = new Map<string, ProfileLocation[]>();
    for (const profile of (carriers?.items ?? []).flatMap(carrierProfiles)) {
      const entries = values.get(profile.source) ?? [];
      if (!entries.some(item => item.package === profile.package && item.assetToken === profile.assetToken)) values.set(profile.source, [...entries, profile]);
    }
    return values;
  }, [carriers]);
  const itemOf = useCallback((path: string): ProjectItem | null => {
    const skill = skillOf(path);
    if (skill === null || skillItems === null) return null;
    const file = path.split('/').slice(2).join('/');
    return skillItems.find(item => item.owner === skill && item.file === file) ?? null;
  }, [skillItems]);

  // 资源条目、动画字段或已选动画来自其它面板时，跳到其所在文件夹。
  useEffect(() => {
    if (profileTarget === null || carriers === null) return;
    const carrier = carriers.items.find(item => item.identity === profileTarget.carrierIdentity);
    if (carrier) reveal(carrier.source);
    else consoleActions.log('error', 'Project', `引用的来源载体已失效，原身份已保留：${profileTarget.carrierIdentity}`);
  }, [profileTarget, carriers, reveal]);
  const animationValue = field?.catalog.kind === 'ready' && field.catalog.referenceKind === animationReferenceKind ? field.value : undefined;
  const initialIdentity = useRef(getState().identity);
  useEffect(() => {
    const value = animationValue ?? initialIdentity.current;
    if (value === null || value === undefined || library === null) return;
    initialIdentity.current = null;
    const clip = library.clips.find(item => item.key != null && referenceValueKey(item.key) === referenceValueKey(value));
    if (!clip) return;
    resourceActions.select(clip.resource.identity);
    reveal(clip.unityAsset);
  }, [field?.id, animationValue, library, reveal]);

  const listing = listings[folder];
  const shownFolders = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return (listing?.folders ?? []).filter(entry => entry.name.toLocaleLowerCase().includes(query));
  }, [listing, search]);
  const shownFiles = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return (listing?.files ?? []).filter(entry => `${entry.name} ${itemOf(entry.path)?.name ?? ''}`.toLocaleLowerCase().includes(query));
  }, [listing, search, itemOf]);
  const treeRows = useMemo(() => {
    const rows: { path: string; name: string; depth: number }[] = [];
    const walk = (path: string, depth: number) => {
      for (const entry of listings[path]?.folders ?? []) {
        rows.push({ path: entry.path, name: entry.name, depth });
        if (expanded.has(entry.path)) walk(entry.path, depth + 1);
      }
    };
    walk('', 0);
    return rows;
  }, [listings, expanded]);

  const selectedFile = selected === null ? null : listings[parentOf(selected)]?.files.find(entry => entry.path === selected) ?? null;
  const selectedIndex = selectedFile === null ? -1 : shownFolders.length + shownFiles.indexOf(selectedFile);
  const describe = (file: ProjectFileEntry) => {
    const item = itemOf(file.path);
    if (item) return { name: item.name, detail: `${kindNames[item.kind]} · ${item.ownership === 'Shared' ? '共享' : '私有'} · ${file.name}` };
    const clips = clipsByAsset.get(file.path);
    if (clips) return { name: file.name, detail: clips.length === 1 ? `动画 · ${clips[0].duration.toFixed(3)} s` : `动画 · ${clips.length} 段` };
    if (carrierBySource.has(file.path)) return { name: file.name, detail: `${carrierBySource.get(file.path)!.length} 个执行 Scope` };
    return { name: file.name, detail: `${file.extension.replace('.', '') || '文件'} · ${sizeText(file.length)}` };
  };

  return <div className={projectStyles.browserGrid}>
    <div className={projectStyles.tree} role="tree" aria-label="Project 文件夹">
      {treeRows.map(row => <div key={row.path} className={projectStyles.treeRow} role="treeitem"
        aria-selected={row.path === folder} aria-expanded={expanded.has(row.path)} style={{ paddingLeft: 4 + row.depth * 12 }}>
        <button className={projectStyles.arrow} aria-label={expanded.has(row.path) ? `收起 ${row.name}` : `展开 ${row.name}`}
          disabled={listings[row.path]?.folders.length === 0} onClick={() => {
            load(row.path);
            setExpanded(previous => { const next = new Set(previous); if (!next.delete(row.path)) next.add(row.path); return next; });
          }}>{listings[row.path]?.folders.length === 0 ? '' : expanded.has(row.path) ? '▾' : '▸'}</button>
        <button className={projectStyles.treeName} onClick={() => open(row.path)}>{row.name}</button>
      </div>)}
    </div>
    <div className={projectStyles.browser}>
      <PanelBar wrap>
        <Button variant="ghost" disabled={folder === ''} onClick={() => open(parentOf(folder))}>上级</Button>
        <TextInput aria-label="在当前文件夹中搜索" placeholder="搜索当前文件夹" value={search} className={projectStyles.search}
          onChange={event => setSearch(event.currentTarget.value)} />
        <Button variant="ghost" onClick={() => load(folder, true)}>刷新</Button>
        <MenuButton label="新建作者内容" caret entries={[
          { label: '技能', onSelect: () => setCreating('skill') },
          { label: `Timeline${target ? `（${target}）` : ''}`, disabled: !editable || elements === null, onSelect: () => setCreating('timeline') },
          { label: `共享子图${target ? `（${target}）` : ''}`, disabled: !editable || elements === null, onSelect: () => setCreating('macro') },
          { label: `输入方案${target ? `（${target}）` : ''}`, disabled: !editable, onSelect: () => setCreating('input') },
        ]}>新建</MenuButton>
      </PanelBar>
      {field?.resourceRegistration && selectedFile !== null && <ResourceObjectPicker
        key={JSON.stringify([field.id, selectedFile.path, field.resourceRegistration.objects])} file={selectedFile} field={field}
        registration={field.resourceRegistration} businessKeys={projectCatalog.businessKeys} failure={projectCatalog.failure} />}
      <div className={projectStyles.path} title={folder}>{folder || 'Project'}</div>
      {failure && <ErrorLine {...failure} />}
      {listing === undefined ? <Empty text="正在读取文件夹" /> :
        <VirtualList className={projectStyles.list} count={shownFolders.length + shownFiles.length} rowHeight={40} label="文件夹内容"
          revealKey={selected ?? undefined} revealIndex={selectedIndex < 0 ? undefined : selectedIndex} render={index => {
          if (index < shownFolders.length) {
            const entry = shownFolders[index];
            return <button className={projectStyles.row} onDoubleClick={() => open(entry.path)} onClick={() => setSelected(null)}>
              <span>{entry.name}/</span><small>文件夹 · 双击进入</small>
            </button>;
          }
          const file = shownFiles[index - shownFolders.length];
          const item = itemOf(file.path);
          const clips = clipsByAsset.get(file.path);
          const { name, detail } = describe(file);
          const choices = item && field?.catalog.kind === 'ready'
            ? field.catalog.items.filter(choice => referenceValueKey(choice.value) === referenceValueKey(item.identity)) : [];
          return <button className={projectStyles.row} aria-selected={file.path === selected}
            draggable={choices.length === 1 || item?.kind === 'graph' || clips?.length === 1}
            onDragStart={event => {
              if (field?.catalog.kind === 'ready' && choices.length === 1) writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choices[0]);
              else if (item?.kind === 'graph') writeReferenceDrag(event.dataTransfer, item.kind,
                { value: item.identity, source: `${item.owner}/${item.file}`, targets: [], associated: [] });
              else if (clips?.length === 1) writeReferenceDrag(event.dataTransfer, animationReferenceKind, animationChoice(clips[0]));
            }}
            onClick={event => {
              setSelected(file.path);
              setCreating(null);
              if (event.detail < 2 && choices.length === 1) field!.choose(choices[0]);
            }}
            onDoubleClick={() => { if (item) void projectActions.open(item).catch(error => consoleActions.error('Project', error)); }}>
            <span>{name}</span><small>{detail}</small>
          </button>;
        }} />}
      <div className={projectStyles.note}>{shownFolders.length + shownFiles.length} 项{skillOf(folder) !== null && skillItems === null ? ' · 正在读取作者名称' : ''}</div>
    </div>
    <ProjectDetails file={selectedFile} creating={creating} target={target} elements={elements} library={library}
      clips={selectedFile ? clipsByAsset.get(selectedFile.path) ?? null : null}
      carriers={selectedFile ? carrierBySource.get(selectedFile.path) ?? [] : []}
      profiles={selectedFile ? profilesBySource.get(selectedFile.path) ?? [] : []} businessKeys={carriers?.businessKeys ?? []}
      item={selectedFile ? itemOf(selectedFile.path) : null} items={skillItems ?? []} stamp={stamp}
      profileTarget={profileTarget}
      onCreated={() => { setCreating(null); onRefresh(); load(folder, true); }}
      onChanged={() => { onRefresh(); load(folder, true); }} />
  </div>;
}

function ProjectDetails({ file, creating, target, elements, library, clips, carriers, profiles, businessKeys, item, items, stamp, profileTarget, onCreated, onChanged }: {
  file: ProjectFileEntry | null; creating: SkillContentKind | null; target: string | null; elements: ReturnType<typeof useSkillElements>;
  library: AnimationLibrary | null; clips: AnimationClip[] | null; carriers: AuthoringCarrier[];
  profiles: ProfileLocation[]; businessKeys: BusinessKeyMetadata[];
  item: ProjectItem | null; items: ProjectItem[]; stamp: string; profileTarget: ProfileEntryTarget | null;
  onCreated(): void; onChanged(): void;
}) {
  if (creating !== null) return <div className={projectStyles.details}>
    <SkillContentCreation key={creating} kind={creating} skill={target} elements={elements} onDone={onCreated} />
  </div>;
  if (file === null) return <div className={projectStyles.details}><Empty text="选择文件查看内容；双击文件夹进入，双击作者文件打开编辑器" /></div>;
  if (item) return <SkillItemDetails key={file.path} item={item} items={items} stamp={stamp} onChanged={onChanged} />;
  if (clips && library) return <AnimationDetails key={file.path} library={library} clips={clips} />;
  if (carriers.length) return <CarrierScopeDetails key={file.path} carriers={carriers} focusTarget={profileTarget} businessKeys={businessKeys} />;
  if (profiles.length) return <div className={projectStyles.details}><ProfileSources key={file.path} profiles={profiles} businessKeys={businessKeys} /></div>;
  return <div className={projectStyles.details}><div className={projectStyles.facts}>
    <strong>{file.name}</strong>
    <code>{file.path}</code>
    <small>{sizeText(file.length)}{file.guid ? ` · guid ${file.guid}` : ''}</small>
    {skillOf(file.path) !== null && <span className={projectStyles.note}>作者目录读取完成后显示此文件的作者内容</span>}
  </div></div>;
}
