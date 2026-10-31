import { ApiError } from '../api/http';
import { playerApi } from '../api/player';
import type { PlayerArchive, PlayerArchiveImage, PlayerCurveSample, PlayerFrameImage, PlayerInputPage, PlayerJobId } from '../api/playerRecords';
import type { Availability } from '../api/types';
import { consoleActions, getState, isCurrent, runActions, type ArchiveFrame, type ArchiveImage } from './store';
import { transport, type ArchiveTimebase } from './transport';
import { readFrame } from './frames';

const source = 'archive';
const pageFrames = 64;
const cacheLimit = 192;
const imageCacheLimit = 32;
const cache = new Map<string, ArchiveFrame>();
const pending = new Map<string, Promise<void>>();
const pages = new Map<string, { controller: AbortController; page: number; curveId: string | null }>();
const images = new Map<string, ArchiveImage>();
const key = (jobId: PlayerJobId, index: number) => `${jobId}:${index}`;
interface OpenArchive {
  kind: 'ready';
  jobId: PlayerJobId;
  generation: number;
  metadata: PlayerArchive;
  frameCount: number;
  timebase: ArchiveTimebase;
  controller: AbortController;
}
type OpeningArchive = Pick<OpenArchive, 'jobId' | 'generation' | 'controller'> & { kind: 'opening' };
let current: OpenArchive | OpeningArchive | null = null;
let imageController: AbortController | null = null;
let frameRequest = 0;

function remember(jobId: PlayerJobId, index: number, frame: ArchiveFrame) {
  const id = key(jobId, index);
  cache.delete(id);
  cache.set(id, frame);
  while (cache.size > cacheLimit)
    for (const oldest of cache.keys()) { cache.delete(oldest); break; }
}

function readCachedFrame(jobId: PlayerJobId, index: number) {
  const frame = cache.get(key(jobId, index));
  if (frame !== undefined) remember(jobId, index, frame);
  return frame;
}

function publishFrame(context: OpenArchive) {
  const state = getState();
  if (isCurrent(context.generation) && state.runId === context.jobId) {
    const frame = readCachedFrame(context.jobId, state.frameIndex);
    runActions.setData(context.generation, { archiveFrame: frame === undefined ? null : { ...frame } });
  }
}

async function readAll<T>(read: (offset: number) => Promise<{
  page: { items: T[]; nextOffset: number | null }; availability: Availability[];
}>) {
  let result = await read(0);
  const items = [...result.page.items];
  while (result.page.nextOffset !== null) {
    result = await read(result.page.nextOffset);
    items.push(...result.page.items);
  }
  return { items, availability: result.availability };
}

async function channel<T>(name: string, work: Promise<{ items: T[]; availability: Availability[] }>) {
  try {
    return await work;
  } catch (error) {
    if (!(error instanceof ApiError) || (error.status !== 404 && error.status !== 409)) throw error;
    return { items: [], availability: [{ channel: name, available: false, complete: false, reason: error.reason }] };
  }
}

function loadArchivePage(context: OpenArchive, page: number, curveId: string | null,
  read: (signal: AbortSignal) => Promise<void>): Promise<void> {
  const id = `${context.generation}:${key(context.jobId, page)}${curveId === null ? '' : `:curve:${curveId}`}`;
  const existing = pending.get(id);
  if (existing) return existing;
  const controller = new AbortController();
  pages.set(id, { controller, page, curveId });
  const signal = AbortSignal.any([context.controller.signal, controller.signal]);
  const request = read(signal).finally(() => {
    if (pending.get(id) === request) { pending.delete(id); pages.delete(id); }
  });
  pending.set(id, request);
  return request;
}

function loadPage(context: OpenArchive, page: number): Promise<void> {
  const offset = page * pageFrames;
  return loadArchivePage(context, page, null, async signal => {
    const logic = await playerApi.logic(context.jobId, {
      firstSdkFrame: context.metadata.firstRecordedSdkFrame, lastSdkFrame: context.metadata.lastRecordedSdkFrame,
      offset, limit: pageFrames,
    }, signal);
    signal.throwIfAborted();
    const first = logic.page.items.at(0);
    const last = logic.page.items.at(-1);
    if (!first || !last) return;
    const range = (pageOffset: number) => ({
      firstSdkFrame: Number(first.source.clock.position.hostFrame), lastSdkFrame: Number(last.source.clock.position.hostFrame),
      offset: pageOffset, limit: 128,
    });
    let inputContext: Pick<PlayerInputPage, 'plans' | 'valueEncoding'> | null = null;
    const [presentation, camera, input, unavailable] = await Promise.all([
      channel('presentation', readAll(pageOffset => playerApi.presentation(context.jobId, range(pageOffset), signal))),
      channel('camera', readAll(pageOffset => playerApi.camera(context.jobId, range(pageOffset), signal))),
      channel('input', readAll(async pageOffset => {
        const result = await playerApi.input(context.jobId, range(pageOffset), signal);
        if (pageOffset === 0) inputContext = { plans: result.plans, valueEncoding: result.valueEncoding };
        return result;
      })),
      channel('unavailable', readAll(pageOffset => playerApi.unavailable(context.jobId, range(pageOffset), signal))),
    ]);
    signal.throwIfAborted();
    logic.page.items.forEach((record, index) => {
      remember(context.jobId, offset + index, {
        hostFrame: record.source.clock.position.hostFrame, logic: record,
        presentations: presentation.items.filter(item => item.source.clock.position.hostFrame === record.source.clock.position.hostFrame),
        cameras: camera.items.filter(item => item.source.clock.position.hostFrame === record.source.clock.position.hostFrame),
        input: input.items.find(item => item.source.clock.position.hostFrame === record.source.clock.position.hostFrame) ?? null,
        inputContext,
        curves: cache.get(key(context.jobId, offset + index))?.curves ?? {},
        unavailable: unavailable.items.filter(item => item.source.clock.position.hostFrame === record.source.clock.position.hostFrame),
        availability: [...logic.availability, ...presentation.availability, ...camera.availability,
          ...input.availability, ...unavailable.availability],
      });
    });
    publishFrame(context);
  });
}

function loadCurvePage(context: OpenArchive, page: number, curveId: string): Promise<void> {
  const offset = page * pageFrames;
  const indices = Array.from({ length: Math.min(pageFrames, context.frameCount - offset) }, (_, index) => offset + index);
  const frames = indices.map(index => cache.get(key(context.jobId, index)));
  if (frames.every(frame => frame !== undefined && curveId in frame.curves)) return Promise.resolve();
  return loadArchivePage(context, page, curveId, async signal => {
    if (frames.some(frame => frame === undefined)) await loadPage(context, page);
    signal.throwIfAborted();
    await readFrame(offset, { kind: 'pinned', signal });
    await readFrame(indices[indices.length - 1], { kind: 'pinned', signal });
    signal.throwIfAborted();
    const first = context.timebase.get(offset), last = context.timebase.get(indices[indices.length - 1]);
    if (!first || !last) throw new Error('当前页没有正式宿主帧时间，曲线未读取');
    const result = await channel('data-curves', readAll(pageOffset => playerApi.curveSamples(context.jobId, curveId, {
      firstSdkFrame: Number(first.hostFrame), lastSdkFrame: Number(last.hostFrame), offset: pageOffset, limit: 128,
    }, signal)));
    signal.throwIfAborted();
    for (const index of indices) {
      const id = key(context.jobId, index);
      const frame = cache.get(id)!;
      cache.set(id, { ...frame, curves: { ...frame.curves, [curveId]: {
        kind: 'sampled', samples: result.items.filter(sample => sample.source.clock.position.hostFrame === frame.hostFrame), availability: result.availability,
      } } });
    }
    publishFrame(context);
  });
}

function loadVisibleCurves(context: OpenArchive) {
  const state = getState();
  const page = Math.floor(state.frameIndex / pageFrames);
  const selected = state.selectedCurves;
  for (const neighbour of [page, page - 1, page + 1]) {
    if (neighbour < 0 || neighbour * pageFrames >= context.frameCount) continue;
    void (async () => {
      for (const curveId of selected) {
        const active = getState();
        if (!isCurrent(context.generation) || !active.selectedCurves.includes(curveId) ||
          Math.abs(Math.floor(active.frameIndex / pageFrames) - neighbour) > 1) return;
        try {
          await loadCurvePage(context, neighbour, curveId);
        } catch (error) {
          const entryId = consoleActions.error('data-curves', error);
          if (entryId === null) return;
          const reason = error instanceof Error ? error.message : String(error);
          for (let index = neighbour * pageFrames; index < Math.min(context.frameCount, (neighbour + 1) * pageFrames); index++) {
            const id = key(context.jobId, index), frame = cache.get(id);
            if (frame) cache.set(id, { ...frame, curves: { ...frame.curves, [curveId]: { kind: 'failed', reason, entryId } } });
          }
          publishFrame(context);
        }
      }
    })();
  }
}

async function imageObservations(context: OpenArchive, metadata: PlayerArchiveImage | PlayerFrameImage, signal: AbortSignal) {
  const range = (offset: number) => ({ firstSdkFrame: Number(metadata.sourceHostFrame),
    lastSdkFrame: Number(metadata.sourceHostFrame), offset, limit: 128 });
  const [poses, cameras] = await Promise.all([
    channel('presentation', readAll(offset => playerApi.presentation(context.jobId, range(offset), signal))),
    channel('camera', readAll(offset => playerApi.camera(context.jobId, range(offset), signal))),
  ]);
  const presentation = poses.items.find(item => item.presentationSequence === metadata.presentationSequence) ?? null;
  const camera = cameras.items.find(item => item.presentationSequence === metadata.presentationSequence &&
    (!('cameraInstanceId' in metadata) || item.cameraInstanceId === metadata.cameraInstanceId)) ?? null;
  return { presentation, camera,
    overlayReason: !presentation ? '画面没有同一表现序号的姿态记录' : !camera ? '画面没有同一相机提交的记录' : null };
}

async function loadImage(context: OpenArchive, hostFrame: string, request: number) {
  imageController?.abort();
  imageController = new AbortController();
  const signal = AbortSignal.any([context.controller.signal, imageController.signal]);
  const id = `${context.jobId}:${hostFrame}`;
  let image = images.get(id);
  try {
    if (!image) {
      const imageChannel = context.metadata.availability.find(item => item.channel === 'images');
      if (imageChannel?.available) {
        const records = await readAll(offset => playerApi.frameImages(context.jobId, {
          firstSdkFrame: Number(hostFrame), lastSdkFrame: Number(hostFrame), offset, limit: 128,
        }, signal));
        const metadata = records.items.at(-1);
        if (metadata) {
          const [blob, observations] = await Promise.all([
            playerApi.frameImage(context.jobId, metadata.imageIndex, signal), imageObservations(context, metadata, signal),
          ]);
          image = { kind: 'frame', metadata, image: blob, ...observations };
        } else image = { kind: 'unavailable', hostFrame, reason: `Player 档案未录制 SDK ${hostFrame} 帧的画面` };
      } else if (!context.metadata.captures.some(item => String(item.sdkFrame) === hostFrame)) {
        image = { kind: 'unavailable', hostFrame, reason: `Player 档案未提供 SDK ${hostFrame} 帧的画面` };
      } else {
        const metadata = await playerApi.archiveImageMetadata(context.jobId, hostFrame, signal);
        if (metadata.available) {
          const [blob, observations] = await Promise.all([
            playerApi.archiveImage(context.jobId, hostFrame, signal), imageObservations(context, metadata, signal),
          ]);
          image = { kind: 'capture', metadata, image: blob, ...observations };
        } else image = { kind: 'unavailable', hostFrame, reason: metadata.reason };
      }
      images.set(id, image);
      while (images.size > imageCacheLimit)
        for (const oldest of images.keys()) { images.delete(oldest); break; }
    }
    signal.throwIfAborted();
    if (request === frameRequest && isCurrent(context.generation))
      runActions.setData(context.generation, { archiveImage: image });
  } catch (error) {
    if (signal.aborted || request !== frameRequest || !isCurrent(context.generation)) return;
    consoleActions.error(source, error);
    runActions.setData(context.generation, { archiveImage: {
      kind: 'unavailable', hostFrame, reason: error instanceof Error ? error.message : String(error),
    } });
  }
}

export const archive = {
  inputFrames(jobId: PlayerJobId, actorId: string) {
    const prefix = `${jobId}:`;
    return [...cache.entries()].filter(([id]) => id.startsWith(prefix))
      .sort(([left], [right]) => Number(left.slice(prefix.length)) - Number(right.slice(prefix.length)))
      .flatMap(([, frame]) => {
      const input = frame.input?.actors.find(actor => actor.actorId === actorId);
      return input ? [{ frame: frame.hostFrame, input }] : [];
    });
  },

  curveSamples(jobId: PlayerJobId, curveId: string): PlayerCurveSample[] {
    return [...cache.entries()].flatMap(([id, frame]) => {
      const curve = frame.curves[curveId];
      return id.startsWith(jobId + ':') && curve?.kind === 'sampled' ? curve.samples : [];
    })
      .sort((left, right) => left.source.clock.seconds - right.source.clock.seconds ||
          (left.presentationSequence !== null && right.presentationSequence !== null
            ? BigInt(left.presentationSequence) < BigInt(right.presentationSequence) ? -1 : BigInt(left.presentationSequence) > BigInt(right.presentationSequence) ? 1 : 0
            : 0));
  },

  loadCurves() {
    if (current?.kind !== 'ready') return;
    const selected = getState().selectedCurves;
    for (const [id, request] of pages)
      if (request.curveId !== null && !selected.includes(request.curveId)) {
        request.controller.abort();
        pages.delete(id);
        pending.delete(id);
      }
    loadVisibleCurves(current);
  },
  cancel() {
    current?.controller.abort();
    current = null;
    imageController?.abort();
    for (const request of pages.values()) request.controller.abort();
    pages.clear();
    pending.clear();
    frameRequest++;
  },

  async open(jobId: PlayerJobId, generation: number, requestedFrame: number) {
    archive.cancel();
    const controller = new AbortController();
    current = { kind: 'opening', jobId, generation, controller };
    try {
      const [summary, layout, metadata, geometry, curves] = await Promise.all([
        playerApi.summary(jobId, controller.signal), playerApi.poseLayout(jobId, controller.signal),
        playerApi.archive(jobId, controller.signal), playerApi.geometry(jobId, controller.signal),
        channel('data-curves', readAll(offset => playerApi.curves(jobId, offset, 128, controller.signal))),
      ]);
      if (!isCurrent(generation)) { controller.abort(); return; }
      const timebase: ArchiveTimebase = new Map();
      current = { kind: 'ready', jobId, generation, metadata, frameCount: summary.frameCount, timebase, controller };
      transport.setTimebase(jobId, timebase);
      runActions.setData(generation, { archiveLayout: layout, archiveMetadata: metadata, geometry,
        metadataAvailability: summary.availability, archiveCurves: curves.items, curveAvailability: curves.availability });
      runActions.setSummary(generation, summary, true);
      if (summary.frameCount) {
        transport.place(Math.min(requestedFrame, summary.frameCount - 1));
        void transport.readBounds({ runId: jobId, frames: timebase }, summary.frameCount, controller.signal).then(() => {
          if (isCurrent(generation)) transport.setTimebase(jobId, timebase);
        }, error => {
          if (isCurrent(generation)) consoleActions.error('archive-time', error);
        });
      }
      archive.loadFrame();
    } catch (error) {
      if (!isCurrent(generation)) return;
      const entryId = consoleActions.error(source, error);
      if (entryId !== null) runActions.setData(generation, { failure: { reason: 'Player 档案读取失败', entryId } });
    }
  },

  loadFrame() {
    const context = current;
    const { runId, frameIndex, summary, generation } = getState();
    if (context?.kind !== 'ready' || runId !== context.jobId || !summary?.frameCount) return;
    const frameKey = key(runId, frameIndex);
    if (getState().frameKey === frameKey) return;
    const page = Math.floor(frameIndex / pageFrames);
    const request = ++frameRequest;
    imageController?.abort();
    runActions.setData(generation, { frame: null, archiveFrame: null, archiveImage: null });
    const allowed = new Set([page - 1, page, page + 1]);
    for (const [id, request] of pages) if (!allowed.has(request.page)) {
      request.controller.abort();
      pages.delete(id);
      pending.delete(id);
    }
    const failed = (error: unknown) => {
      if (request === frameRequest && isCurrent(generation)) consoleActions.error(source, error);
    };
    const show = () => {
      const archiveFrame = readCachedFrame(runId, frameIndex);
      if (request !== frameRequest || !archiveFrame || !isCurrent(generation)) return;
      runActions.setData(generation, { archiveFrame, frameKey });
      loadVisibleCurves(context);
      void loadImage(context, archiveFrame.hostFrame, request);
    };
    (cache.has(frameKey) ? Promise.resolve() : loadPage(context, page)).then(show, failed);
    readFrame(frameIndex, { kind: 'cursor' }).then(frame => {
      if (request !== frameRequest || !frame || !isCurrent(generation)) return;
      runActions.setData(generation, { frame, routeAvailability: frame.route?.availability ?? [] });
    }, failed);
    for (const neighbour of [page - 1, page + 1])
      if (neighbour >= 0 && neighbour * pageFrames < summary.frameCount && !cache.has(key(runId, neighbour * pageFrames)))
        void loadPage(context, neighbour).catch(error => {
          if (isCurrent(generation)) consoleActions.error(source, error);
        });
  },
};
