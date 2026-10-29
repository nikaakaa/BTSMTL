import { ApiError } from '../api/http';
import { openViewer, playerApi, sendViewerCommand } from '../api/player';
import type { CameraCommand, InputCommand, LiveCommand, PlayerPick, VideoFrame, ViewerMessage } from '../api/types';
import { consoleActions, getState, initialPlayerTarget, useWorkbench } from './store';

// 播放器服务（D12）：画面 WebSocket、命令与回执由这里持有，随“载入／停止播放器”开关；
// 画面面板只在挂载时绘制最新画面，未挂载时只保留最新一帧，不解码。
// 播放、暂停、单步、跳帧由 transport 发送（D6），这里不提供第二套播放控制。

const source = 'player';
const pendingStartKey = 'btsmtl.player.pending-start';

let socket: WebSocket | null = null;
let latest: VideoFrame | null = null;
const videoListeners = new Set<() => void>();
const pickListeners = new Set<(pick: PlayerPick) => void>();

function setVideo(frame: VideoFrame | null) {
  latest = frame;
  videoListeners.forEach(listener => listener());
}

function onMessage(message: ViewerMessage) {
  switch (message.type) {
    case 'status':
      useWorkbench.setState({ live: message.live, alignment: message.live.alignment });
      break;
    case 'frames': {
      const last = message.items.at(-1);
      useWorkbench.setState({ alignment: message.alignment, ...(last ? { actualFrame: last.hostFrame } : {}) });
      break;
    }
    case 'ack':
      if (!message.ok) consoleActions.log('error', source, `命令被拒绝：${message.error ?? ''}`, message.rawJson);
      else if (message.path) consoleActions.log('info', source, `截图已保存：${message.path}`, message.rawJson);
      break;
    case 'pick':
      pickListeners.forEach(listener => listener(message));
      break;
    case 'error':
      consoleActions.log('error', source, message.displayName ? `${message.displayName}：${message.error}` : message.error, message.rawJson);
      break;
    case 'extension':
      if (message.error) consoleActions.log('error', source, `${message.displayName}：${message.error}`, message.rawJson);
      break;
  }
}

function close() {
  const current = socket;
  socket = null;
  current?.close();
  setVideo(null);
}

async function command(liveCommand: LiveCommand) {
  const liveId = getState().liveId;
  if (!liveId) return null;
  try {
    const result = await playerApi.command(liveId, liveCommand);
    if (result.stage === 'rejected' || result.stage === 'not-applied')
      consoleActions.log('error', source, `${liveCommand.type}：${result.ack?.error ?? '播放器未执行'}`, result.ack?.rawJson);
    else if (result.stage === 'submitted')
      consoleActions.log('warn', source, `${liveCommand.type}：等待时限内未收到播放器回执`);
    return result;
  } catch (error) {
    consoleActions.error(source, error);
    return null;
  }
}

// 无回执的高频消息（相机、输入、点选）走画面连接。
function viewerSend(message: LiveCommand) {
  if (!socket || socket.readyState !== WebSocket.OPEN) return;
  try {
    sendViewerCommand(socket, message);
  } catch (error) {
    consoleActions.error(source, error);
  }
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
    } catch (error) {
      consoleActions.error(source, error);
    }
  },

  attach(liveId: string | null) {
    if (getState().liveId === liveId && socket) return;
    close();
    useWorkbench.setState({ liveId, live: null, alignment: null, viewerOpen: false, ...initialPlayerTarget });
    if (!liveId) return;
    const opened = openViewer(liveId, {
      open: () => { if (socket === opened) useWorkbench.setState({ viewerOpen: true }); },
      message: message => { if (socket === opened) onMessage(message); },
      video: frame => { if (socket === opened) setVideo(frame); },
      close: () => {
        if (socket !== opened) return;
        socket = null;
        useWorkbench.setState({ viewerOpen: false });
        consoleActions.log('warn', source, '与播放器画面的连接已断开');
      },
    });
    socket = opened;
  },

  // 启动请求身份先存入本页会话，结果未确认时刷新页面按原身份重查。
  async start(playerBuildId: string, width: number) {
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
      playerService.attach(live.liveId);
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
    try {
      await playerApi.stop(liveId);
    } catch (error) {
      consoleActions.error(source, error);
    }
    playerService.attach(null);
    await playerService.refresh();
  },

  loadRun: (runId: string) => command({ type: 'load', commandId: crypto.randomUUID(), runId }),
  loadScene: (scene: string, sceneRevision: string) =>
    command({ type: 'load', commandId: crypto.randomUUID(), scene, sceneRevision, inputMode: 'device' }),
  async capture() {
    const liveId = getState().liveId;
    const commandId = crypto.randomUUID();
    const result = await command({ type: 'capture', commandId });
    if (liveId && result?.stage === 'applied' && result.ack?.path) window.open(playerApi.capturePath(liveId, commandId), '_blank');
  },

  camera: (camera: CameraCommand) => viewerSend({ type: 'camera', ...camera }),
  input: (input: InputCommand) => viewerSend({ type: 'input', ...input }),
  pick: (x: number, y: number) => viewerSend({ type: 'pick', commandId: crypto.randomUUID(), x, y }),
  connected: () => !!socket && socket.readyState === WebSocket.OPEN,
};
