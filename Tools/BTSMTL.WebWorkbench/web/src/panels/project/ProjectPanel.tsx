import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { AnimationLibrary } from '../../api/types';
import type { ProfileEntryTarget, ReferenceChoiceTarget } from '../../api/authoring';
import { openPanel } from '../../shell/layout';
import { usePanelAnimationFrame, usePanelApi, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { panelInfo } from '../../shell/panelTypes';
import { writeResourceReady } from '../../shell/readiness';
import { animationChoice, animationReferenceKind, resourceCache } from '../../state/resources';
import { referenceValueKey, useReferenceSelection, writeReferenceDrag, type ReferenceFieldSelection } from '../../state/referenceSelection';
import { authoringDocument, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions, getState, resourceActions, selectionActions, useWorkbench } from '../../state/store';
import { BarSeparator, Empty, Fold, PanelBar, Tabs } from '../../ui/blocks';
import { Button, MenuButton, NumberInput, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { ResourceScene } from '../resources/resourceScene';
import { AuthoringProject } from './AuthoringProject';
import { CarrierProject } from './CarrierProject';
import { LocateAssetButton } from './LocateAssetButton';
import { TargetReferrers } from './TargetReferrers';
import styles from '../resources/resources.module.css';
import projectStyles from './project.module.css';

// Project 的动画库：片段搜索与列表、同版 glb、素材播放与拖动时间、适配角色；单击片段在检查器显示资源信息。
// 素材时间只改 resource 分片，不经过 transport、不移动运行游标（D6）；按 D8 写入 resource* 就绪标记。

export function ProjectPanel() {
  const panel = usePanelApi();
  useEffect(() => panel.setTitle(panelInfo.resources.title), [panel]);
  const field = useReferenceSelection(state => state.field);
  const [section, setSection] = useState<'authoring' | 'carriers' | 'animations'>(() => getState().identity === null ? 'authoring' : 'animations');
  const [profileTarget, setProfileTarget] = useState<ProfileEntryTarget | null>(null);
  const [referenceTarget, setReferenceTarget] = useState<ReferenceChoiceTarget | null>(null);
  const fieldKind = field?.catalog.kind === 'ready' ? field.catalog.referenceKind : null;
  useEffect(() => { if (fieldKind === animationReferenceKind) setSection('animations'); }, [field?.id, fieldKind]);
  return <div className={projectStyles.root}>
    {field && <ProjectFieldChoices key={field.id} field={field} onReferrers={setReferenceTarget} onProfileTarget={target => {
      setProfileTarget(target);
      setSection('carriers');
    }} />}
    {referenceTarget && <div className={projectStyles.referenceDetails}>
      <PanelBar><span>所选目标的引用者</span><Button variant="ghost" onClick={() => setReferenceTarget(null)}>关闭</Button></PanelBar>
      <TargetReferrers target={referenceTarget} />
    </div>}
    <div className={projectStyles.body}><Tabs<'authoring' | 'carriers' | 'animations'> value={section} onChange={setSection} items={[
      { value: 'authoring', label: '作者内容', content: <AuthoringProject /> },
      { value: 'carriers', label: '载体与资源', content: <CarrierProject target={profileTarget} /> },
      { value: 'animations', label: '动画库', content: <AnimationProject /> },
    ]} /></div>
  </div>;
}

function ProjectFieldChoices({ field, onProfileTarget, onReferrers }: {
  field: ReferenceFieldSelection; onProfileTarget(target: ProfileEntryTarget): void; onReferrers(target: ReferenceChoiceTarget): void;
}) {
  const [search, setSearch] = useState('');
  const query = search.toLocaleLowerCase();
  const items = field.catalog.kind === 'ready' ? field.catalog.items.filter(choice => `${choice.name} ${choice.detail} ${choice.source}`.toLocaleLowerCase().includes(query)) : [];
  const selected = items.findIndex(choice => referenceValueKey(choice.value) === referenceValueKey(field.value));
  return <div className={projectStyles.choices}><Fold title={`当前字段：${field.name}`} defaultOpen>
    {field.catalog.kind === 'unavailable' ? <div className={projectStyles.note}>{field.catalog.reason}</div> : <>
      <TextInput aria-label="筛选当前字段候选" placeholder="搜索名称、说明或来源" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
      <VirtualList className={projectStyles.candidateList} count={items.length} rowHeight={48} label={field.name}
        revealKey={field.id} revealIndex={selected < 0 ? undefined : selected} render={index => {
          const choice = items[index];
          return <div className={projectStyles.candidateRow}><button className={projectStyles.row} draggable
          aria-selected={referenceValueKey(choice.value) === referenceValueKey(field.value)}
          onClick={() => field.choose(choice)}
          onDragStart={event => { if (field.catalog.kind === 'ready') writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choice); }}>
          <span>{choice.name} · {choice.detail}</span><small>{choice.source}</small>
          {choice.conflict && <span className={projectStyles.issue}>{choice.conflict}</span>}
          </button>
          {choice.targets.length !== 0 && <MenuButton label={`打开${choice.name}的来源`} variant="ghost" entries={choice.targets.flatMap(target => {
            if (target.kind === 'ProfileEntry') return [{
              label: `打开 ${target.domain} · ${target.key} · ${target.carrierIdentity}`,
              onSelect: () => onProfileTarget(target),
            }, { label: `查看 ${target.domain} · ${target.key} 的引用者`, onSelect: () => onReferrers(target) }];
            const graph = authoringDocument(useAuthoring.getState()).documentGraphs?.find(value => value.id === target.graphId);
            return [{
              label: graph ? `${graph.displayName ?? graph.id} · ${target.nodeId}` : `所属图尚未加载 · ${target.graphId}`,
              disabled: graph === undefined,
              onSelect: () => { if (graph) void projectActions.openGraph(graph, target.nodeId)
                .then(() => { openPanel('graph'); }, error => consoleActions.error('Project', error)); },
            }, { label: `查看节点 ${target.nodeId} 的引用者`, onSelect: () => onReferrers(target) }];
          })}>来源</MenuButton>}
        </div>;
      }} />
    </>}
  </Fold></div>;
}

function AnimationProject() {
  const [library, setLibrary] = useState<AnimationLibrary | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  useEffect(() => {
    let alive = true;
    resourceCache.library().then(value => { if (alive) setLibrary(value); },
      error => { if (alive) setFailure({ text: '资源清单读取失败', entryId: consoleActions.error('resources', error) }); });
    return () => { alive = false; };
  }, []);
  if (failure) return <div className={styles.failure}><ErrorLine text={failure.text} entryId={failure.entryId} /></div>;
  if (!library) return <Empty text="读取中" />;
  return <ResourceView library={library} />;
}

function ResourceView({ library }: { library: AnimationLibrary }) {
  const { identity, time, playing } = useWorkbench(useShallow(state => ({ identity: state.identity, time: state.time, playing: state.playing })));
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<string | null>(null);
  const field = useReferenceSelection(state => state.field);
  const animationField = field?.catalog.kind === 'ready' && field.catalog.referenceKind === animationReferenceKind;
  useEffect(() => {
    if (animationField) { setSearch(''); setCategory(null); }
  }, [field?.id, animationField]);
  const selectedValue = field?.catalog.kind === 'ready' && field.catalog.referenceKind === animationReferenceKind
    ? field.value : identity;
  const categories = useMemo(() => [...new Set(library.clips.map(item => item.category))].sort(), [library]);
  const clips = useMemo(() => {
    const text = search.trim().toLowerCase();
    return library.clips.filter(clip => (category === null || clip.category === category) &&
      `${clip.name} ${clip.category}`.toLowerCase().includes(text));
  }, [library, search, category]);
  const clip = library.clips.find(item => item.resource.identity === identity) ?? null;
  const referencedClip = animationField ? library.clips.find(item => item.resource.identity === field.value) : undefined;
  const referencedIdentity = referencedClip?.resource.identity;
  useEffect(() => { if (referencedIdentity !== undefined) resourceActions.select(referencedIdentity); }, [referencedIdentity]);
  const selectedIndex = clips.findIndex(item => referenceValueKey(selectedValue) === referenceValueKey(item.resource.identity));

  useEffect(() => {
    if (identity && !clip) consoleActions.log('error', 'resources', `该版本资源清单不包含所选资源 ${identity}`);
  }, [identity, clip, library]);

  const duration = clip?.exportedDuration ?? 0;
  const outOfRange = !!clip && (time < 0 || time > duration);
  useEffect(() => {
    if (outOfRange) consoleActions.log('error', 'resources', `请求的素材秒数 ${time} 超出该片段的实际导出范围 0—${duration}`);
  }, [outOfRange, time, duration]);

  // 播放：按显示帧推进素材秒数，到结尾停止。
  const last = useRef<number | null>(null);
  usePanelAnimationFrame(useCallback((now: number) => {
    const previous = last.current;
    last.current = now;
    if (previous === null) return;
    const state = getState();
    const next = Math.min(duration, state.time + (now - previous) / 1000);
    resourceActions.setTime(next);
    if (next >= duration) resourceActions.setPlaying(false);
  }, [duration]), playing && !!clip);
  useEffect(() => { if (!playing) last.current = null; }, [playing]);

  return (
    <div className={styles.root}>
      <div className={styles.side}>
        <div className={styles.search}>
          <span className={styles.sectionTitle}>动画库</span>
          <TextInput className={styles.searchInput} placeholder="搜索片段" value={search} aria-label="搜索片段"
            onChange={event => setSearch(event.currentTarget.value)} />
        </div>
        <PanelBar>
          <MenuButton label="筛选动画类别" caret entries={[
            { label: '全部类别', onSelect: () => setCategory(null) },
            ...categories.map(value => ({ label: value || '未分类', onSelect: () => setCategory(value) })),
          ]}>{category ?? '全部类别'}</MenuButton>
        </PanelBar>
        <VirtualList className={styles.list} count={clips.length} rowHeight={36} label="动画片段"
          revealKey={field?.id} revealIndex={selectedIndex < 0 ? undefined : selectedIndex} render={index => {
          const item = clips[index];
          return (
            <button className={styles.clip} draggable
              aria-selected={referenceValueKey(selectedValue) === referenceValueKey(item.resource.identity)}
              onDragStart={event => writeReferenceDrag(event.dataTransfer, animationReferenceKind, animationChoice(item))}
              onClick={() => {
                resourceActions.select(item.resource.identity);
                if (field?.catalog.kind === 'ready' && field.catalog.referenceKind === animationReferenceKind)
                  field.choose(animationChoice(item));
                else selectionActions.select({ kind: 'resource', id: item.resource.identity });
              }}>
              <span>{item.name}</span>
              <small>{item.category} · {item.duration.toFixed(3)} s · {item.frameRate} fps</small>
            </button>
          );
        }} />
        <div className={styles.count}>{clips.length} / {library.clips.length} 段</div>
      </div>
      <div className={styles.main}>
        <PanelBar>
          <Button variant="ghost" disabled={!clip} onClick={() => {
            if (!playing && time >= duration) resourceActions.setTime(0);
            resourceActions.setPlaying(!playing);
          }}>{playing ? '暂停' : time >= duration && clip ? '重播' : '播放'}</Button>
          <input className={styles.slider} type="range" min={0} max={duration} step={0.001} value={Math.min(time, duration)}
            disabled={!clip} aria-label="素材秒数"
            onChange={event => { resourceActions.setPlaying(false); resourceActions.setTime(Number(event.currentTarget.value)); }} />
          <NumberInput label="素材秒数" value={Number(time.toFixed(3))} min={0} max={duration} step={0.001} disabled={!clip}
            onChange={value => { if (value !== null) { resourceActions.setPlaying(false); resourceActions.setTime(value); } }} />
          <BarSeparator />
          <FitButton />
          {clip && <LocateAssetButton key={clip.resource.identity} target={{ kind: 'asset', identity: clip.resource.identity }} />}
        </PanelBar>
        <ResourceCanvas library={library} />
      </div>
    </div>
  );
}

let fitCurrent: (() => void) | null = null;
function FitButton() {
  return <Button variant="ghost" onClick={() => fitCurrent?.()}>适配角色</Button>;
}

// 场景属于面板实例：可见时建立，隐藏、卸载或换窗口时释放；glb 数据在缓存中。
function ResourceCanvas({ library }: { library: AnimationLibrary }) {
  const host = useRef<HTMLDivElement>(null);
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  const [failure, setFailure] = useState<number | null>(null);

  useEffect(() => {
    if (!visible || !host.current) return;
    const scene = new ResourceScene(host.current, panelWindow);
    let alive = true, loaded = false, shown: string | null = null;
    let drawn: { identity: string; time: number } | null = null;
    scene.onRendered = () => {
      if (!drawn) return;
      const clip = library.clips.find(item => item.resource.identity === drawn!.identity);
      if (!clip) return;
      writeResourceReady({
        identity: clip.resource.identity, revision: clip.resource.contentRevision, library: library.sha256,
        source: library.sourceSha256, time: drawn.time,
      });
    };
    const apply = () => {
      if (!loaded) return;
      const { identity, time } = getState();
      const clip = library.clips.find(item => item.resource.identity === identity);
      writeResourceReady(null);
      drawn = null;
      if (!clip || time < 0 || time > clip.exportedDuration) return;
      try {
        if (shown !== identity) {
          scene.showClip(clip);
          scene.setTime(time);
          scene.fit();
          shown = identity;
        } else scene.setTime(time);
        drawn = { identity: clip.resource.identity, time };
      } catch (error) {
        setFailure(consoleActions.error('resources', error));
      }
    };
    fitCurrent = () => scene.fit();
    resourceCache.file(library.sha256).then(data => scene.load(data)).then(() => {
      if (!alive) return;
      loaded = true;
      apply();
    }, error => { if (alive) setFailure(consoleActions.error('resources', error)); });
    const unsubscribe = useWorkbench.subscribe((state, previous) => {
      if (state.identity !== previous.identity || state.time !== previous.time) apply();
    });
    return () => {
      alive = false;
      unsubscribe();
      if (fitCurrent) fitCurrent = null;
      writeResourceReady(null);
      scene.dispose();
    };
  }, [visible, panelWindow, library]);

  return (
    <div className={styles.canvasWrap}>
      {failure !== null && <ErrorLine text="动画资源加载失败" entryId={failure} />}
      <div ref={host} className={styles.canvas} />
    </div>
  );
}
