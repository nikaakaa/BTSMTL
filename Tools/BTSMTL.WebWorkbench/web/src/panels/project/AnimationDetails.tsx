import { useCallback, useEffect, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { AnimationClip, AnimationLibrary } from '../../api/types';
import { usePanelAnimationFrame, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { writeResourceReady } from '../../shell/readiness';
import { animationChoice, animationReferenceKind, resourceCache } from '../../state/resources';
import { referenceValueKey, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions, getState, resourceActions, selectionActions, useWorkbench } from '../../state/store';
import { BarSeparator, PanelBar } from '../../ui/blocks';
import { Button, NumberInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { ResourceScene } from '../resources/resourceScene';
import styles from '../resources/resources.module.css';
import projectStyles from './project.module.css';

// Project 中选中动画文件时的详情：文件内片段、素材播放与拖动时间。
// 素材时间只改 resource 分片，不经过 transport、不移动运行游标（D6）；按 D8 写入 resource* 就绪标记。
export function AnimationDetails({ library, clips }: { library: AnimationLibrary; clips: AnimationClip[] }) {
  const { identity, time, playing } = useWorkbench(useShallow(state => ({ identity: state.identity, time: state.time, playing: state.playing })));
  const field = useReferenceSelection(state => state.field);
  const animationField = field?.catalog.kind === 'ready' && field.catalog.referenceKind === animationReferenceKind ? field : null;
  const clip = clips.find(item => item.resource.identity === identity) ?? null;
  useEffect(() => {
    if (clip === null) resourceActions.select(clips[0].resource.identity);
  }, [clip, clips]);

  const duration = clip?.exportedDuration ?? 0;
  const outOfRange = !!clip && (time < 0 || time > duration);
  useEffect(() => {
    if (outOfRange) consoleActions.log('error', 'resources', `请求的素材秒数 ${time} 超出该片段的实际导出范围 0—${duration}`);
  }, [outOfRange, time, duration]);

  const last = useRef<number | null>(null);
  usePanelAnimationFrame(useCallback((now: number) => {
    const previous = last.current;
    last.current = now;
    if (previous === null) return;
    const next = Math.min(duration, getState().time + (now - previous) / 1000);
    resourceActions.setTime(next);
    if (next >= duration) resourceActions.setPlaying(false);
  }, [duration]), playing && !!clip);
  useEffect(() => { if (!playing) last.current = null; }, [playing]);

  return <div className={projectStyles.animation}>
    {clips.length > 1 && <div className={projectStyles.clipList}>{clips.map(item =>
      <button key={item.resource.identity} className={projectStyles.row} draggable={item.key != null}
        aria-selected={referenceValueKey(item.resource.identity) === referenceValueKey(identity)}
        onDragStart={event => { if (item.key != null) writeReferenceDrag(event.dataTransfer, animationReferenceKind, animationChoice(item)); }}
        onClick={() => resourceActions.select(item.resource.identity)}>
        <span>{item.name}</span><small>{item.duration.toFixed(3)} s · {item.frameRate} fps</small>
      </button>)}</div>}
    {clip && <div className={projectStyles.facts}>
      <strong>{clip.name}</strong>
      <small>{clip.category} · {clip.duration.toFixed(3)} s · {clip.frameRate} fps · {clip.provenance.description}</small>
    </div>}
    <PanelBar wrap>
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
      <Button variant="ghost" onClick={() => fitCurrent?.()}>适配角色</Button>
      {clip && <Button variant="ghost" onClick={() => selectionActions.select({ kind: 'resource', id: clip.resource.identity })}>检查器</Button>}
      {clip && animationField && <Button disabled={clip.key == null} tip={clip.key == null ? '该片段没有登记 animation-clip 键，请先登记键并重新导出动画库' : undefined}
        onClick={() => animationField.choose(animationChoice(clip))}>填入 {animationField.name}</Button>}
    </PanelBar>
    <ResourceCanvas library={library} />
  </div>;
}

let fitCurrent: (() => void) | null = null;

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
