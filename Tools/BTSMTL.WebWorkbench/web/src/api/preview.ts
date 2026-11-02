import { getJson, postJson, query } from './http';
import type { Sources } from './types';
import { readPlayerHost, readPlayerInputCatalog } from './playerRecords';
import { readQuickDescriptionReply, readQuickStatusReply, type PreviewQuickJobId, type PreviewQuickRequest, type PreviewQuickTarget } from './previewQuickRecords';

const base = '/api/preview';

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<unknown>(`${base}/host`, signal).then(value => readPlayerHost(value)),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<unknown>(`${base}/inputs${query({ scene, revision })}`, signal).then(value => readPlayerInputCatalog(value)),
  describeQuick: (target: PreviewQuickTarget, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/describe`, target, signal, [409]).then(value => readQuickDescriptionReply(value, 'PreviewQuick.describe')),
  planQuick: (request: PreviewQuickRequest, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/plans`, request, signal, [409]).then(value => readQuickStatusReply(value, 'PreviewQuick.start')),
  quickStatus: (jobId: PreviewQuickJobId, signal?: AbortSignal) =>
    getJson<unknown>(`${base}/quick/plans/${encodeURIComponent(jobId)}`, signal, [409]).then(value => readQuickStatusReply(value, 'PreviewQuick.status')),
  cancelQuick: (jobId: PreviewQuickJobId, signal?: AbortSignal) =>
    postJson<unknown>(`${base}/quick/plans/${encodeURIComponent(jobId)}/cancel`, undefined, signal, [409])
      .then(value => readQuickStatusReply(value, 'PreviewQuick.cancel')),
};
