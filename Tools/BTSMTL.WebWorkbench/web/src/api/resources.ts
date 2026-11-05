import { getJson } from './http';
import type { AnimationLibrary } from './types';
import { array, boolean, choice, number, object, text } from './playerRecords';

const readAnimationLibrary = object({
  schema: choice('btsmtl-web-animation-library/1'), complete: boolean, file: text, sha256: text, sourceSha256: text,
  clips: array(object({ name: text, category: text, animationIndex: number, duration: number, exportedDuration: number,
    frameRate: number, unityAsset: text, value: text, provenance: object({ description: text }),
    resource: object({ identity: text, guid: text, localFileId: text, contentRevision: text }) })),
});

export const resourceApi = {
  animations: (signal?: AbortSignal): Promise<AnimationLibrary> => getJson<unknown>('/api/resources/animations', signal)
    .then(value => readAnimationLibrary(value, 'AnimationLibrary')),
  animationFilePath: (sha256: string) => `/api/resources/animations/file/${encodeURIComponent(sha256)}`,
};
