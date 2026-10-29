import { getBlob, getJson, postJson } from './http';
import type {
  ImageMetadata, ImageSourceAvailability, LiveCommand, LiveCommandResult, LivePlayer, PlayerBuild,
  PlayerCodeUpdate, PlayerJob, PlayerJobReceipt, StartLivePlayerRequest, StartPlayerJobRequest, VideoFrame,
  ViewerMessage,
} from './types';

const live = (id: string) => `/api/preview/live/${encodeURIComponent(id)}`;
const job = (runId: string, jobId: string) =>
  `/api/preview/runs/${encodeURIComponent(runId)}/player/${encodeURIComponent(jobId)}`;

export const playerApi = {
  images: (signal?: AbortSignal) => getJson<ImageSourceAvailability>('/api/preview/images', signal),
  builds: (signal?: AbortSignal) => getJson<{ items: PlayerBuild[] }>('/api/preview/players', signal),
  frameImage: (runId: string, sourceId: string, hostFrame: string, signal?: AbortSignal) =>
    getBlob(`${job(runId, sourceId)}/frames/${encodeURIComponent(hostFrame)}`, signal).then(response => response.blob()),
  frameMetadata: (runId: string, sourceId: string, hostFrame: string, signal?: AbortSignal) =>
    getJson<ImageMetadata>(`${job(runId, sourceId)}/frames/${encodeURIComponent(hostFrame)}/metadata`, signal),

  startJob: (request: StartPlayerJobRequest, signal?: AbortSignal) =>
    postJson<PlayerJobReceipt>(`/api/preview/runs/${encodeURIComponent(request.runId)}/player`, request, signal),
  job: (runId: string, jobId: string, signal?: AbortSignal) => getJson<PlayerJob>(job(runId, jobId), signal),
  updateCode: (requestId: string, playerBuildId: string, signal?: AbortSignal) =>
    postJson<PlayerCodeUpdate>('/api/preview/players/code-updates', { requestId, playerBuildId }, signal),

  lives: (signal?: AbortSignal) => getJson<LivePlayer[]>('/api/preview/live', signal),
  live: (id: string, signal?: AbortSignal) => getJson<LivePlayer>(live(id), signal),
  start: (request: StartLivePlayerRequest, signal?: AbortSignal) =>
    postJson<LivePlayer>('/api/preview/live', request, signal),
  stop: (id: string, signal?: AbortSignal) => postJson<LivePlayer>(`${live(id)}/stop`, {}, signal),
  swap: (id: string, playerBuildId: string, signal?: AbortSignal) =>
    postJson<LivePlayer>(`${live(id)}/swap`, { playerBuildId }, signal),
  command: (id: string, command: LiveCommand, signal?: AbortSignal) =>
    postJson<LiveCommandResult>(`${live(id)}/commands`, command, signal),
  readCommand: (id: string, commandId: string, signal?: AbortSignal) =>
    getJson<LiveCommandResult>(`${live(id)}/commands/${encodeURIComponent(commandId)}`, signal),
  capturePath: (id: string, commandId: string) => `${live(id)}/captures/${commandId.replaceAll('-', '')}.png`,
  latestImage: async (id: string, signal?: AbortSignal) => {
    const response = await getBlob(`${live(id)}/frame.jpg`, signal);
    return { hostFrame: response.headers.get('X-Q3-Frame') ?? '', image: await response.blob() };
  },
};

export interface ViewerHandlers {
  open(): void;
  message(message: ViewerMessage): void;
  video(frame: VideoFrame): void;
  close(event: CloseEvent): void;
}

// 画面连接：文本为状态、对照、回执；二进制为 [4 字节元数据长度][元数据 JSON][图像]。
export function openViewer(liveId: string, handlers: ViewerHandlers): WebSocket {
  const protocol = location.protocol === 'https:' ? 'wss' : 'ws';
  const socket = new WebSocket(`${protocol}://${location.host}${live(liveId)}/view`);
  socket.binaryType = 'arraybuffer';
  socket.onopen = () => handlers.open();
  socket.onclose = event => handlers.close(event);
  socket.onmessage = event => {
    if (typeof event.data === 'string') {
      handlers.message(JSON.parse(event.data) as ViewerMessage);
      return;
    }
    const buffer = event.data as ArrayBuffer;
    const length = new DataView(buffer, 0, 4).getInt32(0, true);
    const metadata = JSON.parse(new TextDecoder().decode(new Uint8Array(buffer, 4, length))) as Omit<VideoFrame, 'image'>;
    handlers.video({ ...metadata, image: new Blob([new Uint8Array(buffer, 4 + length)], { type: metadata.mediaType }) });
  };
  return socket;
}

export function sendViewerCommand(socket: WebSocket, command: LiveCommand): void {
  if (socket.readyState !== WebSocket.OPEN) throw new Error('画面连接未就绪');
  socket.send(JSON.stringify(command));
}
