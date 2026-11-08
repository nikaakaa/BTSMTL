import { getJson, postJson, query } from './http';
import type { Sources } from './types';
import { readPlayerHost, readPlayerInputCatalog } from './playerRecords';
import { readPublishedTimelineScene, readQuickDescriptionReply, readQuickScenes, readQuickStatusReply, type PreviewQuickJobId, type PreviewQuickRequest, type PreviewQuickSeekRequest, type PreviewQuickTarget } from './previewQuickRecords';

const base = '/api/preview';

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<unknown>(`${base}/host`, signal).then(value => readPlayerHost(value)),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal).then(sources => ({
    ...sources, quickScenes: readQuickScenes(sources.quickScenes, 'Preview.sources.quickScenes'),
    timelines: sources.timelines.map((timeline, index) => ({ ...timeline,
      scene: readPublishedTimelineScene(timeline.scene, `Preview.sources.timelines[${index}].scene`),
    })),
  })),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<unknown>(`${base}/inputs${query({ scene, revision })}`, signal).then(value => readPlayerInputCatalog(value)),
  describeQuick: (target: PreviewQuickTarget, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/describe`, target, signal, [409]).then(value => readQuickDescriptionReply(value, 'PreviewQuick.describe')),
  planQuick: (request: PreviewQuickRequest, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/plans`, request, signal, [409]).then(value => readQuickStatusReply(value, 'PreviewQuick.start')),
  // waitWhileStage：宿主等到作业离开该阶段或结束再答复，不必定时轮询。
  quickStatus: (jobId: PreviewQuickJobId, signal?: AbortSignal, waitWhileStage?: string) =>
    getJson<unknown>(`${base}/quick/plans/${encodeURIComponent(jobId)}${query({ stage: waitWhileStage })}`, signal, [409])
      .then(value => readQuickStatusReply(value, 'PreviewQuick.status')),
  seekQuick: (jobId: PreviewQuickJobId, request: PreviewQuickSeekRequest, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/plans/${encodeURIComponent(jobId)}/seek`, request, signal, [409])
      .then(value => readQuickStatusReply(value, 'PreviewQuick.seek')),
  cancelQuick: (jobId: PreviewQuickJobId, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/plans/${encodeURIComponent(jobId)}/cancel`, undefined, signal, [409])
      .then(value => readQuickStatusReply(value, 'PreviewQuick.cancel')),
};
