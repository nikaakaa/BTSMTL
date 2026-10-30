/// <reference lib="webworker" />
import { ApiError, isAbort } from '../api/http';
import { playerApi } from '../api/player';
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

self.onmessage = (message: MessageEvent<WorkerRequest>) => {
  const request = message.data;
  switch (request.type) {
    case 'frames':
      void run(request.id, 'Player 档案帧', async signal => {
        const page = await playerApi.frames(request.jobId, request.offset, request.limit, signal);
        post({ type: 'frames', id: request.id, page });
      });
      break;
    case 'observations':
      void run(request.id, 'Player 档案查询', async signal => {
        const page = await playerApi.observations(request.jobId, request.range, signal);
        post({ type: 'observations', id: request.id, page });
      });
      break;
    // 长 JSON 文本格式化后按行返回，供虚拟列表显示；不是合法 JSON 时按原文分行。
    case 'format': {
      let lines: string[];
      try {
        lines = JSON.stringify(JSON.parse(request.text), null, 2).split('\n');
      } catch {
        lines = request.text.split(/\r?\n/);
      }
      post({ type: 'format', id: request.id, lines });
      break;
    }
    case 'cancel':
      reading.get(request.id)?.abort();
      reading.delete(request.id);
      break;
  }
};
