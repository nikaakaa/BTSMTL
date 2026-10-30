type Decoder<T> = (value: unknown, path?: string) => T;
type Identity<Kind extends string> = string & { readonly playerIdentity: Kind };

const text: Decoder<string> = (value, path = 'Player') => {
  if (typeof value !== 'string') throw new Error(`${path} 需要字符串`);
  return value;
};
const number: Decoder<number> = (value, path = 'Player') => {
  if (typeof value !== 'number') throw new Error(`${path} 需要数字`);
  return value;
};
const boolean: Decoder<boolean> = (value, path = 'Player') => {
  if (typeof value !== 'boolean') throw new Error(`${path} 需要布尔值`);
  return value;
};
const opaque: Decoder<unknown> = value => value;
const nullable = <T>(decode: Decoder<T>): Decoder<T | null> =>
  (value, path) => value === null ? null : decode(value, path);
const array = <T>(decode: Decoder<T>): Decoder<T[]> => (value, path = 'Player') => {
  if (!Array.isArray(value)) throw new Error(`${path} 需要数组`);
  return value.map((item: unknown, index) => decode(item, `${path}[${index}]`));
};
const identity = <Kind extends string>(): Decoder<Identity<Kind>> =>
  (value, path) => text(value, path) as Identity<Kind>;
const choice = <const Values extends readonly string[]>(...values: Values): Decoder<Values[number]> =>
  (value, path = 'Player') => {
    const parsed = text(value, path);
    if (!values.some(item => item === parsed)) throw new Error(`${path} 不支持 ${parsed}`);
    return parsed as Values[number];
  };
function object<Shape extends Record<string, Decoder<unknown>>>(shape: Shape) {
  return (value: unknown, path = 'Player'): { [Key in keyof Shape]: ReturnType<Shape[Key]> } => {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error(`${path} 需要对象`);
    const record = value as Record<string, unknown>;
    const result: Record<string, unknown> = {};
    for (const key of Object.keys(shape)) result[key] = shape[key](record[key], `${path}.${key}`);
    return result as { [Key in keyof Shape]: ReturnType<Shape[Key]> };
  };
}

const jobId = identity<'job'>();
const liveId = identity<'live'>();
const buildId = identity<'build'>();
const field = object({ displayName: text, value: text });
const availability = object({ channel: text, available: boolean, complete: boolean, reason: text });
const source = object({
  playerBuildId: text, contentRevision: text, sceneRevision: text, inputSource: text, inputRevision: text,
  sdkFrame: text, gameplayTick: text,
});
const page = <T>(item: Decoder<T>) => object({ items: array(item), total: number, nextOffset: nullable(number) });
const observationPage = <T>(item: Decoder<T>) => object({ page: page(item), availability: array(availability) });
const inputChoice = object({ actorId: text, inputPlanRoot: text, inputPlanRevision: text, schemeIndex: number });
const replay = object({ kind: choice('replay'), path: text, revision: text });
const recording = object({ kind: choice('recording'), runId: text, revision: text });
const local = object({ kind: choice('local'), lastSdkFrame: number, players: array(inputChoice) });
const device = object({ kind: choice('device'), lastSdkFrame: number });
const runSource = (value: unknown, path = 'Player.source') => {
  if (typeof value !== 'object' || value === null || !('kind' in value)) throw new Error(`${path} 缺少 kind`);
  switch (value.kind) {
    case 'replay': return replay(value, path);
    case 'recording': return recording(value, path);
    case 'local': return local(value, path);
    case 'device': return device(value, path);
    default: throw new Error(`${path} 的 kind 不受支持`);
  }
};

export const readPlayerJobRequest = object({
  requestId: jobId, playerBuildId: buildId, scene: text, sceneRevision: text, source: runSource,
  firstSdkFrame: number, lastSdkFrame: number, captureFrames: array(number), outputDirectory: text,
  width: number, height: number,
});
export const readPlayerJobReceipt = object({
  request: readPlayerJobRequest,
  stage: choice('queued', 'running', 'completed', 'failed', 'cancelled', 'rejected', 'unavailable'),
  acceptedAt: text, finishedAt: nullable(text), liveId: nullable(liveId), archivePath: nullable(text),
  load: opaque, result: opaque, error: nullable(text), cachedFrom: nullable(jobId),
  jobId, playerBuildId: buildId, statusPath: text,
});
export const readPlayerJobs = object({
  capacity: number, concurrency: number, queued: number, archiveCacheCapacity: number,
  items: array(readPlayerJobReceipt),
});
export const readPlayerJob = object({ receipt: readPlayerJobReceipt, archive: opaque });
export const readPlayerSummary = object({
  runId: jobId, contentRevision: text, frameDisplayName: text, firstFrame: text, lastFrame: text,
  frameCount: number, channels: array(text), geometryAvailable: boolean, fields: array(field), rawJson: text,
  playerBuildId: buildId, sceneRevision: text, inputSource: text, inputRevision: text,
  availability: array(availability),
});
const pose = object({ position: array(number), rotation: array(number), scale: array(number) });
const actorPose = object({
  actorId: text, rosterIndex: number, sourceSequence: text, completionIdentity: text,
  worldRoot: pose, localPoseRoot: pose, localBones: array(pose),
});
export const readPlayerPoseLayout = object({
  coordinates: text, units: text, actors: array(object({
    actorId: text, rosterIndex: number, rigId: text, rigRevision: text,
    bones: array(text), parents: array(number), diagnosticsUnavailableReason: text,
  })),
});
export const readPlayerLogic = object({
  bodyColumns: array(text), page: page(object({ source, checksum: text, bodies: array(array(number)) })),
  availability: array(availability),
});
export const readPlayerPresentation = observationPage(object({
  source, presentationSequence: text, verifiedSdkFrame: text, previousSdkFrame: text, previousGameplayTick: text,
  presentationTick: number, elapsedSeconds: number, interpolationFactor: number, actors: array(actorPose),
}));
export const readPlayerCamera = observationPage(object({
  source, presentationSequence: text, verifiedSdkFrame: text, cameraInstanceId: number, width: number, height: number,
  presentationTick: number, interpolationFactor: number, worldPosition: array(number), worldRotation: array(number),
  worldToCamera: array(number), projection: array(number),
}));
export const readPlayerUnavailable = observationPage(object({
  source, presentationSequence: text, channel: text, reason: text, actorRosterIndex: number, boneIndex: number,
}));

export type PlayerJobId = ReturnType<typeof jobId>;
export type PlayerBuildId = ReturnType<typeof buildId>;
export type StartPlayerJobRequest = ReturnType<typeof readPlayerJobRequest>;
export type PlayerJobReceipt = ReturnType<typeof readPlayerJobReceipt>;
export type PlayerJobStage = PlayerJobReceipt['stage'];
export type PlayerJob = ReturnType<typeof readPlayerJob>;
export type PlayerSummary = ReturnType<typeof readPlayerSummary>;
export type PlayerPoseLayout = ReturnType<typeof readPlayerPoseLayout>;
export type PlayerLogicPage = ReturnType<typeof readPlayerLogic>;
export type PlayerPresentationPage = ReturnType<typeof readPlayerPresentation>;
export type PlayerCameraPage = ReturnType<typeof readPlayerCamera>;
export type PlayerUnavailablePage = ReturnType<typeof readPlayerUnavailable>;
export type PlayerArchiveRange = { firstSdkFrame: number; lastSdkFrame: number; offset: number; limit: number };

export const newPlayerJobId = (): PlayerJobId => crypto.randomUUID() as PlayerJobId;
