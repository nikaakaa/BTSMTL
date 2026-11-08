import { ApiError } from '../api/http';
import { openViewer, playerApi, sendViewerCommand } from '../api/player';
import { samePreviewLoad, type PlayerPreviewContent, type PreviewSampleSource } from '../api/playerRecords';
import type { CameraCommand, HostLiveCommand, InputCommand, LiveCommand, LiveCommandResult, PlayerAck, PlayerPick, VideoFrame, ViewerLive, ViewerMessage } from '../api/types';
import { releaseSurface, shareSurface, sharedSurfaceAvailable } from '../shell/sharedSurface';
import { consoleActions, getState, initialPlayerTarget, selectedScene, useWorkbench } from './store';
import { transport } from './transport';

// 播放器服务（D12）：画面 WebSocket、命令与回执由这里持有，随“载入／停止播放器”开关；
// 画面面板只在挂载时绘制最新画面，未挂载时只保留最新一帧，不解码。
// 播放、暂停、单步、跳帧由 transport 发送（D6），这里不提供第二套播放控制。

const source = 'player';
const pendingStartKey = 'btsmtl.player.pending-start';
// 常驻播放器在桌面壳里的共享画面区身份；该类画面消息不带图像，像素按采样序号在画面区帧槽里取。
export const liveSurface = 'live';
export const surfaceMediaType = 'application/x-btsmtl-surface';
// 已把画面区接给的播放器与进程；进程变化（换代码重启）或换播放器时重新接入。
let surfaceTarget: { liveId: string; processId: number } | null = null;

function syncSurface(live: ViewerLive) {
  if (!sharedSurfaceAvailable || live.processId === null || live.width === null || live.height === null) return;
  if (surfaceTarget?.liveId === live.liveId && surfaceTarget.processId === live.processId) return;
  const target = surfaceTarget = { liveId: live.liveId, processId: live.processId };
  const { width, height } = live;
  void (async () => {
    try {
      const { handle, bytes } = await shareSurface(liveSurface, width, height, target.processId);
      if (surfaceTarget !== target) return;
      await command('surface', liveId => liveId === target.liveId ? playerApi.command(liveId,
        { type: 'surface', commandId: crypto.randomUUID(), surfaceHandle: handle, surfaceBytes: bytes, width, height }) : Promise.resolve(null));
    } catch (error) {
      if (surfaceTarget === target) surfaceTarget = null;
      consoleActions.error(source, error);
    }
  })();
}

let socket: WebSocket | null = null;
let latest: VideoFrame | null = null;
let requestedLoad: { commandId: string; content: PlayerPreviewContent } | null = null;
let deviceInput: InputCommand | null = null;
const pendingPicks = new Map<string, PreviewSampleSource>();
const videoListeners = new Set<() => void>();
const pickListeners = new Set<(pick: PlayerPick) => void>();
const ackListeners = new Set<(ack: PlayerAck) => void>();

function setVideo(frame: VideoFrame | null) {
  latest = frame;
  videoListeners.forEach(listener => listener());
}

function matchesRequestedLoad(sample: PreviewSampleSource) {
  const request = requestedLoad;
  const content = sample.content;
  return request === null || sample.loadCommandId === request.commandId && content !== null && content.kind === request.content.kind &&
    content.revision === request.content.revision && (request.content.kind !== 'timeline' || content.documentId === request.content.timeline);
}

function acceptVideo(frame: VideoFrame) {
  const source = getState().live?.player?.source;
  if (source && matchesRequestedLoad(frame.source) && samePreviewLoad(frame.source, source)) setVideo(frame);
}

function acceptStatus(live: ViewerLive) {
  if (getState().liveId !== live.liveId) return;
  syncSurface(live);
  const previous = getState().live?.player?.source ?? null;
  const sample = live.player?.source ?? null;
  if (sample !== null && !matchesRequestedLoad(sample)) return;
  if (sample !== null) requestedLoad = null;
  if (!samePreviewLoad(previous, sample)) {
    pendingPicks.clear();
    deviceInput = null;
  }
  useWorkbench.setState({ live, ...(samePreviewLoad(previous, sample) ? {} : initialPlayerTarget) });
  if (latest && !samePreviewLoad(latest.source, sample)) setVideo(null);
}

function onMessage(message: ViewerMessage) {
  switch (message.type) {
    case 'status': {
      acceptStatus(message.live);
      break;
    }
    case 'ack':
      transport.acceptAck(message);
      ackListeners.forEach(listener => listener(message));
      if (!message.ok) consoleActions.log('error', source, `命令被拒绝：${message.error ?? ''}`, message.rawJson);
      else {
        if (message.path) consoleActions.log('info', source, `截图已保存：${message.path}`, message.rawJson);
      }
      break;
    case 'pick': {
      const target = pendingPicks.get(message.commandId);
      pendingPicks.delete(message.commandId);
      if (target !== undefined && samePreviewLoad(target, getState().live?.player?.source ?? null))
        pickListeners.forEach(listener => listener(message));
      break;
    }
    case 'error':
      consoleActions.log('error', source, message.displayName ? `${message.displayName}：${message.error}` : message.error, message.rawJson);
      break;
    case 'extension':
      if (message.error) consoleActions.log('error', source, `${message.displayName}：${message.error}`, message.rawJson);
      break;
  }
}

function close() {
  releaseDeviceInput();
  pendingPicks.clear();
  const current = socket;
  socket = null;
  current?.close();
  requestedLoad = null;
  setVideo(null);
  if (surfaceTarget !== null) {
    surfaceTarget = null;
    releaseSurface(liveSurface);
  }
}

async function command(type: string, send: (liveId: string) => Promise<LiveCommandResult | null>) {
  const liveId = getState().liveId;
  const context = getState().live?.player?.source ?? null;
  if (!liveId) return null;
  try {
    const result = await send(liveId);
    if (result === null || getState().liveId !== liveId ||
      type !== 'load' && !samePreviewLoad(context, getState().live?.player?.source ?? null)) return null;
    if (result.stage === 'rejected' || result.stage === 'not-applied')
      consoleActions.log('error', source, `${type}：${result.ack?.error ?? '播放器未执行'}`, result.ack?.rawJson);
    else if (result.stage === 'submitted')
      consoleActions.log('warn', source, `${type}：等待时限内未收到播放器回执`);
    return result;
  } catch (error) {
    consoleActions.error(source, error);
    return null;
  }
}

// 相机、输入与点选走画面连接；点选回执由原命令身份关联。
function viewerSend(message: LiveCommand) {
  if (!socket || socket.readyState !== WebSocket.OPEN) return false;
  try {
    sendViewerCommand(socket, message);
    return true;
  } catch (error) {
    consoleActions.error(source, error);
    return false;
  }
}

function releaseDeviceInput() {
  const previous = deviceInput;
  if (previous === null) return;
  deviceInput = null;
  viewerSend({ type: 'input', keys: [], mouseDx: 0, mouseDy: 0, mouseButtons: 0, scroll: 0,
    x: previous.x, y: previous.y, lx: 0, ly: 0, rx: 0, ry: 0, lt: 0, rt: 0, padButtons: 0 });
}

export const playerService = {
  // 宿主扩展命令：与内置命令同一条命令通道与回执记录。
  hostCommand: (message: HostLiveCommand) => command(message.type, liveId => playerApi.command(liveId, message)),
  subscribeAck(listener: (ack: PlayerAck) => void) {
    ackListeners.add(listener);
    return () => { ackListeners.delete(listener); };
  },
  latestVideo: () => latest,
  subscribeVideo(listener: () => void) {
    videoListeners.add(listener);
    return () => { videoListeners.delete(listener); };
  },
  subscribePick(listener: (pick: PlayerPick) => void) {
    pickListeners.add(listener);
    return () => { pickListeners.delete(listener); };
  },

  async refresh() {
    try {
      const [builds, lives] = await Promise.all([playerApi.builds(), playerApi.lives()]);
      useWorkbench.setState({ playerBuilds: builds.items, lives });
      const state = getState(), selected = lives.find(live => live.liveId === state.liveId);
      if (!state.viewerOpen && selected) acceptStatus(selected);
      connectDefault();
    } catch (error) {
      consoleActions.error(source, error);
    }
  },

  async updateCode(playerBuildId: string) {
    try {
      const result = await playerApi.updateCode(crypto.randomUUID(), playerBuildId);
      const raw = JSON.stringify(result);
      switch (result.state) {
        case 'rejected':
          consoleActions.log('error', source, `代码更新被拒绝：${result.refusals.join('\n')}`, raw);
          break;
        case 'unchanged':
          consoleActions.log('info', source, 'Player 代码没有变化，继续使用当前版本。', raw);
          break;
        case 'created':
          consoleActions.log('info', source, `新代码版本已准备：${result.buildId}。可采用到所选播放器，或从构建菜单启动。`, raw);
          await playerService.refresh();
          break;
      }
      return result;
    } catch (error) {
      consoleActions.error(source, error);
      return null;
    }
  },

  async adoptCode(liveId: string, playerBuildId: string) {
    releaseDeviceInput();
    pendingPicks.clear();
    try {
      await playerApi.swap(liveId, playerBuildId);
      await playerService.refresh();
      return true;
    } catch (error) {
      consoleActions.error(source, error);
      return false;
    }
  },

  attach(liveId: string | null) {
    if (getState().liveId === liveId && socket) return;
    close();
    useWorkbench.setState({ liveId, live: null, viewerOpen: false, ...initialPlayerTarget });
    if (!liveId) return;
    const opened = openViewer(liveId, {
      open: () => { if (socket === opened) useWorkbench.setState({ viewerOpen: true }); },
      message: message => { if (socket === opened) onMessage(message); },
      video: frame => { if (socket === opened) acceptVideo(frame); },
      error: error => {
        if (socket !== opened) return;
        releaseDeviceInput();
        pendingPicks.clear();
        setVideo(null);
        useWorkbench.setState({ live: null, ...initialPlayerTarget });
        consoleActions.error(source, error);
      },
      close: () => {
        if (socket !== opened) return;
        pendingPicks.clear();
        deviceInput = null;
        socket = null;
        setVideo(null);
        useWorkbench.setState({ viewerOpen: false, live: null, ...initialPlayerTarget });
        consoleActions.log('warn', source, '与播放器画面的连接已断开');
      },
    });
    socket = opened;
  },

  // 启动请求身份先存入本页会话，结果未确认时刷新页面按原身份重查。
  async start(playerBuildId: string, width: number) {
    const selectedLiveId = getState().liveId;
    let pending = sessionStorage.getItem(pendingStartKey);
    if (!pending) {
      const evenWidth = Math.max(640, Math.round(width / 16) * 16);
      pending = JSON.stringify({ requestId: crypto.randomUUID(), playerBuildId, width: evenWidth, height: Math.round(evenWidth * 9 / 16) });
      sessionStorage.setItem(pendingStartKey, pending);
    }
    try {
      const live = await playerApi.start(JSON.parse(pending));
      sessionStorage.removeItem(pendingStartKey);
      await playerService.refresh();
      if (getState().liveId === selectedLiveId) playerService.attach(live.liveId);
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 0)) sessionStorage.removeItem(pendingStartKey);
      consoleActions.error(source, error);
    }
  },

  resumePendingStart() {
    const pending = sessionStorage.getItem(pendingStartKey);
    if (pending) void playerService.start((JSON.parse(pending) as { playerBuildId: string }).playerBuildId, 0);
  },

  async stop() {
    const liveId = getState().liveId;
    if (!liveId) return;
    releaseDeviceInput();
    pendingPicks.clear();
    try {
      await playerApi.stop(liveId);
    } catch (error) {
      consoleActions.error(source, error);
    }
    if (getState().liveId === liveId) playerService.attach(null);
    await playerService.refresh();
  },

  loadContent: (content: PlayerPreviewContent) =>
    command('load', async liveId => {
      releaseDeviceInput();
      pendingPicks.clear();
      const commandId = crypto.randomUUID();
      const request = requestedLoad = { commandId, content };
      setVideo(null);
      useWorkbench.setState({ live: null, ...initialPlayerTarget });
      const live = await playerApi.live(liveId);
      if (getState().liveId !== liveId || requestedLoad !== request) return null;
      const result = await playerApi.load(liveId, { commandId, content,
        executionMode: 'interactive', channels: live.identity.channels });
      return requestedLoad === request || getState().live?.player?.source?.loadCommandId === commandId ? result : null;
    }),
  async capture() {
    const liveId = getState().liveId;
    const commandId = crypto.randomUUID();
    const result = await command('capture', id => playerApi.command(id, { type: 'capture', commandId }));
    if (liveId && result?.stage === 'applied' && result.ack?.path) window.open(playerApi.capturePath(liveId, commandId), '_blank');
  },

  camera: (camera: CameraCommand) => viewerSend({ type: 'camera', ...camera }),
  input(input: InputCommand) {
    const state = getState();
    const target = state.live?.player;
    if (state.runId !== null || target?.source == null || target.inputMode !== 'device') return false;
    const sent = viewerSend({ type: 'input', ...input });
    if (sent) deviceInput = input;
    return sent;
  },
  releaseInput: releaseDeviceInput,
  pick(x: number, y: number): string | null {
    const state = getState();
    const target = state.live?.player?.source;
    if (state.runId !== null || target == null) return null;
    const commandId = crypto.randomUUID();
    pendingPicks.set(commandId, target);
    if (viewerSend({ type: 'pick', commandId, x, y })) return commandId;
    pendingPicks.delete(commandId);
    return null;
  },
  connected: () => !!socket && socket.readyState === WebSocket.OPEN,
};

// 打开工作台即接入播放器：沿用正在运行的一台，没有就按最新构建启动一台（本页只自动启动一次）。
// 画面连上后自动载入当前所选场景，用实时设备输入试玩；同一播放器与场景修订只自动载入一次，失败留给手动“载入”。
let autoStarted = false;
const autoLoaded = new Set<string>();

function connectDefault() {
  const state = getState();
  if (state.liveId !== null || state.runId !== null || sessionStorage.getItem(pendingStartKey)) return;
  // 快档推导占用的播放器由快档作业持有，不接管。
  const running = state.lives.find(live => live.state !== 'exited' && live.previewTarget?.content.kind !== 'quick');
  if (running) {
    playerService.attach(running.liveId);
    return;
  }
  const build = state.playerBuilds?.[0];
  if (autoStarted || build === undefined) return;
  autoStarted = true;
  void playerService.start(build.buildId, 1280);
}

export function sceneLoadContent(scene: { path: string; revision: string }): PlayerPreviewContent {
  return { kind: 'scene', scene: scene.path, revision: scene.revision, source: { kind: 'device' } };
}

// 场景载入常超过命令回执的等待时限，自动播放等状态里出现本次载入的内容再发。
let autoPlay: string | null = null;

function loadDefault(state: ReturnType<typeof getState>) {
  const live = state.live, scene = selectedScene(state);
  if (state.runId !== null || state.previewMode === 'quick' || !state.viewerOpen || live === null || scene === undefined ||
    live.hello === null || live.identity.liveProtocol !== 'btsmtl.preview-live/1' || requestedLoad !== null) return;
  const source = live.player?.source;
  if (autoPlay !== null && source?.loadCommandId === autoPlay) {
    if (live.player?.state === 'loading') return;
    autoPlay = null;
    if (live.player?.state === 'paused')
      void command('play', liveId => playerApi.command(liveId, { type: 'play', commandId: crypto.randomUUID() }));
    return;
  }
  if (source?.content?.kind === 'scene' && source.content.revision === scene.revision) return;
  const key = JSON.stringify([live.liveId, scene.path, scene.revision]);
  if (autoLoaded.has(key)) return;
  autoLoaded.add(key);
  void playerService.loadContent(sceneLoadContent(scene));
  autoPlay = (requestedLoad as { commandId: string } | null)?.commandId ?? null;
}

useWorkbench.subscribe((state, previous) => {
  if (state.live !== previous.live || state.viewerOpen !== previous.viewerOpen || state.contentSelection !== previous.contentSelection ||
    state.sources !== previous.sources || state.runId !== previous.runId || state.previewMode !== previous.previewMode) loadDefault(state);
});

useWorkbench.subscribe((state, previous) => {
  if (state.runId === previous.runId && state.generation === previous.generation) return;
  releaseDeviceInput();
  pendingPicks.clear();
});
