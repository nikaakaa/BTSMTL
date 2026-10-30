import { ApiError } from '../api/http';
import { playerApi } from '../api/player';
import type { PlayerArchive, PlayerArchiveImage, PlayerFrameImage, PlayerJobId } from '../api/playerRecords';
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
const pages = new Map<string, AbortController>();
const images = new Map<string, ArchiveImage>();
const key = (jobId: PlayerJobId, index: number) => `${jobId}:${index}`;
interface OpenArchive {
  kind: 'ready';
  jobId: PlayerJobId;
  generation: number;
  metadata: PlayerArchive;
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

function loadPage(context: OpenArchive, page: number): Promise<void> {
  const id = `${context.generation}:${key(context.jobId, page)}`;
  const existing = pending.get(id);
  if (existing) return existing;
  const controller = new AbortController();
  pages.set(id, controller);
  const signal = AbortSignal.any([context.controller.signal, controller.signal]);
  const offset = page * pageFrames;
  const request = (async () => {
    const logic = await playerApi.logic(context.jobId, {
      firstSdkFrame: context.metadata.firstRecordedSdkFrame, lastSdkFrame: context.metadata.lastRecordedSdkFrame,
      offset, limit: pageFrames,
    }, signal);
    signal.throwIfAborted();
    logic.page.items.forEach((record, index) => context.timebase.set(offset + index, {
      hostFrame: record.source.sdkFrame,
      seconds: (Number(record.source.sdkFrame) - context.metadata.firstRecordedSdkFrame) / context.metadata.sdkTickRate,
    }));
    transport.setTimebase(context.jobId, context.timebase);
    const first = logic.page.items.at(0);
    const last = logic.page.items.at(-1);
    if (!first || !last) return;
    const range = (pageOffset: number) => ({
      firstSdkFrame: Number(first.source.sdkFrame), lastSdkFrame: Number(last.source.sdkFrame),
      offset: pageOffset, limit: 128,
    });
    const [presentation, camera, input, unavailable] = await Promise.all([
      channel('presentation', readAll(pageOffset => playerApi.presentation(context.jobId, range(pageOffset), signal))),
      channel('camera', readAll(pageOffset => playerApi.camera(context.jobId, range(pageOffset), signal))),
      channel('input', readAll(pageOffset => playerApi.input(context.jobId, range(pageOffset), signal))),
      channel('unavailable', readAll(pageOffset => playerApi.unavailable(context.jobId, range(pageOffset), signal))),
    ]);
    signal.throwIfAborted();
    logic.page.items.forEach((record, index) => {
      remember(context.jobId, offset + index, {
        hostFrame: record.source.sdkFrame, logic: record,
        presentations: presentation.items.filter(item => item.source.sdkFrame === record.source.sdkFrame),
        cameras: camera.items.filter(item => item.source.sdkFrame === record.source.sdkFrame),
        input: input.items.find(item => item.source.sdkFrame === record.source.sdkFrame) ?? null,
        unavailable: unavailable.items.filter(item => item.source.sdkFrame === record.source.sdkFrame),
        availability: [...logic.availability, ...presentation.availability, ...camera.availability,
          ...input.availability, ...unavailable.availability],
      });
    });
  })().finally(() => {
    if (pending.get(id) === request) { pending.delete(id); pages.delete(id); }
  });
  pending.set(id, request);
  return request;
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
  cancel() {
    current?.controller.abort();
    current = null;
    imageController?.abort();
    for (const controller of pages.values()) controller.abort();
    pages.clear();
    pending.clear();
    frameRequest++;
  },

  async open(jobId: PlayerJobId, generation: number, requestedFrame: number) {
    archive.cancel();
    const controller = new AbortController();
    current = { kind: 'opening', jobId, generation, controller };
    try {
      const [summary, layout, metadata, geometry] = await Promise.all([
        playerApi.summary(jobId, controller.signal), playerApi.poseLayout(jobId, controller.signal),
        playerApi.archive(jobId, controller.signal), playerApi.geometry(jobId, controller.signal),
      ]);
      if (!isCurrent(generation)) { controller.abort(); return; }
      const timebase: ArchiveTimebase = new Map();
      for (const [id, frame] of cache) if (id.startsWith(jobId + ':')) timebase.set(Number(id.slice(jobId.length + 1)), {
        hostFrame: frame.hostFrame,
        seconds: (Number(frame.hostFrame) - metadata.firstRecordedSdkFrame) / metadata.sdkTickRate,
      });
      current = { kind: 'ready', jobId, generation, metadata, timebase, controller };
      transport.setTimebase(jobId, timebase);
      runActions.setData(generation, { archiveLayout: layout, archiveMetadata: metadata, geometry,
        metadataAvailability: summary.availability });
      runActions.setSummary(generation, summary, true);
      if (summary.frameCount) transport.place(Math.min(requestedFrame, summary.frameCount - 1));
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
    const allowed = new Set([page - 1, page, page + 1].map(index => `${generation}:${key(runId, index)}`));
    for (const [id, controller] of pages) if (!allowed.has(id)) {
      controller.abort();
      pages.delete(id);
      pending.delete(id);
    }
    const show = (frame: Awaited<ReturnType<typeof readFrame>>) => {
      const archiveFrame = cache.get(frameKey);
      if (request !== frameRequest || !frame || !archiveFrame || !isCurrent(generation)) return;
      runActions.setData(generation, { frame, archiveFrame, frameKey });
      void loadImage(context, archiveFrame.hostFrame, request);
    };
    Promise.all([cache.has(frameKey) ? Promise.resolve() : loadPage(context, page), readFrame(frameIndex)])
      .then(([, frame]) => show(frame), error => {
      if (isCurrent(generation)) consoleActions.error(source, error);
    });
    for (const neighbour of [page - 1, page + 1])
      if (neighbour >= 0 && neighbour * pageFrames < summary.frameCount && !cache.has(key(runId, neighbour * pageFrames)))
        void loadPage(context, neighbour).catch(error => {
          if (isCurrent(generation)) consoleActions.error(source, error);
        });
  },
};
