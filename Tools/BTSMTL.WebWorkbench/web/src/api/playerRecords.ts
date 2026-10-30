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
const optional = <T>(decode: Decoder<T>): Decoder<T | undefined> =>
  (value, path) => value === undefined ? undefined : decode(value, path);
const array = <T>(decode: Decoder<T>): Decoder<T[]> => (value, path = 'Player') => {
  if (!Array.isArray(value)) throw new Error(`${path} 需要数组`);
  return value.map((item: unknown, index) => decode(item, `${path}[${index}]`));
};
const tuple = <const Shape extends readonly Decoder<unknown>[]>(...shape: Shape) =>
  (value: unknown, path = 'Player'): { -readonly [Key in keyof Shape]: ReturnType<Shape[Key]> } => {
    if (!Array.isArray(value) || value.length !== shape.length) throw new Error(`${path} 需要 ${shape.length} 项`);
    return shape.map((decode, index) => decode(value[index], `${path}[${index}]`)) as {
      -readonly [Key in keyof Shape]: ReturnType<Shape[Key]>
    };
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
  playerBuildId: text, contentRevision: text, sceneRevision: text, inputSource: text, inputRevision: nullable(text),
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
const previewTarget = object({
  liveId, playerBuildId: buildId, scene: text, sceneRevision: text, source: runSource,
});
export const readLivePlayer = object({
  liveId, buildId, startedAt: text, state: text, exitCode: nullable(number),
  width: nullable(number), height: nullable(number), hello: opaque, player: opaque,
  previewTarget: nullable(previewTarget), viewers: number, latestVideoFrame: nullable(number),
});
export const readLivePlayers = array(readLivePlayer);
export const readPlayerCreationOptions = object({
  requestId: jobId, firstSdkFrame: number, lastSdkFrame: number, captureFrames: array(number),
  outputDirectory: text, width: number, height: number, frameRangeBasis: text,
  quality: number,
});

export const readPlayerJobRequest = object({
  requestId: jobId, playerBuildId: buildId, scene: text, sceneRevision: text, source: runSource,
  firstSdkFrame: number, lastSdkFrame: number, captureFrames: array(number), outputDirectory: text,
  width: number, height: number, quality: number,
});
export const readPlayerJobReceipt = object({
  request: readPlayerJobRequest,
  stage: choice('queued', 'running', 'completed', 'failed', 'cancelled', 'rejected', 'unavailable'),
  acceptedAt: text, finishedAt: nullable(text), liveId: nullable(liveId), archivePath: nullable(text),
  load: opaque, result: opaque, error: nullable(text), cachedFrom: nullable(jobId),
  jobId, playerBuildId: buildId, statusPath: text,
});
export const readPlayerCommandReceipt = object({
  stage: choice('sent', 'submitted', 'applied', 'rejected', 'not-applied'), commandId: nullable(text), ack: opaque,
});
const nativeAck = object({
  type: choice('ack'), commandId: text, ok: boolean, frame: number,
  error: optional(nullable(text)), path: optional(nullable(text)),
});
export function readPlayerCommandResult(value: unknown, path = 'Player.command') {
  const receipt = readPlayerCommandReceipt(value, path);
  if (receipt.ack === null) return { ...receipt, ack: null };
  const ack = nativeAck(receipt.ack, `${path}.ack`);
  return { ...receipt, ack: {
    type: ack.type, commandId: ack.commandId, ok: ack.ok, hostFrame: String(ack.frame),
    error: ack.error ?? undefined, path: ack.path ?? undefined, rawJson: JSON.stringify(receipt.ack),
  } };
}
export const readPlayerJobs = object({
  capacity: number, concurrency: number, queued: number, archiveCacheCapacity: number,
  items: array(readPlayerJobReceipt),
});
export const readPlayerJob = object({ receipt: readPlayerJobReceipt, archive: opaque });
export const readPlayerSummary = object({
  runId: jobId, contentRevision: text, frameDisplayName: text, firstFrame: text, lastFrame: text,
  frameCount: number, channels: array(text), geometryAvailable: boolean, fields: array(field), rawJson: text,
  playerBuildId: buildId, sceneRevision: text, inputSource: text, inputRevision: nullable(text),
  availability: array(availability),
});
const vector3 = tuple(number, number, number);
const quaternion = tuple(number, number, number, number);
const location = object({ displayName: text, documentId: text, elementId: text });
const record = object({ displayName: text, fields: array(field), rawJson: text, locations: array(location) });
const call = object({
  actorId: text, programId: text, programRevision: text, invocationPath: text, parentInvocationPath: text,
  actionId: text, executionGeneration: text, invocationGeneration: text, parentInvocationGeneration: text, playbackId: text,
});
const routeFrame = object({
  hostFrame: text, seconds: number,
  playbacks: array(object({ documentId: text, cursorSeconds: number, status: text })), active: array(location),
  nodes: array(object({ sequence: text, graphId: text, nodeId: text, kind: text, generation: text, result: text, call })),
  edges: array(object({ sequence: text, graphId: text, edgeId: text, selected: boolean, passed: boolean, call })),
  ports: array(object({ sequence: text, graphId: text, nodeId: text, portId: text, direction: text, valueType: text,
    value: text, rawJson: text, call, sourceAvailable: boolean, reason: text })),
  availability: array(availability),
});
const logicPose = object({ position: vector3, rotation: quaternion });
const segment = object({ shapeIndex: number, start: logicPose, end: logicPose });
const frame = object({
  hostFrame: text, seconds: number, source, availability: array(availability),
  actors: array(object({ id: text, displayName: text, root: logicPose, fields: array(field), rawJson: text })),
  channels: array(text), consistency: nullable(object({ displayName: text, value: text })), fields: array(field),
  sections: array(object({ displayName: text, emptyMessage: text, records: array(record) })),
  geometry: object({ sweeps: array(object({ segments: array(segment), targets: array(segment) })),
    contacts: array(object({ point: vector3, normal: vector3 })) }), rawJson: text, route: nullable(routeFrame),
});
export const readPlayerFrames = page(frame);
export const readPlayerObservations = object({
  runId: jobId, contentRevision: text, channels: array(text), firstFrame: text, lastFrame: text,
  offset: number, total: number, nextOffset: nullable(number), items: array(frame),
});
export const readPlayerGeometry = object({
  runId: jobId, contentRevision: text, rawJson: text,
  world: array(object({ identity: text, kind: text, pose: logicPose, radius: number, halfLength: number,
    halfExtents: vector3, isTrigger: boolean })),
  worldTriangles: array(object({ a: vector3, b: vector3, c: vector3 })),
  actors: array(object({ actorId: text, radius: number, halfLength: number, centerOffset: vector3 })),
  shapes: array(object({ identity: text, kind: text, radius: number, halfLength: number, halfExtents: vector3,
    vertices: array(vector3), parts: array(object({ shapeIndex: number, localPose: logicPose })) })),
});
export const readPlayerSourceMap = page(record);
const pose = object({ position: vector3, rotation: quaternion, scale: vector3 });
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
  presentationTick: number, interpolationFactor: number, worldPosition: vector3, worldRotation: quaternion,
  worldToCamera: array(number), projection: array(number),
}));
export const readPlayerUnavailable = observationPage(object({
  source, presentationSequence: text, channel: text, reason: text, actorRosterIndex: number, boneIndex: number,
}));
export const readPlayerInput = object({
  page: page(object({
    source, actors: array(object({
      actorId: text, rosterIndex: number, received: boolean, inputSequence: text, sourceTick: text,
      values: array(object({ inputId: text, kind: text, value: array(number) })),
      requests: array(object({ requestId: text, sequence: text, sourceTick: text, expireTick: text, priority: number })),
    })),
  })),
  plans: array(object({ actorId: text, plan: text, planRevision: text, schemeIndex: number })),
  availability: array(availability),
});
export const readPlayerArchiveImage = object({
  jobId, archiveJobId: identity<'archive'>(), playerBuildId: buildId, contentRevision: text, sceneRevision: text,
  inputSource: text, inputRevision: nullable(text), hostFrame: text, sourceHostFrame: text, presentationSequence: text,
  width: number, height: number, available: boolean, presentationPrepared: boolean,
  relativePath: nullable(text), mediaType: text, reason: text,
});
const frameImage = object({
  jobId, archiveJobId: identity<'archive'>(), imageIndex: number, source,
  sourceHostFrame: text, verifiedHostFrame: text, previousHostFrame: text, previousGameplayTick: text,
  presentationSequence: text, unityFrame: number, cameraInstanceId: number, width: number, height: number,
  flipY: boolean, presentationAvailable: boolean, presentationTick: number, elapsedSeconds: number,
  interpolationFactor: number, mediaType: text, encodedLength: number,
});
export const readPlayerFrameImages = observationPage(frameImage);
export const readPlayerArchive = object({
  protocol: choice('q3-preview-observation/1'), jobId: identity<'archive'>(), status: text,
  playerBuildId: buildId, contentRevision: text, sceneRevision: text, inputMode: text, inputSource: text, inputRevision: nullable(text),
  firstRecordedSdkFrame: number, lastRecordedSdkFrame: number, sdkTickRate: number, scheduleStartFrame: number,
  captures: array(object({ sdkFrame: number })), availability: array(availability),
});

export type PlayerJobId = ReturnType<typeof jobId>;
export type LivePlayer = ReturnType<typeof readLivePlayer>;
export type PlayerPreviewTarget = ReturnType<typeof previewTarget>;
export type PlayerBuildId = ReturnType<typeof buildId>;
export type StartPlayerJobRequest = ReturnType<typeof readPlayerJobRequest>;
export type PlayerCreationRequest = Pick<StartPlayerJobRequest, 'requestId' | 'source' | 'width' | 'height'>;
export type PlayerJobReceipt = ReturnType<typeof readPlayerJobReceipt>;
export type PlayerCommandReceipt = ReturnType<typeof readPlayerCommandReceipt>;
export type PlayerJobStage = PlayerJobReceipt['stage'];
export type PlayerJob = ReturnType<typeof readPlayerJob>;
export type PlayerSummary = ReturnType<typeof readPlayerSummary>;
export type PlayerFramesPage = ReturnType<typeof readPlayerFrames>;
export type PlayerObservationPage = ReturnType<typeof readPlayerObservations>;
export type PlayerPoseLayout = ReturnType<typeof readPlayerPoseLayout>;
export type PlayerLogicPage = ReturnType<typeof readPlayerLogic>;
export type PlayerPresentationPage = ReturnType<typeof readPlayerPresentation>;
export type PlayerCameraPage = ReturnType<typeof readPlayerCamera>;
export type PlayerUnavailablePage = ReturnType<typeof readPlayerUnavailable>;
export type PlayerInputPage = ReturnType<typeof readPlayerInput>;
export type PlayerArchiveImage = ReturnType<typeof readPlayerArchiveImage>;
export type PlayerFrameImage = ReturnType<typeof frameImage>;
export type PlayerArchive = ReturnType<typeof readPlayerArchive>;
export type PlayerArchiveRange = { firstSdkFrame: number; lastSdkFrame: number; offset: number; limit: number };

export const newPlayerJobId = (): PlayerJobId => crypto.randomUUID() as PlayerJobId;
export const readPlayerJobId = jobId;
