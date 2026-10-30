import { getJson, query } from './http';
import type { Sources } from './types';
import { readPlayerHost, readPlayerInputCatalog } from './playerRecords';

const base = '/api/preview';

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<unknown>(`${base}/host`, signal).then(value => readPlayerHost(value)),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<unknown>(`${base}/inputs${query({ scene, revision })}`, signal).then(value => readPlayerInputCatalog(value)),

};
