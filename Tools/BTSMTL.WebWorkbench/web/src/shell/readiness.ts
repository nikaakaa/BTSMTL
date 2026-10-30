import { getState, useWorkbench } from '../state/store';
import { visiblePanels } from './layout';
import { panelTypeOf, type PanelType } from './panelTypes';

// 截图就绪标记（D8）：Timeline、节点图与地址指定的面板都按当前帧实际绘制完成后一次写入；
// 只有可见面板参与，帧或运行变化时先删除就绪标记。

const rendered = new Map<string, { key: string; detail?: string }>();
// 按当前帧绘制、会报告已绘制的面板类型。
const frameDrawn = new Set<PanelType>(['timeline', 'graph', 'viewport', 'player']);
let addressPanel: PanelType | null = null;
let currentKey: string | null = null;
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

export function schedule() {
  if (scheduled) return;
  scheduled = true;
  queueMicrotask(() => { scheduled = false; evaluate(); });
}

function evaluate() {
  const state = getState();
  const key = frameKey(state.runId, state.frameIndex);
  delete root().previewImageReady;
  if (key !== currentKey) {
    currentKey = key;
    delete root().previewImageReady;
  }
  if (!key || !state.summary || state.frameKey !== key || !state.archiveFrame) return;
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
  const data = root();
  data.previewRun = state.summary.runId;
  data.previewFrame = state.frame?.hostFrame ?? state.hostFrame ?? '';
  data.previewFrameIndex = String(state.frameIndex);
  data.previewConsistency = state.frame?.consistency?.value ?? state.archiveFrame?.logic?.checksum ?? '';
  data.previewContent = state.summary.contentRevision;
  data.previewPlayerBuild = state.summary.playerBuildId;
  data.previewSceneRevision = state.summary.sceneRevision;
  data.previewInputSource = state.summary.inputSource;
  data.previewInputRevision = state.summary.inputRevision;
  if (state.archiveMetadata) data.previewArchiveJob = state.archiveMetadata.jobId;
  else delete data.previewArchiveJob;
  if (viewport?.detail) data.previewRendered = viewport.detail;
  else delete data.previewRendered;
  data.previewImageReady = 'true';
}

useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) rendered.clear();
  if (state.runId !== previous.runId || state.frameIndex !== previous.frameIndex || state.frameKey !== previous.frameKey ||
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
