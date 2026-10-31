import { useEffect } from 'react';
import { getState, useWorkbench } from '../state/store';
import { visiblePanels } from './layout';
import { usePanelId, usePanelVisible, usePanelWindow } from './panel';
import { panelTypeOf, type PanelType } from './panelTypes';

// 截图就绪标记（D8）：Timeline、节点图与地址指定的面板都按当前帧实际绘制完成后一次写入；
// 只有可见面板参与，帧或运行变化时先删除就绪标记。

const rendered = new Map<string, { key: string; detail?: string }>();
// 按当前帧绘制、会报告已绘制的面板类型。
const frameDrawn = new Set<PanelType>(['timeline', 'graph', 'viewport', 'player']);
let addressPanel: PanelType | null = null;
let scheduled = false;

const root = () => document.documentElement.dataset;

export function setAddressPanel(type: PanelType | null) {
  addressPanel = type;
  schedule();
}

// 面板按某帧绘制完成后报告；detail 为逻辑视图实际画出的内容。
export function reportRendered(panelId: string, key: string, detail?: string) {
  rendered.set(panelId, { key, detail });
  schedule();
}

export function clearRendered(panelId: string) {
  if (rendered.delete(panelId)) schedule();
}

export function frameKey(runId: string | null, frameIndex: number): string | null {
  return runId ? `${runId}:${frameIndex}` : null;
}

export function useFrameReport(dependency: unknown) {
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  useEffect(() => {
    if (!visible) return;
    let handle = 0;
    const report = () => {
      panelWindow.cancelAnimationFrame(handle);
      handle = panelWindow.requestAnimationFrame(() => {
        const state = getState();
        const key = frameKey(state.runId, state.frameIndex);
        if (key) reportRendered(panelId, key);
      });
    };
    report();
    const unsubscribe = useWorkbench.subscribe((state, previous) => {
      if (state.frame !== previous.frame || state.frameKey !== previous.frameKey || state.frameIndex !== previous.frameIndex) report();
    });
    return () => { unsubscribe(); panelWindow.cancelAnimationFrame(handle); };
  }, [visible, panelWindow, panelId, dependency]);
  useEffect(() => () => clearRendered(panelId), [panelId]);
}

export function schedule() {
  if (scheduled) return;
  scheduled = true;
  queueMicrotask(() => { scheduled = false; evaluate(); });
}

function evaluate() {
  const state = getState();
  const key = frameKey(state.runId, state.frameIndex);
  const frame = state.frame;
  const data = root();
  for (const name of Object.keys(data)) if (name.startsWith('preview')) delete data[name];
  if (!key || !state.summary || state.frameKey !== key || !frame || !state.archiveMetadata) return;
  const visible = visiblePanels();
  const required = visible.filter(id => {
    const type = panelTypeOf(id);
    return type === 'timeline' || type === 'graph';
  });
  // 地址指定的面板必须可见；按帧绘制的类型还必须报告已绘制。
  if (addressPanel) {
    const target = visible.find(id => panelTypeOf(id) === addressPanel);
    if (!target) return;
    if (frameDrawn.has(addressPanel) && !required.includes(target)) required.push(target);
  }
  if (required.some(id => rendered.get(id)?.key !== key)) return;
  const viewport = visible.map(id => panelTypeOf(id) === 'viewport' ? rendered.get(id) : undefined)
    .find(entry => entry?.key === key && entry.detail);
  data.previewRun = state.summary.runId;
  data.previewFrame = frame.hostFrame;
  data.previewFrameIndex = String(state.frameIndex);
  if (frame.consistency) data.previewConsistency = frame.consistency.value;
  data.previewContent = state.summary.contentRevision;
  data.previewPlayerBuild = state.summary.playerBuildId;
  data.previewSceneRevision = state.summary.sceneRevision;
  data.previewInputSource = state.summary.inputSource;
  if (state.summary.inputRevision !== null) data.previewInputRevision = state.summary.inputRevision;
  data.previewInputMode = state.archiveMetadata.inputMode;
  data.previewArchiveJob = state.archiveMetadata.jobId;
  if (viewport?.detail) data.previewRendered = viewport.detail;
  data.previewImageReady = 'true';
}

useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) rendered.clear();
  if (state.runId !== previous.runId || state.frameIndex !== previous.frameIndex || state.frameKey !== previous.frameKey || state.frame !== previous.frame ||
    state.generation !== previous.generation || state.archiveFrame !== previous.archiveFrame ||
    state.openPanels !== previous.openPanels || state.activePanel !== previous.activePanel) schedule();
});

// 资源截图标记：资源面板按指定时刻实际绘制后写入，切换片段或时刻时先删除。
export function writeResourceReady(values: {
  identity: string; revision: string; library: string; source: string; time: number;
} | null) {
  const data = root();
  if (!values) {
    delete data.resourceImageReady;
    return;
  }
  data.resourceIdentity = values.identity;
  data.resourceRevision = values.revision;
  data.resourceLibrary = values.library;
  data.resourceSource = values.source;
  data.resourceTime = String(values.time);
  data.resourceImageReady = 'true';
}
