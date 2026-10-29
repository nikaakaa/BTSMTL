/// <reference lib="webworker" />
import { ApiError, getJson, isAbort } from '../api/http';
import { readServerEvents } from '../api/stream';
import type { ObservationStreamEvent } from '../api/types';
import type { WorkerError, WorkerRequest, WorkerResponse } from './protocol';

declare const self: DedicatedWorkerGlobalScope;

const reading = new Map<number, AbortController>();
const post = (message: WorkerResponse) => self.postMessage(message);

function failure(path: string, error: unknown): WorkerError {
  if (error instanceof ApiError) return { path, status: error.status, reason: error.reason, raw: error.raw };
  return { path, status: 0, reason: error instanceof Error ? error.message : String(error), raw: '' };
}

async function run(id: number, path: string, work: (signal: AbortSignal) => Promise<void>) {
  const controller = new AbortController();
  reading.set(id, controller);
  try {
    await work(controller.signal);
  } catch (error) {
    if (!isAbort(error) && !controller.signal.aborted) post({ type: 'error', id, error: failure(path, error) });
  } finally {
    reading.delete(id);
  }
}

// 帧事件按动画帧节奏合批发给主线程，避免每帧一次消息。
async function stream(id: number, path: string, lastEventId: string | null, signal: AbortSignal) {
  let batch: ObservationStreamEvent[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;
  const flush = () => {
    timer = null;
    if (!batch.length || signal.aborted) return;
    post({ type: 'events', id, events: batch });
    batch = [];
  };
  await readServerEvents(path, signal, lastEventId, event => {
    if (event.event === 'frame')
      batch.push({ event: 'frame', id: event.id ?? '', data: JSON.parse(event.data) });
    else if (event.event === 'end')
      batch.push({ event: 'end', data: JSON.parse(event.data) });
    else return;
    if (event.event === 'end') flush();
    else timer ??= setTimeout(flush, 16);
  });
  if (timer) clearTimeout(timer);
  flush();
  if (!signal.aborted) post({ type: 'done', id });
}

self.onmessage = (message: MessageEvent<WorkerRequest>) => {
  const request = message.data;
  switch (request.type) {
    case 'fetch':
      void run(request.id, request.path, async signal => {
        const value = await getJson<unknown>(request.path, signal);
        post({ type: 'result', id: request.id, value });
      });
      break;
    case 'stream':
      void run(request.id, request.path, signal => stream(request.id, request.path, request.lastEventId, signal));
      break;
    case 'cancel':
      reading.get(request.id)?.abort();
      reading.delete(request.id);
      break;
  }
};
