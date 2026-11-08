// 桌面壳的共享画面区：页面在壳的 WebView2 里时，按播放器画面尺寸向壳申请共享内存，壳只读交给本页，
// 并把映射句柄复制进播放器进程。播放器每帧把读回的 RGBA 写进帧槽，本页按 Host 推来的采样序号取对应帧槽。
// 布局与播放器 BTSMTL.Preview.PreviewSharedSurface 相同：64 字节头 + 3 个帧槽；帧槽 = 256 字节槽头 + 宽×高×4，
// 槽头 0 为槽序号，8／16 为采集与写完时刻（QueryPerformanceCounter）。壳以计时方式启动时，本页画完一帧把两个时刻回报给壳。

interface SharedBufferEvent extends Event {
  additionalData: { type?: string; id?: string; bytes?: number; timing?: boolean };
  getBuffer(): ArrayBuffer;
}

interface ShellBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  addEventListener(type: 'sharedbufferreceived', listener: (event: SharedBufferEvent) => void): void;
  releaseBuffer(buffer: ArrayBuffer): void;
}

const bridge = (window as Window & { chrome?: { webview?: ShellBridge } }).chrome?.webview ?? null;
export const sharedSurfaceAvailable = bridge !== null;

const HeaderBytes = 64, SlotCount = 3, SlotHeaderBytes = 256;
export const surfaceBytes = (width: number, height: number) => HeaderBytes + SlotCount * (SlotHeaderBytes + width * height * 4);

const buffers = new Map<string, ArrayBuffer>();
let timing = false;
const bufferWaiters = new Map<string, { bytes: number; resolve: (buffer: ArrayBuffer) => void; reject: (error: Error) => void }[]>();
const shareWaiters = new Map<string, { resolve: (handle: number) => void; reject: (error: Error) => void }[]>();

function settle<T>(waiters: Map<string, T[]>, id: string, each: (waiter: T) => void) {
  const pending = waiters.get(id) ?? [];
  waiters.delete(id);
  pending.forEach(each);
}

bridge?.addEventListener('sharedbufferreceived', event => {
  const { type, id, bytes } = event.additionalData ?? {};
  if (type !== 'surface' || id === undefined) return;
  timing = event.additionalData.timing === true;
  const buffer = event.getBuffer();
  const previous = buffers.get(id);
  if (previous !== undefined && previous !== buffer) bridge.releaseBuffer(previous);
  buffers.set(id, buffer);
  settle(bufferWaiters, id, waiter => waiter.bytes === bytes ? waiter.resolve(buffer)
    : waiter.reject(new Error(`画面区 '${id}' 收到的大小 ${bytes} 与申请的 ${waiter.bytes} 不同`)));
});

bridge?.addEventListener('message', event => {
  const message = event.data as { type?: string; id?: string; handle?: number; error?: string };
  if (message.id === undefined) return;
  if (message.type === 'surface' && message.error !== undefined)
    settle(bufferWaiters, message.id, waiter => waiter.reject(new Error(message.error)));
  if (message.type === 'surface-share')
    settle(shareWaiters, message.id, waiter => message.error !== undefined ? waiter.reject(new Error(message.error))
      : waiter.resolve(message.handle!));
});

function requireBuffer(id: string, bytes: number): Promise<ArrayBuffer> {
  const known = buffers.get(id);
  if (known !== undefined && known.byteLength === bytes) return Promise.resolve(known);
  return new Promise((resolve, reject) => {
    bufferWaiters.set(id, [...(bufferWaiters.get(id) ?? []), { bytes, resolve, reject }]);
    bridge!.postMessage({ type: 'surface', id, bytes });
  });
}

// 建好（或沿用同尺寸的）画面区，并把句柄复制进播放器进程；返回该进程内的句柄值与画面区大小，交给播放器的 surface 命令。
export async function shareSurface(id: string, width: number, height: number, processId: number) {
  const bytes = surfaceBytes(width, height);
  await requireBuffer(id, bytes);
  const handle = await new Promise<number>((resolve, reject) => {
    shareWaiters.set(id, [...(shareWaiters.get(id) ?? []), { resolve, reject }]);
    bridge!.postMessage({ type: 'surface-share', id, processId });
  });
  return { handle, bytes };
}

export function releaseSurface(id: string) {
  const buffer = buffers.get(id);
  if (buffer === undefined) return;
  buffers.delete(id);
  bridge!.releaseBuffer(buffer);
  bridge!.postMessage({ type: 'surface-release', id });
}

export interface SurfaceFrame {
  width: number;
  height: number;
  pixels: Uint8ClampedArray<ArrayBuffer>;
  // 读完像素后调用：帧槽仍是该序号才说明没被播放器覆盖。
  intact(): boolean;
  // 画到屏上后调用；壳计时启动时回报采集与写完时刻。
  drawn(): void;
}

// 取采样序号为 sequence 的帧槽；已被覆盖或尚未写入时返回 null。
export function surfaceFrame(id: string, sequence: string): SurfaceFrame | null {
  const buffer = buffers.get(id);
  if (buffer === undefined) return null;
  const header = new DataView(buffer, 0, HeaderBytes);
  const width = header.getInt32(12, true), height = header.getInt32(16, true);
  if (width <= 0 || height <= 0 || surfaceBytes(width, height) !== buffer.byteLength) return null;
  const wanted = BigInt(sequence), stride = SlotHeaderBytes + width * height * 4;
  for (let slot = 0; slot < SlotCount; slot++) {
    const offset = HeaderBytes + slot * stride;
    const slotHeader = new DataView(buffer, offset, 24);
    if (slotHeader.getBigUint64(0, true) !== wanted) continue;
    const captured = slotHeader.getBigInt64(8, true), written = slotHeader.getBigInt64(16, true);
    return {
      width, height,
      pixels: new Uint8ClampedArray(buffer, offset + SlotHeaderBytes, width * height * 4),
      intact: () => slotHeader.getBigUint64(0, true) === wanted,
      drawn: () => {
        if (timing) bridge!.postMessage({ type: 'surface-drawn', id, sequence, captured: captured.toString(), written: written.toString() });
      },
    };
  }
  return null;
}
