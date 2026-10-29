import { ApiError, query } from '../api/http';
import { previewApi } from '../api/preview';
import type { Comparison, Frame, ObservationPage, ObservationQuery, ObservationStreamEvent } from '../api/types';
import type { WorkerRequest, WorkerResponse } from '../workers/protocol';
import QueryWorker from '../workers/query.worker?worker';
import { consoleActions, getState, useWorkbench } from './store';

// 查询服务：后台线程负责大 JSON 解析与观察流读取，结果按运行代次交给面板。
// 切换运行或关闭记录查询面板时取消；面板只是隐藏时保持。

const worker = new QueryWorker();
let nextId = 1;

interface Pending {
  generation: number;
  resolve(value: unknown): void;
  reject(error: ApiError): void;
  events?(events: ObservationStreamEvent[]): void;
  done?(): void;
}
const pending = new Map<number, Pending>();

worker.onmessage = (message: MessageEvent<WorkerResponse>) => {
  const response = message.data;
  const entry = pending.get(response.id);
  if (!entry) return;
  if (entry.generation !== getState().generation) {
    cancel(response.id);
    return;
  }
  switch (response.type) {
    case 'result':
      pending.delete(response.id);
      entry.resolve(response.value);
      break;
    case 'events':
      entry.events?.(response.events);
      break;
    case 'done':
      pending.delete(response.id);
      entry.done?.();
      break;
    case 'error': {
      pending.delete(response.id);
      const { path, status, reason, raw } = response.error;
      entry.reject(new ApiError(path, status, reason, raw));
      break;
    }
  }
};

function send(request: WorkerRequest) {
  worker.postMessage(request);
}

function cancel(id: number) {
  pending.get(id)?.resolve(null);
  pending.delete(id);
  send({ type: 'cancel', id });
}

// 后台解析一次 GET；运行切换后结果丢弃并返回 null。
function fetchParsed<T>(path: string): { id: number; result: Promise<T | null> } {
  const id = nextId++;
  const generation = getState().generation;
  const result = new Promise<T | null>((resolve, reject) => {
    pending.set(id, { generation, resolve: value => resolve(value as T), reject });
    send({ type: 'fetch', id, path });
  });
  return { id, result };
}

// ── 记录查询：分页读取与跟随帧流 ──

export const streamLimit = 512;

export interface QueryState {
  runId: string | null;
  range: ObservationQuery | null;
  items: Frame[];
  total: number | null;
  nextOffset: number | null;
  contentRevision: string | null;
  reading: boolean;
  streaming: boolean;
  lastEventId: string | null;
  ended: boolean;
}

const idle: QueryState = {
  runId: null, range: null, items: [], total: null, nextOffset: null, contentRevision: null,
  reading: false, streaming: false, lastEventId: null, ended: false,
};

let state: QueryState = idle;
let activeId: number | null = null;
const listeners = new Set<() => void>();

function update(next: Partial<QueryState>) {
  state = { ...state, ...next };
  listeners.forEach(listener => listener());
}

function stopActive() {
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
    const { id, result } = fetchParsed<ObservationPage>(
      `/api/preview/runs/${encodeURIComponent(runId)}/observations${query({ ...range, offset, limit: 64 })}`);
    activeId = id;
    update({ runId, range, reading: true, streaming: false, ended: false, ...(more ? {} : { items: [], total: null }) });
    try {
      const page = await result;
      if (!page || activeId !== id) return;
      update({
        items: more ? [...state.items, ...page.items] : page.items, total: page.total, nextOffset: page.nextOffset,
        contentRevision: page.contentRevision,
      });
    } catch (error) {
      consoleActions.error('query', error);
    } finally {
      if (activeId === id) { activeId = null; update({ reading: false }); }
    }
  },

  // 跟随范围内的帧流：只保留最近 512 帧；断开后可按最后事件身份续读。
  follow(range: ObservationQuery, resume: boolean) {
    const runId = getState().runId;
    if (!runId) return;
    stopActive();
    const id = nextId++;
    activeId = id;
    const lastEventId = resume ? state.lastEventId : null;
    update({ runId, range, streaming: true, reading: false, ended: false, ...(resume ? {} : { items: [], total: null, lastEventId: null }) });
    pending.set(id, {
      generation: getState().generation,
      resolve: () => undefined,
      reject: error => {
        if (activeId === id) { activeId = null; update({ streaming: false }); }
        consoleActions.error('query', error);
      },
      events: events => {
        let items = state.items;
        let { lastEventId: eventId, contentRevision, ended } = state;
        for (const event of events) {
          if (event.event === 'frame') {
            items = [...items, event.data.observation];
            eventId = event.id;
            contentRevision = event.data.contentRevision;
          } else ended = true;
        }
        if (items.length > streamLimit) items = items.slice(items.length - streamLimit);
        update({ items, lastEventId: eventId, contentRevision, ended });
      },
      done: () => {
        if (activeId === id) { activeId = null; update({ streaming: false }); }
      },
    });
    send({ type: 'stream', id, path: previewApi.observationStreamPath(runId, range), lastEventId });
  },

  stop() {
    stopActive();
    update({ reading: false, streaming: false });
  },

  // 记录查询面板关闭时调用。
  close() {
    stopActive();
    update(idle);
  },

  // 运行比较结果在后台解析。
  compare(left: string, right: string): Promise<Comparison | null> {
    return fetchParsed<Comparison>(`/api/preview/runs/${encodeURIComponent(left)}/compare/${encodeURIComponent(right)}`).result;
  },

  // 长 JSON 在后台格式化为行。
  formatJson(text: string): Promise<string[] | null> {
    const id = nextId++;
    const generation = getState().generation;
    return new Promise((resolve, reject) => {
      pending.set(id, { generation, resolve: value => resolve(value as string[]), reject });
      send({ type: 'format', id, text });
    });
  },

  // 帧记录大页在后台解析。
  framesPage(runId: string, offset: number, limit: number) {
    return fetchParsed<{ items: Frame[]; total: number; nextOffset: number | null }>(
      `/api/preview/runs/${encodeURIComponent(runId)}/frames?offset=${offset}&limit=${limit}`).result;
  },
};

// 切换运行：取消旧读取，清空结果。
useWorkbench.subscribe((next, previous) => {
  if (next.runId !== previous.runId) {
    stopActive();
    for (const id of [...pending.keys()]) cancel(id);
    update(idle);
  }
});
