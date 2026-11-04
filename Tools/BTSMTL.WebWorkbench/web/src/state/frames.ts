import { isAbort } from '../api/http';
import type { PlayerJobId, PlayerSummary } from '../api/playerRecords';
import type { Frame } from '../api/types';
import { queryService } from './query';
import { consoleActions, getState, isCurrent, runActions, useWorkbench } from './store';
import { transport } from './transport';

export const frameCacheLimit = 512;
export const framePageSize = 64;

interface FrameScope {
  runId: PlayerJobId;
  generation: number;
  summary: PlayerSummary;
  key: string;
}

const cache = new Map<string, Frame>();
const pending = new Map<string, { controller: AbortController; request: Promise<void> }>();
const key = (scope: FrameScope, index: number) => `${scope.key}:${index}`;
const pageKey = (scope: FrameScope, page: number) => `${scope.generation}:${key(scope, page * framePageSize)}`;

function frameScope(state = getState()): FrameScope | null {
  const { runId, generation, summary } = state;
  if (runId === null || summary === null) return null;
  return { runId, generation, summary, key: JSON.stringify([runId, summary.contentRevision, summary.sceneRevision,
    summary.execution.kind, summary.execution.identity, summary.execution.playerBuildId, summary.execution.editorAssemblyMvid]) };
}

const currentScope = (scope: FrameScope) => isCurrent(scope.generation) && frameScope()?.key === scope.key;

function remember(scope: FrameScope, index: number, frame: Frame) {
  const id = key(scope, index);
  cache.delete(id);
  cache.set(id, frame);
  while (cache.size > frameCacheLimit) cache.delete(cache.keys().next().value!);
}

export function cachedFrame(runId: string, index: number): Frame | undefined {
  const scope = frameScope();
  if (scope === null || scope.runId !== runId) return undefined;
  const id = key(scope, index);
  const frame = cache.get(id);
  if (frame) {
    cache.delete(id);
    cache.set(id, frame);
  }
  return frame;
}

function publishRoutes(scope: FrameScope) {
  const prefix = `${scope.key}:`;
  const routeFrames = [...cache.entries()].filter(([id]) => id.startsWith(prefix))
    .sort(([left], [right]) => Number(left.slice(prefix.length)) - Number(right.slice(prefix.length)))
    .flatMap(([, frame]) => frame.route === null ? [] : [frame.route]);
  runActions.setData(scope.generation, { routeFrames });
}

function loadPage(scope: FrameScope, page: number, signal: AbortSignal): Promise<void> {
  return queryService.framesPage(scope.runId, page * framePageSize, framePageSize, signal).then(result => {
    if (!result || !currentScope(scope)) return;
    const expected = scope.summary;
    for (const frame of result.items) {
      const actual = frame.source;
      if (actual === null || actual.contentRevision !== expected.contentRevision || actual.sceneRevision !== expected.sceneRevision ||
        actual.execution.kind !== expected.execution.kind || actual.execution.identity !== expected.execution.identity ||
        actual.execution.playerBuildId !== expected.execution.playerBuildId || actual.execution.editorAssemblyMvid !== expected.execution.editorAssemblyMvid)
        throw new Error(`档案帧 ${frame.hostFrame} 的内容或执行来源与本次读取修订不一致，未发布旧帧。`);
    }
    result.items.forEach((frame, offset) => remember(scope, page * framePageSize + offset, frame));
    transport.recordFrames(scope.runId, page * framePageSize, result.items);
    publishRoutes(scope);
  });
}

function loadCursorPage(scope: FrameScope, page: number): Promise<void> {
  const id = pageKey(scope, page);
  const existing = pending.get(id);
  if (existing) return existing.request;
  const controller = new AbortController();
  const request = loadPage(scope, page, controller.signal).finally(() => {
    if (pending.get(id)?.request === request) pending.delete(id);
  });
  pending.set(id, { controller, request });
  return request;
}

export async function readFrame(index: number, scope: { kind: 'cursor' } | { kind: 'pinned'; signal: AbortSignal }): Promise<Frame | null> {
  const context = frameScope();
  if (context === null) return null;
  const { runId, generation, summary } = context;
  const page = Math.floor(index / framePageSize);
  if (scope.kind === 'cursor') {
    const allowed = new Set([page - 1, page, page + 1].map(value => pageKey(context, value)));
    for (const [id, entry] of pending) if (!allowed.has(id)) {
      entry.controller.abort();
      pending.delete(id);
    }
  }
  const hit = cachedFrame(runId, index);
  if (hit) {
    transport.recordFrames(runId, index, [hit]);
  }
  else if (scope.kind === 'cursor') await loadCursorPage(context, page);
  else await loadPage(context, page, scope.signal);
  if (!currentScope(context)) return null;
  if (scope.kind === 'cursor') {
    if (getState().frameIndex !== index) return null;
    const count = summary.frameCount;
    for (const neighbour of [page - 1, page + 1])
      if (neighbour >= 0 && neighbour * framePageSize < count && !cache.has(key(context, neighbour * framePageSize)))
        void loadCursorPage(context, neighbour).catch(error => {
          if (!isAbort(error) && isCurrent(generation)) consoleActions.error('archive', error);
        });
  }
  return hit ?? cachedFrame(runId, index) ?? null;
}

useWorkbench.subscribe((state, previous) => {
  if (state.generation === previous.generation && frameScope(state)?.key === frameScope(previous)?.key) return;
  for (const entry of pending.values()) entry.controller.abort();
  pending.clear();
  cache.clear();
  runActions.setData(state.generation, { routeFrames: [], frame: null, frameKey: null });
});
