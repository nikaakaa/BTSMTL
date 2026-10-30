import { ApiError } from '../api/http';
import type { PlayerFramesPage, PlayerJobId, PlayerObservationPage } from '../api/playerRecords';
import type { Frame, ObservationQuery } from '../api/types';
import type { WorkerRequest, WorkerResponse } from '../workers/protocol';
import QueryWorker from '../workers/query.worker?worker';
import { consoleActions, getState, useWorkbench } from './store';

const worker = new QueryWorker();
let nextId = 1;

interface Pending<T> {
  generation: number;
  resolve(value: T | null): void;
  reject(error: ApiError): void;
}
const pending = {
  frames: new Map<number, Pending<PlayerFramesPage>>(),
  observations: new Map<number, Pending<PlayerObservationPage>>(),
  format: new Map<number, Pending<string[]>>(),
};

function complete<T>(queue: Map<number, Pending<T>>, id: number, value: T) {
  const entry = queue.get(id);
  if (!entry) return;
  queue.delete(id);
  entry.resolve(entry.generation === getState().generation ? value : null);
}

worker.onmessage = (message: MessageEvent<WorkerResponse>) => {
  const response = message.data;
  switch (response.type) {
    case 'frames': complete(pending.frames, response.id, response.page); break;
    case 'observations': complete(pending.observations, response.id, response.page); break;
    case 'format': complete(pending.format, response.id, response.lines); break;
    case 'error': {
      const { path, status, reason, raw } = response.error;
      for (const queue of Object.values(pending)) {
        const entry = queue.get(response.id);
        if (entry) { queue.delete(response.id); entry.reject(new ApiError(path, status, reason, raw)); }
      }
      break;
    }
  }
};

function send(request: WorkerRequest) {
  worker.postMessage(request);
}

function cancel(id: number) {
  for (const queue of Object.values(pending)) {
    queue.get(id)?.resolve(null);
    queue.delete(id);
  }
  send({ type: 'cancel', id });
}

function enqueue<T>(queue: Map<number, Pending<T>>, request: WorkerRequest): Promise<T | null> {
  const generation = getState().generation;
  return new Promise<T | null>((resolve, reject) => {
    queue.set(request.id, { generation, resolve, reject });
    send(request);
  });
}

export const queryLimit = 512;

export interface QueryState {
  runId: PlayerJobId | null;
  range: ObservationQuery | null;
  items: Frame[];
  total: number | null;
  nextOffset: number | null;
  contentRevision: string | null;
  reading: boolean;
  readingAll: boolean;
  ended: boolean;
}

const idle: QueryState = {
  runId: null, range: null, items: [], total: null, nextOffset: null, contentRevision: null,
  reading: false, readingAll: false, ended: false,
};

let state: QueryState = idle;
let activeId: number | null = null;
let requestVersion = 0;
const listeners = new Set<() => void>();

function update(next: Partial<QueryState>) {
  state = { ...state, ...next };
  listeners.forEach(listener => listener());
}

function stopActive() {
  requestVersion++;
  if (activeId !== null) cancel(activeId);
  activeId = null;
}

export const queryService = {
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  snapshot: () => state,

  async read(range: ObservationQuery, more: boolean) {
    const runId = getState().runId;
    if (!runId) return;
    stopActive();
    const offset = more ? state.nextOffset ?? 0 : 0;
    const id = nextId++;
    const result = observations(id, runId, range, offset);
    activeId = id;
    update({ runId, range, reading: true, readingAll: false, ended: false, ...(more ? {} : { items: [], total: null, nextOffset: null }) });
    try {
      const page = await result;
      if (!page || activeId !== id) return;
      update({
        items: more ? [...state.items, ...page.items] : page.items, total: page.total, nextOffset: page.nextOffset,
        contentRevision: page.contentRevision, ended: page.nextOffset === null,
      });
    } catch (error) {
      consoleActions.error('query', error);
    } finally {
      if (activeId === id) { activeId = null; update({ reading: false }); }
    }
  },

  async readAll(range: ObservationQuery, resume: boolean) {
    const runId = getState().runId;
    if (!runId) return;
    stopActive();
    const version = requestVersion;
    let offset: number | null = resume ? state.nextOffset : 0;
    update({ runId, range, readingAll: true, reading: false, ended: false, ...(resume ? {} : { items: [], total: null, nextOffset: null }) });
    try {
      while (offset !== null && version === requestVersion) {
        const id = nextId++;
        activeId = id;
        const page = await observations(id, runId, range, offset);
        if (!page || version !== requestVersion) return;
        offset = page.nextOffset;
        update({ items: [...state.items, ...page.items].slice(-queryLimit), total: page.total,
          nextOffset: page.nextOffset, contentRevision: page.contentRevision, ended: offset === null });
      }
    } catch (error) {
      consoleActions.error('query', error);
    } finally {
      if (version === requestVersion) { activeId = null; update({ readingAll: false }); }
    }
  },

  stop() {
    stopActive();
    update({ reading: false, readingAll: false });
  },

  // 记录查询面板关闭时调用。
  close() {
    stopActive();
    update(idle);
  },

  // 长 JSON 在后台格式化为行。
  formatJson(text: string): Promise<string[] | null> {
    const id = nextId++;
    return enqueue(pending.format, { type: 'format', id, text });
  },

  // 帧记录大页在后台解析。
  framesPage(jobId: PlayerJobId, offset: number, limit: number, signal: AbortSignal) {
    signal.throwIfAborted();
    const id = nextId++;
    const request = enqueue(pending.frames, { type: 'frames', id, jobId, offset, limit });
    const abort = () => cancel(id);
    signal.addEventListener('abort', abort, { once: true });
    return request.finally(() => signal.removeEventListener('abort', abort));
  },
};

// 切换运行：取消旧读取，清空结果。
useWorkbench.subscribe((next, previous) => {
  if (next.generation !== previous.generation) {
    stopActive();
    for (const queue of Object.values(pending)) for (const id of [...queue.keys()]) cancel(id);
    update(idle);
  }
});

function observations(id: number, jobId: PlayerJobId, range: ObservationQuery, offset: number) {
  return enqueue(pending.observations, { type: 'observations', id, jobId, range: {
    firstSdkFrame: Number(range.firstFrame), lastSdkFrame: Number(range.lastFrame), offset, limit: 64,
  } });
}
