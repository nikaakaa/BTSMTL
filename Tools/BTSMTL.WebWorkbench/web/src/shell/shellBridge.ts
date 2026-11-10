export interface SharedBufferEvent extends Event {
  additionalData: { type?: string; id?: string; bytes?: number; timing?: boolean };
  getBuffer(): ArrayBuffer;
}

interface ShellBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  addEventListener(type: 'sharedbufferreceived', listener: (event: SharedBufferEvent) => void): void;
  removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  releaseBuffer(buffer: ArrayBuffer): void;
}

export const shellBridge = (window as Window & { chrome?: { webview?: ShellBridge } }).chrome?.webview ?? null;
