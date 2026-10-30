import { isAbort } from '../api/http';
import { playerApi } from '../api/player';
import type { PlayerJobId } from '../api/playerRecords';
import type { Frame } from '../api/types';
import { consoleActions, getState, isCurrent, useWorkbench } from './store';

export const frameCacheLimit = 512;
export const framePageSize = 64;

// 帧记录 LRU：键为"运行身份 + 帧序号"，Map 插入顺序即最近使用顺序。
const cache = new Map<string, Frame>();
const pending = new Map<string, Promise<void>>();
const controllers = new Map<string, AbortController>();
const key = (runId: string, index: number) => `${runId}:${index}`;

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

function loadPage(runId: PlayerJobId, page: number, generation: number): Promise<void> {
  const id = `${generation}:${key(runId, page * framePageSize)}`;
  let request = pending.get(id);
  if (!request) {
    const controller = new AbortController();
    controllers.set(id, controller);
    request = playerApi.frames(runId, page * framePageSize, framePageSize, controller.signal).then(result => {
      if (!isCurrent(generation)) return;
      result.items.forEach((frame, offset) => remember(runId, page * framePageSize + offset, frame));
    }).finally(() => { pending.delete(id); controllers.delete(id); });
    pending.set(id, request);
  }
  return request;
}

// 读取当前运行的一帧；同时预取前后各一页。运行已切换时返回 null。
export async function readFrame(index: number): Promise<Frame | null> {
  const { runId, generation, summary } = getState();
  if (!runId) return null;
  const page = Math.floor(index / framePageSize);
  const hit = cachedFrame(runId, index);
  if (!hit) await loadPage(runId, page, generation);
  if (!isCurrent(generation)) return null;
  const count = summary?.frameCount ?? 0;
  for (const neighbour of [page - 1, page + 1])
    if (neighbour >= 0 && neighbour * framePageSize < count && !cache.has(key(runId, neighbour * framePageSize)))
      void loadPage(runId, neighbour, generation).catch(error => {
        if (!isAbort(error) && isCurrent(generation)) consoleActions.error('archive', error);
      });
  return hit ?? cachedFrame(runId, index) ?? null;
}

useWorkbench.subscribe((next, previous) => {
  if (next.generation === previous.generation) return;
  for (const controller of controllers.values()) controller.abort();
});
