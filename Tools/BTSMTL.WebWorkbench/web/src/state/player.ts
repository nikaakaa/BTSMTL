import { ApiError } from '../api/http';
import { openViewer, playerApi, sendViewerCommand } from '../api/player';
import { samePreviewLoad, type PlayerPreviewContent, type PreviewSampleSource } from '../api/playerRecords';
import type { CameraCommand, InputCommand, LiveCommand, LiveCommandResult, PlayerPick, VideoFrame, ViewerLive, ViewerMessage } from '../api/types';
import { consoleActions, getState, initialPlayerTarget, useWorkbench } from './store';
import { transport } from './transport';

// 播放器服务（D12）：画面 WebSocket、命令与回执由这里持有，随“载入／停止播放器”开关；
// 画面面板只在挂载时绘制最新画面，未挂载时只保留最新一帧，不解码。
// 播放、暂停、单步、跳帧由 transport 发送（D6），这里不提供第二套播放控制。

const source = 'player';
const pendingStartKey = 'btsmtl.player.pending-start';

let socket: WebSocket | null = null;
let latest: VideoFrame | null = null;
let requestedLoad: { commandId: string; content: PlayerPreviewContent } | null = null;
let deviceInput: InputCommand | null = null;
const pendingPicks = new Map<string, PreviewSampleSource>();
const videoListeners = new Set<() => void>();
const pickListeners = new Set<(pick: PlayerPick) => void>();

function setVideo(frame: VideoFrame | null) {
  latest = frame;
  videoListeners.forEach(listener => listener());
}

function matchesRequestedLoad(sample: PreviewSampleSource) {
  const request = requestedLoad;
  const content = sample.content;
  return request === null || sample.loadCommandId === request.commandId && content !== null && content.kind === request.content.kind &&
    content.revision === request.content.revision && (request.content.kind === 'scene' || content.documentId === request.content.timeline);
}

function acceptVideo(frame: VideoFrame) {
  const source = getState().live?.player?.source;
  if (source && matchesRequestedLoad(frame.source) && samePreviewLoad(frame.source, source)) setVideo(frame);
}

function acceptStatus(live: ViewerLive) {
  if (getState().liveId !== live.liveId) return;
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

useWorkbench.subscribe((state, previous) => {
  if (state.runId === previous.runId && state.generation === previous.generation) return;
  releaseDeviceInput();
  pendingPicks.clear();
});
