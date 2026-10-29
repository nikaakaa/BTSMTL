import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { AnimationLibrary } from '../../api/types';
import { usePanelAnimationFrame, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { writeResourceReady } from '../../shell/readiness';
import { resourceCache } from '../../state/resources';
import { consoleActions, getState, resourceActions, useWorkbench } from '../../state/store';
import { BarSeparator, Empty, Facts, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { ResourceScene } from './resourceScene';
import styles from './resources.module.css';

// 动画资源面板（承接 resources.js）：片段搜索与列表、同版 glb、素材播放与拖动时间、适配角色、资源版本信息。
// 素材时间只改 resource 分片，不经过 transport、不移动运行游标（D6）；按 D8 写入 resource* 就绪标记。

export function ResourcesPanel() {
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
  const clips = useMemo(() => {
    const text = search.trim().toLowerCase();
    return text ? library.clips.filter(clip => `${clip.name} ${clip.category}`.toLowerCase().includes(text)) : library.clips;
  }, [library, search]);
  const clip = library.clips.find(item => item.resource.identity === identity) ?? null;

  // 未指定资源时选第一段；地址指定的资源不在清单中时报错。
  useEffect(() => {
    if (!identity && library.clips.length) resourceActions.select(library.clips[0].resource.identity);
    else if (identity && !clip) consoleActions.log('error', 'resources', `该版本资源清单不包含所选资源 ${identity}`);
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
          <TextInput className={styles.searchInput} placeholder="搜索片段" value={search} aria-label="搜索片段"
            onChange={event => setSearch(event.currentTarget.value)} />
        </div>
        <VirtualList className={styles.list} count={clips.length} rowHeight={36} label="动画片段" render={index => {
          const item = clips[index];
          return (
            <button className={styles.clip} aria-selected={item.resource.identity === identity}
              onClick={() => resourceActions.select(item.resource.identity)}>
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
        </PanelBar>
        <ResourceCanvas library={library} />
        {clip && (
          <div className={styles.details}>
            <Fold title={clip.name} kind="var(--track-anim)">
              <Facts fields={[
                { displayName: '分类', value: clip.category },
                { displayName: 'Unity 来源', value: clip.unityAsset },
                { displayName: '素材时长', value: `${clip.duration} s` },
                { displayName: 'glb 时长', value: `${clip.exportedDuration} s` },
                { displayName: '源帧率', value: `${clip.frameRate} fps` },
                { displayName: '来源说明', value: clip.provenance.description },
              ]} />
            </Fold>
            <IdentityFold fields={[
              { displayName: '资源身份', value: clip.resource.identity }, { displayName: 'GUID', value: clip.resource.guid },
              { displayName: 'Local file ID', value: clip.resource.localFileId },
              { displayName: '来源资产 SHA-256', value: clip.resource.contentRevision },
              { displayName: '来源 Blend SHA-256', value: library.sourceSha256 }, { displayName: 'glb SHA-256', value: library.sha256 },
            ]} />
          </div>
        )}
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
