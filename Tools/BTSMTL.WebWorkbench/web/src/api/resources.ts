import { getJson } from './http';
import type { AnimationLibrary } from './types';

export const resourceApi = {
  animations: (signal?: AbortSignal) => getJson<AnimationLibrary>('/api/resources/animations', signal),
  animationFilePath: (sha256: string) => `/api/resources/animations/file/${encodeURIComponent(sha256)}`,
};
