import { ApiError } from '../api/http';
import { playerApi } from '../api/player';
import { previewApi } from '../api/preview';
import type { Availability, GraphDefinition, RunReceipt, RunStage, Summary } from '../api/types';
import { invalidateTail, readFrame } from './frames';
import {
  connectionActions, consoleActions, contentActions, getState, inputRootKey, isCurrent, runActions, useWorkbench,
} from './store';
import { transport } from './transport';

// 运行服务：宿主连接、来源与运行列表、所选运行的路线与元数据、游标所在帧。

const source = 'run';
const pendingRequestKey = 'btsmtl.preview.pending-request';
const heartbeatMilliseconds = 3000;
const pollMilliseconds = 500;
const routePageSize = 128;
const metadataPageSize = 64;

export const stageNames: Record<RunStage, string> = {
  accepted: '准备中', running: '运行中', completed: '已封存', stopped: '已停止', failed: '执行失败', cancelled: '已取消',
};

export const runLabel = (run: RunReceipt) => `${new Date(run.acceptedAt).toLocaleString()} · ${stageNames[run.stage]}`;

let pollTimer: ReturnType<typeof setTimeout> | null = null;
let heartbeat: ReturnType<typeof setInterval> | null = null;
let requestedFrame = 0;

// ── 连接与列表 ──

async function refresh() {
  const [sources, runs] = await Promise.all([previewApi.sources(), previewApi.runs()]);
  contentActions.setSources(sources);
  runActions.setRuns(runs);
  const state = getState();
  if (!state.scenePath && sources.scenes.length) contentActions.selectScene(sources.scenes[0].path);
  if (!state.replayId && sources.replays.length) contentActions.selectReplay(sources.replays[0].id);
}

async function beat() {
  try {
    runActions.setRuns(await previewApi.runs());
    if (!getState().connected) {
      connectionActions.setConnected(true);
      consoleActions.log('info', 'connection', '宿主已重新连接');
    }
  } catch (error) {
    if (getState().connected) {
      const reason = error instanceof Error ? error.message : String(error);
      connectionActions.setConnected(false, reason);
      consoleActions.error('connection', error);
    }
  }
}

// ── 所选运行 ──

function clearPoll() {
  if (pollTimer) clearTimeout(pollTimer);
  pollTimer = null;
}

async function readPages<P extends { items: unknown[]; nextOffset: number | null }>(read: (offset: number) => Promise<P>) {
  const items: P['items'][number][] = [];
  let offset: number | null = 0;
  let last: P | null = null;
  while (offset !== null) {
    last = await read(offset);
    items.push(...last.items);
    offset = last.nextOffset;
  }
  return { items, last: last! };
}

async function loadMetadata(runId: string, generation: number) {
  try {
    const [graphs, inputs, timelines] = await Promise.all([
      readPages(offset => previewApi.graphs(runId, { offset, limit: metadataPageSize })),
      previewApi.inputTimeline(runId),
      readPages(offset => previewApi.timelines(runId, offset, metadataPageSize)),
    ]);
    runActions.setData(generation, {
      graphs: graphs.items as GraphDefinition[],
      graphAvailability: graphs.last.availability,
      inputTimeline: inputs,
      inputRoot: inputs.roots.length ? inputRootKey(inputs.roots[0]) : null,
      timelines: timelines.items.map(item => item.timeline),
      metadataAvailability: [...inputs.availability, ...timelines.last.availability],
    });
  } catch (error) {
    if (isCurrent(generation)) consoleActions.error(source, error);
  }
}

async function loadGeometry(runId: string, generation: number) {
  try {
    const geometry = await previewApi.geometry(runId);
    runActions.setData(generation, { geometry });
  } catch (error) {
    if (isCurrent(generation)) consoleActions.error(source, error);
  }
}

// 路线帧补齐到帧数，登记时间基准。
async function syncRoute(runId: string, summary: Summary, generation: number) {
  let frames = getState().routeFrames;
  let availability: Availability[] = getState().routeAvailability;
  let protocol = getState().routeProtocol;
  while (frames.length < summary.frameCount) {
    const page = await previewApi.routeFrames(runId, {
      firstFrame: summary.firstFrame, lastFrame: summary.lastFrame,
      offset: frames.length, limit: Math.min(routePageSize, summary.frameCount - frames.length),
    });
    if (!isCurrent(generation)) return;
    if (!page.items.length) break;
    frames = [...frames, ...page.items.map(({ hostFrame, seconds, playbacks }) => ({ hostFrame, seconds, playbacks }))];
    availability = page.availability;
    protocol = page.protocol;
  }
  if (frames === getState().routeFrames) return;
  runActions.setData(generation, { routeFrames: frames, routeAvailability: availability, routeProtocol: protocol });
  transport.setTimebase(runId, frames.map(frame => frame.hostFrame), frames.map(frame => frame.seconds));
}

async function display(runId: string, summary: Summary, sealed: boolean, generation: number) {
  const previous = getState().summary;
  const first = previous === null;
  if (!sealed && previous && summary.frameCount > previous.frameCount) invalidateTail(runId, previous.frameCount);
  runActions.setSummary(generation, summary, sealed);
  if (first) {
    const scene = getState().sources?.scenes.find(item => item.revision === summary.contentRevision);
    if (scene) contentActions.selectScene(scene.path);
    void loadMetadata(runId, generation);
    if (summary.geometryAvailable) void loadGeometry(runId, generation);
  }
  await syncRoute(runId, summary, generation);
  if (!isCurrent(generation)) return;
  if (first && summary.frameCount) {
    if (sealed && requestedFrame >= summary.frameCount)
      consoleActions.log('error', source, `帧序号 ${requestedFrame} 超出本次已封存范围（共 ${summary.frameCount} 帧）`);
    transport.place(Math.min(requestedFrame, summary.frameCount - 1));
  }
  if (!sealed) transport.framesGrown();
}

async function updateRun(runId: string, generation: number) {
  clearPoll();
  try {
    const receipt = await previewApi.run(runId);
    if (!isCurrent(generation)) return;
    runActions.setReceipt(generation, receipt);
    if (receipt.stage === 'accepted' || receipt.stage === 'running') {
      const playback = await previewApi.playback(runId);
      if (!isCurrent(generation)) return;
      if (playback.ready && playback.snapshot) {
        runActions.setHostPlayback(generation, playback.snapshot.state);
        await display(runId, playback.snapshot.observation.summary, false, generation);
      }
      if (isCurrent(generation)) pollTimer = setTimeout(() => void updateRun(runId, generation), pollMilliseconds);
      return;
    }
    runActions.setHostPlayback(generation, null);
    if (receipt.stage === 'failed' || receipt.stage === 'cancelled') {
      const reason = receipt.error ? receipt.error.split(/\r?\n/, 1)[0].replace(/^[\w.]+Exception: /, '') : stageNames[receipt.stage];
      const entryId = consoleActions.log(receipt.stage === 'failed' ? 'error' : 'warn', source, reason, receipt.error ?? undefined);
      runActions.setData(generation, { failure: { reason, entryId } });
      return;
    }
    if (getState().sealed) return;
    const summary = await previewApi.summary(runId);
    if (!isCurrent(generation)) return;
    await display(runId, summary, true, generation);
  } catch (error) {
    if (!isCurrent(generation)) return;
    const entryId = consoleActions.error(source, error);
    if (entryId !== null && !getState().summary)
      runActions.setData(generation, { failure: { reason: '运行读取失败', entryId } });
  }
}

// ── 游标所在帧：游标或帧数变化时读取，读取中保留上一帧 ──

let frameRequest = 0;
function loadCurrentFrame() {
  const { runId, frameIndex, summary, generation } = getState();
  if (!runId || !summary?.frameCount) return;
  const key = `${runId}:${frameIndex}`;
  if (getState().frameKey === key) return;
  const request = ++frameRequest;
  readFrame(frameIndex).then(frame => {
    if (request !== frameRequest || !frame || !isCurrent(generation)) return;
    runActions.setData(generation, { frame, frameKey: key });
  }, error => {
    if (isCurrent(generation)) consoleActions.error(source, error);
  });
}

useWorkbench.subscribe((state, previous) => {
  if (state.frameIndex !== previous.frameIndex || state.runId !== previous.runId ||
    state.summary?.frameCount !== previous.summary?.frameCount) loadCurrentFrame();
});

// ── 公开入口 ──

export const session = {
  async connect() {
    try {
      const host = await previewApi.host();
      connectionActions.setHost(host.available, host.description);
      if (!host.available) {
        consoleActions.log('warn', 'connection', '当前工作台未接入运行宿主');
        return;
      }
      void playerApi.images().then(images => connectionActions.setImages(images.description),
        error => consoleActions.error('connection', error));
      await refresh();
    } catch (error) {
      connectionActions.setConnected(false, error instanceof Error ? error.message : String(error));
      consoleActions.error('connection', error);
    }
    heartbeat ??= setInterval(() => void beat(), heartbeatMilliseconds);
    if (sessionStorage.getItem(pendingRequestKey)) await session.startRun();
  },

  refresh: () => refresh().catch(error => consoleActions.error(source, error)),

  selectRun(runId: string | null, frameIndex = 0) {
    clearPoll();
    requestedFrame = Math.max(0, Math.trunc(frameIndex));
    const generation = runActions.selectRun(runId);
    transport.reset();
    if (runId) void updateRun(runId, generation);
  },

  canStart(): string | null {
    const { sources, scenePath, replayId, sourceKind, mode, hostAvailable } = getState();
    if (!hostAvailable) return '宿主不可用';
    if (mode === 'off') return '预览已关闭';
    const scene = sources?.scenes.find(item => item.path === scenePath);
    if (!scene) return '先选择内容';
    if (sourceKind === 'local') return '宿主尚未提供输入方案目录';
    if (!sources?.replays.some(item => item.id === replayId)) return '先选择录像';
    return null;
  },

  // 新建运行：请求身份先存入本页会话，提交结果未确认时刷新页面会按原身份重查。
  async startRun() {
    let pending = sessionStorage.getItem(pendingRequestKey);
    if (!pending) {
      const reason = session.canStart();
      if (reason) { consoleActions.log('warn', source, reason); return; }
      const { sources, scenePath, replayId, interactive } = getState();
      const scene = sources!.scenes.find(item => item.path === scenePath)!;
      const replay = sources!.replays.find(item => item.id === replayId)!;
      pending = JSON.stringify({
        requestId: crypto.randomUUID(), scene: scene.path, sceneRevision: scene.revision, source: replay.source, interactive,
      });
      sessionStorage.setItem(pendingRequestKey, pending);
    }
    try {
      const receipt = await previewApi.start(JSON.parse(pending));
      sessionStorage.removeItem(pendingRequestKey);
      await refresh();
      session.selectRun(receipt.jobId);
    } catch (error) {
      const entryId = consoleActions.error(source, error);
      if (entryId !== null) {
        // 宿主未响应时保留请求身份，下次按原身份重查；宿主明确拒绝时清除。
        if (!(error instanceof ApiError && error.status === 0)) sessionStorage.removeItem(pendingRequestKey);
        session.selectRun(null);
        runActions.setData(getState().generation, { failure: { reason: '运行未启动', entryId } });
      }
    }
  },

  // 失败时返回控制台记录号，面板显示一行错误。
  async loadInputs(): Promise<number | null> {
    const { sources, scenePath } = getState();
    const scene = sources?.scenes.find(item => item.path === scenePath);
    if (!scene) return null;
    try {
      contentActions.setInputCatalog(scene.path, await previewApi.inputs(scene.path, scene.revision));
      return null;
    } catch (error) {
      return consoleActions.error('content', error);
    }
  },
};
