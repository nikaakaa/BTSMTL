import { postJson } from './http';
import { array, boolean, object, readWallContactField, text } from './playerRecords';

export type WallContactAuthoringTarget = { sourceAssetId: string; graphId: string; nodeId: string };
const readAuthoring = object({ sourceAssetId: text, assetPath: text, graphId: text, nodeId: text,
  revision: text, saved: boolean, values: array(object({ id: text, value: text })),
  fields: array(readWallContactField) });
export type WallContactAuthoring = ReturnType<typeof readAuthoring>;

export const wallContactApi = {
  read: (target: WallContactAuthoringTarget, signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/wall-contact/read', target, signal).then(value => readAuthoring(value)),
  save: (target: WallContactAuthoringTarget, expectedRevision: string, values: { id: string; value: string }[], signal?: AbortSignal) =>
    postJson<unknown>('/api/authoring/wall-contact/save', { target, expectedRevision, values }, signal).then(value => readAuthoring(value)),
};
