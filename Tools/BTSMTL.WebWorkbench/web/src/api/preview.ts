import { getJson, query } from './http';
import type { HostAvailability, InputCatalog, Sources } from './types';

const base = '/api/preview';

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<HostAvailability>(`${base}/host`, signal),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<InputCatalog>(`${base}/inputs${query({ scene, revision })}`, signal),

};
