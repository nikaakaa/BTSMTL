import { ApiError } from '../api/http';
import { playerApi } from '../api/player';
import { previewApi } from '../api/preview';
import {
  newPlayerJobId, readPlayerJobId, readPlayerJobRequest,
  type PlayerJobId, type PlayerJobReceipt, type PlayerJobStage, type PlayerPreviewTarget,
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

export type ArchivePreviewTarget = Omit<PlayerPreviewTarget, 'channels' | 'source'> & {
  channels: NonNullable<PlayerPreviewTarget['channels']>;
  source: Exclude<PlayerPreviewTarget['source'], { kind: 'device' }>;
};

type RunPreparation = { kind: 'selection' } | {
  kind: 'adopted'; target: ArchivePreviewTarget; viewport: { width: number; height: number };
};

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
  const mode = state.host?.modes.find(item => item.id === state.previewMode);
  if (!mode) return { kind: 'unavailable', reason: '先选择预览档位' } as const;
  if (!mode.available || !mode.createsRun) return { kind: 'unavailable', reason: mode.reason } as const;
  const liveId = state.liveId;
  if (liveId === null) return { kind: 'unavailable', reason: '先选择一个 Player' } as const;
  if (!state.playerViewport) return { kind: 'unavailable', reason: '打开画面面板以确定本次画面尺寸' } as const;
  const scene = state.sources?.scenes.find(item => item.path === state.scenePath);
  if (!scene) return { kind: 'unavailable', reason: '先选择内容' } as const;
  const selectedInput = state.inputSource;
  if (selectedInput === null) return { kind: 'unavailable', reason: '先选择输入来源' } as const;
  if (selectedInput.kind === 'recorded') {
    const replay = state.sources?.replays.find(item => item.id === selectedInput.id);
    if (!replay) return { kind: 'unavailable', reason: '输入来源已变化，请重新选择' } as const;
    return { kind: 'selected', liveId, scene, source: replay.source, viewport: state.playerViewport } as const;
  }
  if (!state.inputCatalog) return { kind: 'unavailable', reason: '先读取当前场景的输入方案' } as const;
  for (const player of state.inputCatalog.players) {
    if (!player.localPlans.length) return { kind: 'unavailable', reason: `${player.actorId} 没有可用的本地输入方案` } as const;
    if (!state.inputChoices.some(choice => choice.actorId === player.actorId))
      return { kind: 'unavailable', reason: `先为 ${player.actorId} 选择输入方案` } as const;
  }
  if (state.localLastSdkFrame === null) return { kind: 'unavailable', reason: '填写本次结束 SDK 帧' } as const;
  return { kind: 'selected', liveId, scene, viewport: state.playerViewport,
    source: { kind: 'local', lastSdkFrame: state.localLastSdkFrame, players: state.inputChoices } } as const;
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
    const { receipt, archive: manifest } = await playerApi.job(jobId, signal);
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
      if (manifest === null || manifest.frameCount === 0) return;
    }
    if (!getState().sealed) await archive.open(receipt.jobId, generation, requestedFrame);
  } catch (error) {
    if (!isCurrent(generation)) return;
    const entryId = consoleActions.error(source, error);
    if (entryId !== null) runActions.setData(generation, { failure: { reason: 'Player 作业读取失败', entryId } });
  }
}

useWorkbench.subscribe((state, previous) => {
  if (state.selectedCurves !== previous.selectedCurves) archive.loadCurves();
  if (state.frameIndex !== previous.frameIndex || state.runId !== previous.runId ||
    state.summary?.frameCount !== previous.summary?.frameCount) archive.loadFrame();
});

export const session = {
  async prepareRun(input: RunPreparation, signal?: AbortSignal) {
    const selected = input.kind === 'selection' ? selectedStart() : {
      kind: 'selected' as const, liveId: input.target.liveId, source: input.target.source, viewport: input.viewport,
      scene: { path: input.target.scene, revision: input.target.sceneRevision },
    };
    if (selected.kind === 'unavailable') throw new Error(selected.reason);
    const [options, playerBuildId] = await Promise.all([
      playerApi.creationOptions({ requestId: newPlayerJobId(), source: selected.source, ...selected.viewport }, signal),
      input.kind === 'adopted' ? Promise.resolve(input.target.playerBuildId)
        : playerApi.live(selected.liveId, signal).then(live => live.buildId),
    ]);
    const { frameRangeBasis, ...parameters } = options;
    consoleActions.log('info', source, frameRangeBasis);
    return readPlayerJobRequest({ ...parameters, playerBuildId,
      channels: input.kind === 'adopted' ? input.target.channels : parameters.channels,
      scene: selected.scene.path, sceneRevision: selected.scene.revision, source: selected.source });
  },

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

  async cancelRun(jobId: PlayerJobId) {
    try {
      const result = await playerApi.cancelJob(jobId);
      consoleActions.log(result.cancellationRequested ? 'info' : 'warn', source, result.reason, JSON.stringify(result));
      await refresh();
    } catch (error) {
      consoleActions.error(source, error);
    }
  },

  async startRun() {
    let pending = sessionStorage.getItem(pendingRequestKey);
    try {
      if (!pending) {
        pending = JSON.stringify(await session.prepareRun({ kind: 'selection' }));
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

  async loadInputs(signal?: AbortSignal): Promise<number | null> {
    const scene = selectedScene();
    if (!scene) return null;
    const current = () => selectedScene()?.path === scene.path && selectedScene()?.revision === scene.revision;
    useWorkbench.setState({ inputCatalogReading: true, inputCatalogFailure: null });
    let failure: number | null = null;
    try {
      contentActions.setInputCatalog(scene.path, await previewApi.inputs(scene.path, scene.revision, signal));
    } catch (error) {
      failure = consoleActions.error('content', error);
    }
    if (current() && !signal?.aborted) useWorkbench.setState({ inputCatalogReading: false, inputCatalogFailure: failure });
    return failure;
  },
};

function selectedScene() {
  const { sources, scenePath } = getState();
  return sources?.scenes.find(item => item.path === scenePath);
}

// 选定场景或其修订变化后自动读取一次输入声明，换场景时放弃上一次读取；失败后由界面的重新读取按钮重试。
let inputsController: AbortController | null = null;
let inputsKey: string | null = null;
useWorkbench.subscribe((state, previous) => {
  if (state.scenePath === previous.scenePath && state.sources === previous.sources) return;
  const scene = selectedScene();
  const key = scene ? `${scene.path}\n${scene.revision}` : null;
  if (key === inputsKey) return;
  inputsKey = key;
  inputsController?.abort();
  inputsController = null;
  if (!scene) {
    useWorkbench.setState({ inputCatalogReading: false });
    return;
  }
  inputsController = new AbortController();
  void session.loadInputs(inputsController.signal);
});
