import { isAbort } from '../api/http';
import type { PlayerJobId } from '../api/playerRecords';
import type { Frame } from '../api/types';
import { queryService } from './query';
import { consoleActions, getState, isCurrent, runActions } from './store';

export const frameCacheLimit = 512;
export const framePageSize = 64;

// 帧记录 LRU：键为"运行身份 + 帧序号"，Map 插入顺序即最近使用顺序。
const cache = new Map<string, Frame>();
const pending = new Map<string, { controller: AbortController; request: Promise<void> }>();
const key = (runId: string, index: number) => `${runId}:${index}`;
const pageKey = (runId: string, page: number, generation: number) => `${generation}:${key(runId, page * framePageSize)}`;

function remember(runId: string, index: number, frame: Frame) {
  const id = key(runId, index);
  cache.delete(id);
  cache.set(id, frame);
  while (cache.size > frameCacheLimit) cache.delete(cache.keys().next().value!);
}

export function cachedFrame(runId: string, index: number): Frame | undefined {
  const id = key(runId, index);
  const frame = cache.get(id);
  if (frame) {
    cache.delete(id);
    cache.set(id, frame);
  }
  return frame;
}

function publishRoutes(runId: PlayerJobId, generation: number) {
  const routeFrames = [...cache.entries()].flatMap(([id, frame]) =>
    id.startsWith(`${runId}:`) && frame.route !== null ? [frame.route] : []);
  routeFrames.sort((left, right) => BigInt(left.hostFrame) < BigInt(right.hostFrame) ? -1
    : BigInt(left.hostFrame) > BigInt(right.hostFrame) ? 1 : 0);
  runActions.setData(generation, { routeFrames });
}

function loadPage(runId: PlayerJobId, page: number, generation: number): Promise<void> {
  const id = pageKey(runId, page, generation);
  const existing = pending.get(id);
  if (existing) return existing.request;
  const controller = new AbortController();
  const request = queryService.framesPage(runId, page * framePageSize, framePageSize, controller.signal).then(result => {
    if (!result || !isCurrent(generation)) return;
    result.items.forEach((frame, offset) => remember(runId, page * framePageSize + offset, frame));
    publishRoutes(runId, generation);
  }).finally(() => {
    if (pending.get(id)?.request === request) pending.delete(id);
  });
  pending.set(id, { controller, request });
  return request;
}

// 读取当前运行的一帧；同时预取前后各一页。运行已切换时返回 null。
export async function readFrame(index: number): Promise<Frame | null> {
  const { runId, generation, summary } = getState();
  if (!runId) return null;
  const page = Math.floor(index / framePageSize);
  const allowed = new Set([page - 1, page, page + 1].map(value => pageKey(runId, value, generation)));
  for (const [id, entry] of pending) if (!allowed.has(id)) {
    entry.controller.abort();
    pending.delete(id);
  }
  const hit = cachedFrame(runId, index);
  if (hit) publishRoutes(runId, generation);
  else await loadPage(runId, page, generation);
  if (!isCurrent(generation)) return null;
  const count = summary?.frameCount ?? 0;
  for (const neighbour of [page - 1, page + 1])
    if (neighbour >= 0 && neighbour * framePageSize < count && !cache.has(key(runId, neighbour * framePageSize)))
      void loadPage(runId, neighbour, generation).catch(error => {
        if (!isAbort(error) && isCurrent(generation)) consoleActions.error('archive', error);
      });
  return hit ?? cachedFrame(runId, index) ?? null;
}
