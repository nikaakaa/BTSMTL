import { getBlob, getJson, postJson, query } from './http';
import {
  readPlayerCamera, readPlayerJob, readPlayerJobReceipt, readPlayerJobs, readPlayerLogic, readPlayerPoseLayout,
  readPlayerPresentation, readPlayerSummary, readPlayerUnavailable,
  type PlayerArchiveRange, type PlayerJobId, type StartPlayerJobRequest,
} from './playerRecords';
import type {
  ImageSourceAvailability, LiveCommand, LiveCommandResult, LivePlayer, PlayerBuild,
  PlayerCodeUpdate, StartLivePlayerRequest, VideoFrame,
  ViewerMessage,
} from './types';

const live = (id: string) => `/api/preview/live/${encodeURIComponent(id)}`;
const job = (id: PlayerJobId) => `/api/preview/player-jobs/${encodeURIComponent(id)}`;
const decoded = <T>(path: string, read: (value: unknown, path?: string) => T, signal?: AbortSignal) =>
  getJson<unknown>(path, signal).then(value => read(value, path));

export const playerApi = {
  images: (signal?: AbortSignal) => getJson<ImageSourceAvailability>('/api/preview/images', signal),
  builds: (signal?: AbortSignal) => getJson<{ items: PlayerBuild[] }>('/api/preview/players', signal),
  jobs: (signal?: AbortSignal) => decoded('/api/preview/player-jobs', readPlayerJobs, signal),
  startJob: (request: StartPlayerJobRequest, signal?: AbortSignal) =>
    postJson<unknown>('/api/preview/player-jobs', request, signal).then(value => readPlayerJobReceipt(value)),
  job: (id: PlayerJobId, signal?: AbortSignal) => decoded(job(id), readPlayerJob, signal),
  summary: (id: PlayerJobId, signal?: AbortSignal) => decoded(`${job(id)}/summary`, readPlayerSummary, signal),
  archive: (id: PlayerJobId, signal?: AbortSignal) => getJson<unknown>(`${job(id)}/archive`, signal),
  archiveFile: (id: PlayerJobId, path: string, signal?: AbortSignal) =>
    getBlob(`${job(id)}/files/${path.split('/').map(encodeURIComponent).join('/')}`, signal),
  logic: (id: PlayerJobId, range: PlayerArchiveRange, signal?: AbortSignal) =>
    decoded(`${job(id)}/logic${query({ ...range })}`, readPlayerLogic, signal),
  poseLayout: (id: PlayerJobId, signal?: AbortSignal) =>
    decoded(`${job(id)}/pose-layout`, readPlayerPoseLayout, signal),
  presentation: (id: PlayerJobId, range: PlayerArchiveRange & { rosterIndex?: number }, signal?: AbortSignal) =>
    decoded(`${job(id)}/presentation${query({ ...range })}`, readPlayerPresentation, signal),
  camera: (id: PlayerJobId, range: PlayerArchiveRange, signal?: AbortSignal) =>
    decoded(`${job(id)}/camera${query({ ...range })}`, readPlayerCamera, signal),
  unavailable: (id: PlayerJobId, range: PlayerArchiveRange, signal?: AbortSignal) =>
    decoded(`${job(id)}/unavailable${query({ ...range })}`, readPlayerUnavailable, signal),
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
