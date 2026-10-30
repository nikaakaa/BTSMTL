import { ApiError } from '../api/http';
import { playerApi } from '../api/player';
import { previewApi } from '../api/preview';
import {
  newPlayerJobId, readPlayerJobId, readPlayerJobRequest,
  type PlayerJobId, type PlayerJobReceipt, type PlayerJobStage,
} from '../api/playerRecords';
import { archive } from './archive';
import {
  connectionActions, consoleActions, contentActions, getState, isCurrent, runActions, useWorkbench,
} from './store';
import { transport } from './transport';

const source = 'run';
const pendingRequestKey = 'btsmtl.preview.pending-player-job';
const heartbeatMilliseconds = 3000;
const pollMilliseconds = 500;

export const stageNames: Record<PlayerJobStage, string> = {
  queued: '排队中', running: '运行中', completed: '已封存', failed: '执行失败',
  cancelled: '已取消', rejected: '被拒绝', unavailable: '不可用',
};
export const runLabel = (job: PlayerJobReceipt) =>
  `${new Date(job.acceptedAt).toLocaleString()} · ${stageNames[job.stage]}`;

let pollTimer: ReturnType<typeof setTimeout> | null = null;
let heartbeat: ReturnType<typeof setInterval> | null = null;
let requestedFrame = 0;
let controller: AbortController | null = null;

function selectedStart() {
  const state = getState();
  if (!state.hostAvailable) return { kind: 'unavailable', reason: '宿主不可用' } as const;
  const liveId = state.liveId;
  if (liveId === null) return { kind: 'unavailable', reason: '先选择一个 Player' } as const;
  if (!state.playerViewport) return { kind: 'unavailable', reason: '打开画面面板以确定本次画面尺寸' } as const;
  const scene = state.sources?.scenes.find(item => item.path === state.scenePath);
  if (!scene) return { kind: 'unavailable', reason: '先选择内容' } as const;
  const replay = state.sources?.replays.find(item => item.id === state.replayId);
  if (!replay) return { kind: 'unavailable', reason: '先选择输入来源' } as const;
  return { kind: 'selected', liveId, scene, source: replay.source, viewport: state.playerViewport } as const;
}

async function refresh() {
  const [sources, jobs] = await Promise.all([previewApi.sources(), playerApi.jobs()]);
  contentActions.setSources(sources);
  runActions.setArchiveJobs(jobs.items);
}

async function beat() {
  try {
    runActions.setArchiveJobs((await playerApi.jobs()).items);
    if (!getState().connected) {
      connectionActions.setConnected(true);
      consoleActions.log('info', 'connection', '宿主已重新连接');
    }
  } catch (error) {
    if (getState().connected) {
      connectionActions.setConnected(false, error instanceof Error ? error.message : String(error));
      consoleActions.error('connection', error);
    }
  }
}

function clearPoll() {
  if (pollTimer) clearTimeout(pollTimer);
  pollTimer = null;
}

async function updateRun(jobId: PlayerJobId, generation: number, signal: AbortSignal) {
  clearPoll();
  try {
    const { receipt } = await playerApi.job(jobId, signal);
    if (!isCurrent(generation)) return;
    runActions.setReceipt(generation, receipt);
    if (receipt.stage === 'queued' || receipt.stage === 'running') {
      pollTimer = setTimeout(() => void updateRun(receipt.jobId, generation, signal), pollMilliseconds);
      return;
    }
    if (receipt.stage !== 'completed') {
      const reason = receipt.error ?? stageNames[receipt.stage];
      const entryId = consoleActions.log(receipt.stage === 'failed' ? 'error' : 'warn', source, reason);
      runActions.setData(generation, { failure: { reason, entryId } });
      return;
    }
    if (!getState().sealed) await archive.open(receipt.jobId, generation, requestedFrame);
  } catch (error) {
    if (!isCurrent(generation)) return;
    const entryId = consoleActions.error(source, error);
    if (entryId !== null) runActions.setData(generation, { failure: { reason: 'Player 作业读取失败', entryId } });
  }
}

useWorkbench.subscribe((state, previous) => {
  if (state.frameIndex !== previous.frameIndex || state.runId !== previous.runId ||
    state.summary?.frameCount !== previous.summary?.frameCount) archive.loadFrame();
});

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

  selectRun(id: string | null, frameIndex = 0) {
    clearPoll();
    controller?.abort();
    archive.cancel();
    requestedFrame = Math.max(0, Math.trunc(frameIndex));
    const jobId = id === null ? null : readPlayerJobId(id);
    const generation = runActions.selectRun(jobId);
    transport.reset();
    if (jobId) {
      controller = new AbortController();
      void updateRun(jobId, generation, controller.signal);
    }
  },

  canStart(): string | null {
    const selected = selectedStart();
    return selected.kind === 'unavailable' ? selected.reason : null;
  },

  async startRun() {
    let pending = sessionStorage.getItem(pendingRequestKey);
    try {
      if (!pending) {
        const selected = selectedStart();
        if (selected.kind === 'unavailable') { consoleActions.log('warn', source, selected.reason); return; }
        const { scene, source: inputSource } = selected;
        const [options, live] = await Promise.all([
          playerApi.creationOptions({ requestId: newPlayerJobId(), source: inputSource, ...selected.viewport }),
          playerApi.live(selected.liveId),
        ]);
        const { frameRangeBasis, ...parameters } = options;
        consoleActions.log('info', 'run', frameRangeBasis);
        pending = JSON.stringify({ ...parameters, playerBuildId: live.buildId,
          scene: scene.path, sceneRevision: scene.revision, source: inputSource });
        sessionStorage.setItem(pendingRequestKey, pending);
      }
      const request = readPlayerJobRequest(JSON.parse(pending), 'pending-player-job');
      const receipt = await playerApi.startJob(request);
      sessionStorage.removeItem(pendingRequestKey);
      await refresh();
      session.selectRun(receipt.jobId);
    } catch (error) {
      const entryId = consoleActions.error(source, error);
      if (entryId !== null) {
        if (!(error instanceof ApiError && error.status === 0)) sessionStorage.removeItem(pendingRequestKey);
        session.selectRun(null);
        runActions.setData(getState().generation, { failure: { reason: 'Player 作业未启动', entryId } });
      }
    }
  },

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
