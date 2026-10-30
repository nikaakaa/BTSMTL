import { getJson, query } from './http';
import type { HostAvailability, Sources } from './types';
import { readPlayerInputCatalog } from './playerRecords';

const base = '/api/preview';

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<HostAvailability>(`${base}/host`, signal),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<unknown>(`${base}/inputs${query({ scene, revision })}`, signal).then(value => readPlayerInputCatalog(value)),

};
