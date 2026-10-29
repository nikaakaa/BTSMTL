// 宿主响应类型。64 位整数、帧号、修订与 FP 原值按宿主原样保留为 string。

export interface Field {
  displayName: string;
  value: string;
}

export interface Availability {
  channel: string;
  available: boolean;
  complete: boolean;
  reason: string;
}

export interface Page<T> {
  items: T[];
  total: number;
  nextOffset: number | null;
}

export interface RangePage<T> extends Page<T> {
  offset: number;
}

// ── 宿主声明 ──

export interface PreviewMode {
  id: string;
  displayName: string;
  available: boolean;
  reason: string;
}

export interface QueryField {
  name: string;
  displayName: string;
  inputType: string;
  placeholder: string;
}

export interface NumberInput {
  name: string;
  displayName: string;
  minimum: string;
  step: string;
  initialValue: string;
}

export interface HostDescription {
  displayName: string;
  frameDisplayName: string;
  filters: QueryField[];
  inputLimit: NumberInput;
  modes: PreviewMode[];
}

export interface HostAvailability {
  available: boolean;
  description: HostDescription | null;
}

// ── 来源与输入 ──

export type RunSource =
  | { kind: 'replay'; path: string; revision: string }
  | { kind: 'recording'; runId: string; revision: string }
  | { kind: 'local'; lastSdkFrame: number; players: PlayerInputChoice[] };

export interface PlayerInputChoice {
  actorId: string;
  inputPlanRoot: string;
  inputPlanRevision: string;
  schemeIndex: number;
}

export interface SourceFile {
  path: string;
  revision: string;
}

export interface ReplayChoice {
  id: string;
  displayName: string;
  source: RunSource;
}

export interface Sources {
  scenes: SourceFile[];
  replays: ReplayChoice[];
  sceneDirectory: string;
  replayDirectory: string;
  runDirectory: string;
}

export interface InputCatalog {
  revision: string;
  tickRate: number;
  players: {
    actorId: string;
    values: { inputId: string; kind: string; encoding: string }[];
    requests: { inputId: string; bufferTicks: number; priority: number; timingClass: number }[];
  }[];
  actorTargets: string[];
}

// ── 运行 ──

export type RunStage = 'accepted' | 'running' | 'completed' | 'stopped' | 'failed' | 'cancelled';

export interface RunReceipt {
  jobId: string;
  stage: RunStage;
  acceptedAt: string;
  finishedAt: string | null;
  resultPath: string | null;
  error: string | null;
  workerId?: string | null;
  processId?: number | null;
}

export interface StartRunRequest {
  requestId: string;
  scene: string;
  sceneRevision: string;
  source: RunSource;
  interactive: boolean;
}

export interface CancelResult {
  cancellationRequested: boolean;
  receipt: RunReceipt;
}

export type PlaybackState = 'paused' | 'playing' | 'stepping' | 'stopped' | 'completed';
export type PlaybackCommand = 'Play' | 'Pause' | 'Step' | 'Stop' | 'Cancel';

export interface PlaybackView {
  ready: boolean;
  receipt: RunReceipt;
  snapshot: {
    state: PlaybackState;
    lastCommandId: string | null;
    observation: { summary: Summary };
  } | null;
}

export interface ControlRequest {
  commandId: string;
  command: PlaybackCommand;
}

export interface ControlResult {
  commandId: string;
  command: PlaybackCommand;
  stage: 'submitted' | 'applied' | 'rejected';
  hostFrame: string | null;
  error: string | null;
}

export interface Summary {
  runId: string;
  contentRevision: string;
  frameDisplayName: string;
  firstFrame: string;
  lastFrame: string;
  frameCount: number;
  channels: string[];
  geometryAvailable: boolean;
  fields: Field[];
  rawJson: string;
}

// ── 帧记录 ──

export interface AuthoringLocation {
  displayName: string;
  documentId: string;
  elementId: string;
}

export interface HostRecord {
  displayName: string;
  fields: Field[];
  rawJson: string;
  locations: AuthoringLocation[];
}

export interface Section {
  displayName: string;
  emptyMessage: string;
  records: HostRecord[];
}

export type Vec3 = [number, number, number];
export type Quat = [number, number, number, number];

export interface Pose {
  position: Vec3;
  rotation: Quat;
}

export interface Actor {
  id: string;
  displayName: string;
  root: Pose;
  fields: Field[];
  rawJson: string;
}

export interface Consistency {
  displayName: string;
  value: string;
}

export interface SweepSegment {
  shapeIndex: number;
  start: Pose;
  end: Pose;
}

export interface FrameGeometry {
  sweeps: { segments: SweepSegment[]; targets: SweepSegment[] }[];
  contacts: { point: Vec3; normal: Vec3 }[];
}

export interface Frame {
  hostFrame: string;
  seconds: number;
  actors: Actor[];
  channels: string[];
  consistency: Consistency | null;
  fields: Field[];
  sections: Section[];
  geometry: FrameGeometry;
  rawJson: string;
  route: RouteFrame | null;
}

export interface ObservationPage extends RangePage<Frame> {
  runId: string;
  contentRevision: string;
  channels: string[];
  firstFrame: string;
  lastFrame: string;
}

export interface ObservationQuery {
  firstFrame: string;
  lastFrame: string;
  rosterIndex?: string;
  source?: string;
}

export type ObservationStreamEvent =
  | { event: 'frame'; id: string; data: { runId: string; contentRevision: string; observation: Frame } }
  | { event: 'end'; data: { runId: string; contentRevision: string; nextFrame: string } };

// ── 几何 ──

export interface Shape {
  identity: string;
  kind: string;
  radius: number;
  halfLength: number;
  halfExtents: Vec3;
  vertices: Vec3[];
  parts: { shapeIndex: number; localPose: Pose }[];
}

export interface WorldCollider {
  identity: string;
  kind: string;
  pose: Pose;
  radius: number;
  halfLength: number;
  halfExtents: Vec3;
  isTrigger: boolean;
}

export interface Geometry {
  runId: string;
  contentRevision: string;
  world: WorldCollider[];
  worldTriangles: { a: Vec3; b: Vec3; c: Vec3 }[];
  actors: { actorId: string; radius: number; halfLength: number; centerOffset: Vec3 }[];
  shapes: Shape[];
  rawJson: string;
}

// ── 路线与节点图 ──

export interface ExecutionCall {
  actorId: string;
  programId: string;
  programRevision: string;
  invocationPath: string;
  parentInvocationPath: string;
  actionId: string;
  executionGeneration: string;
  invocationGeneration: string;
  parentInvocationGeneration: string;
  playbackId: string;
}

export interface NodeOccurrence {
  sequence: string;
  graphId: string;
  nodeId: string;
  kind: string;
  generation: string;
  result: string;
  call: ExecutionCall;
}

export interface EdgeOccurrence {
  sequence: string;
  graphId: string;
  edgeId: string;
  selected: boolean;
  passed: boolean;
  call: ExecutionCall;
}

export interface PortSample {
  sequence: string;
  graphId: string;
  nodeId: string;
  portId: string;
  direction: string;
  valueType: string;
  value: string;
  rawJson: string;
  call: ExecutionCall;
  sourceAvailable: boolean;
  reason: string;
}

export interface RoutePlayback {
  documentId: string;
  cursorSeconds: number;
  status: string;
}

export interface RouteFrame {
  hostFrame: string;
  seconds: number;
  playbacks: RoutePlayback[];
  active: AuthoringLocation[];
  nodes: NodeOccurrence[];
  edges: EdgeOccurrence[];
  ports: PortSample[];
  availability: Availability[];
}

export interface Route {
  runId: string;
  contentRevision: string;
  frames: RouteFrame[];
  protocol: string;
  availability: Availability[];
}

export interface RoutePage extends RangePage<RouteFrame> {
  runId: string;
  contentRevision: string;
  protocol: string;
  availability: Availability[];
  firstFrame: string;
  lastFrame: string;
}

export interface GraphEndpoint {
  graphId: string;
  nodeId: string;
  portId: string;
}

export interface GraphNode {
  id: string;
  displayName: string | null;
  invocationPath: string;
  position: { x: number; y: number } | null;
}

export interface GraphPort {
  nodeId: string;
  id: string;
  displayName: string;
  direction: string;
  isTrigger: boolean;
  invocationPath: string;
}

export interface GraphEdge {
  id: string;
  kind: string;
  invocationPath: string;
  source: GraphEndpoint | null;
  target: GraphEndpoint | null;
  available: boolean;
  reason: string;
}

export interface GraphDefinition {
  actorId: string;
  programId: string;
  programRevision: string;
  id: string;
  displayName: string | null;
  nodes: GraphNode[];
  ports: GraphPort[];
  edges: GraphEdge[];
  availability: Availability[];
}

export interface GraphPage extends RangePage<GraphDefinition> {
  runId: string;
  contentRevision: string;
  protocol: string;
  availability: Availability[];
}

export interface GraphQuery {
  rosterIndex?: number;
  graphId?: string;
  offset: number;
  limit: number;
}

// ── Timeline 元数据 ──

export interface AuthorClip {
  id: string;
  contract: string;
  displayName: string | null;
  startSeconds: number;
  endSeconds: number;
  graphId: string | null;
}

export interface AuthorTrack {
  id: string;
  displayName: string;
  muted: boolean;
  clips: AuthorClip[];
  markers: { id: string; seconds: number; graphId: string }[];
}

export interface AuthorTimeline {
  documentId: string;
  contentIdentity: string;
  contentRevision: string;
  displayName: string;
  tracks: AuthorTrack[];
}

export interface TimelinePage extends RangePage<{
  actorId: string;
  programId: string;
  programRevision: string;
  timeline: AuthorTimeline;
}> {
  protocol: string;
  runId: string;
  contentRevision: string;
  availability: Availability[];
}

export interface InputWrite {
  seconds: number;
  keyId: string;
  fields: Field[];
}

export interface FrozenInputTimeline {
  actorId: string;
  programId: string;
  programRevision: string;
  planId: string;
  planRevision: string;
  schemeIndex: number;
  displayName: string;
  timeOriginSeconds: number;
  tracks: {
    id: string;
    displayName: string;
    clips: { id: string; startSeconds: number; endSeconds: number; graphId: string; writes: InputWrite[] }[];
    markers: { id: string; seconds: number; graphId: string; writes: InputWrite[] }[];
  }[];
  scheduledInputs: { seconds: number; inputId: string; request: boolean; fields: Field[] }[];
  values: { inputId: string; startSeconds: number; endSeconds: number; fields: Field[] }[];
}

export interface InputTimeline {
  protocol: string;
  runId: string;
  contentRevision: string;
  availability: Availability[];
  roots: FrozenInputTimeline[];
}

// ── 比较 ──

export interface Comparison {
  comparable: boolean;
  equal: boolean;
  comparedFrames: number;
  channels: string[];
  firstDifference: { hostFrame: string; reason: string } | null;
  rawJson: string;
}

// ── 画面来源与播放器 ──

export interface ImageSourceAvailability {
  available: boolean;
  description: { displayName: string; frameDisplayName: string } | null;
}

export interface PlayerBuild {
  buildId: string;
  displayName: string;
  fields: Field[];
  rawJson: string;
}

export interface ImageMetadata {
  runId: string;
  hostFrame: string;
  contentRevision: string | null;
  alignment: string;
  fields: Field[];
  rawJson: string;
}

export type AlignmentState = 'matched' | 'mismatch' | 'pending' | 'no-dotnet-record' | 'live-input' | 'not-loaded';

export interface Alignment {
  state: AlignmentState;
  comparedFrames: number;
  firstMismatchFrame: number | null;
  lastPlayerFrame: number;
  changeKind: string | null;
  reason: string | null;
}

export type LiveProcessState = 'starting' | 'running' | 'exited' | (string & {});

export interface PlayerPreviewTarget {
  liveId: string;
  playerBuildId: string;
  scene: string;
  sceneRevision: string;
  source: RunSource | { kind: 'device'; lastSdkFrame?: number };
}

// `/api/preview/live` 的进程描述；hello 与 player 为播放器原样回报。
export interface LivePlayer {
  liveId: string;
  buildId: string;
  startedAt: string;
  state: LiveProcessState;
  exitCode: number | null;
  width: number;
  height: number;
  runId: string | null;
  hello: unknown;
  player: unknown;
  alignment: Alignment;
  previewTarget: PlayerPreviewTarget | null;
  viewers: number;
  latestVideoFrame: number | null;
}

export interface StartLivePlayerRequest {
  requestId: string;
  playerBuildId: string;
  width: number;
  height: number;
}

export type LiveCommandStage = 'sent' | 'submitted' | 'applied' | 'rejected' | 'not-applied';

export interface LiveCommandResult {
  stage: LiveCommandStage;
  commandId: string | null;
  ack: PlayerAck | null;
}

export type CameraCommand = {
  mode: 'orbit' | 'game';
  focusActorId?: string;
  reset?: boolean;
  yaw?: number;
  pitch?: number;
  zoom?: number;
  panX?: number;
  panY?: number;
};

export interface InputCommand {
  keys: string[];
  mouseDx: number;
  mouseDy: number;
  mouseButtons: number;
  scroll: number;
  x: number;
  y: number;
  lx: number;
  ly: number;
  rx: number;
  ry: number;
  lt: number;
  rt: number;
  padButtons: number;
}

export type LiveCommand =
  | { type: 'load'; commandId: string; runId: string }
  | { type: 'load'; commandId: string; scene: string; sceneRevision: string; inputMode: 'device' }
  | { type: 'play' | 'pause' | 'capture' | 'stop'; commandId: string }
  | { type: 'step'; commandId: string; count: number }
  | { type: 'seek'; commandId: string; frame: number }
  | { type: 'pick'; commandId: string; x: number; y: number }
  | ({ type: 'camera' } & CameraCommand)
  | ({ type: 'input' } & InputCommand)
  | { type: 'stream'; enabled: boolean; quality?: number; maxFps?: number };

export type PlayerState = 'idle' | 'loading' | 'paused' | 'playing' | 'stepping' | 'seeking' | 'failed';

export interface ViewerPlayer {
  state: PlayerState;
  error: string | null;
  hostFrame: string;
  lastFrame: string | null;
  inputMode: string | null;
  cameraMode: string | null;
  fields: Field[];
  rawJson: string;
}

export interface ViewerLive {
  liveId: string;
  buildId: string;
  state: LiveProcessState;
  exitCode: number | null;
  runId: string | null;
  alignment: Alignment;
  player: ViewerPlayer | null;
  rawJson: string;
}

export interface PlayerAck {
  type: 'ack';
  commandId: string;
  ok: boolean;
  error?: string;
  hostFrame?: string;
  path?: string;
  rawJson: string;
}

export interface PlayerPick {
  type: 'pick';
  commandId: string;
  ok: boolean;
  hostFrame?: string;
  actorId: string;
  position: Vec3;
  hitCollider?: string;
  rawJson: string;
}

export type ViewerMessage =
  | { type: 'status'; live: ViewerLive }
  | { type: 'frames'; alignment: Alignment; items: { hostFrame: string; consistency: Consistency }[]; rawJson: string }
  | PlayerAck
  | PlayerPick
  | { type: 'error'; error: string; displayName?: string; rawJson?: string }
  | { type: 'extension'; error: string | null; displayName: string; rawJson: string };

export interface VideoFrame {
  hostFrame: string;
  width: number;
  height: number;
  flipY: boolean;
  worldToClip: number[];
  mediaType: string;
  image: Blob;
}

export interface StartPlayerJobRequest {
  requestId: string;
  runId: string;
  playerBuildId: string;
  captureFrames: number[];
  width?: number;
  height?: number;
}

export interface PlayerJobReceipt {
  jobId: string;
  runId: string;
  playerBuildId: string;
  stage: string;
  acceptedAt: string;
  finishedAt: string | null;
  exitCode: number | null;
  error: string | null;
}

export interface PlayerJob {
  receipt: PlayerJobReceipt;
  player: unknown;
  alignment: { state: string; changeKind: string | null; reason: string | null } & Record<string, unknown>;
}

export interface PlayerCodeUpdate {
  state: 'created' | 'unchanged' | 'rejected';
  buildId: string;
  parentBuildId: string | null;
  changed?: unknown;
  refusals?: string[];
  sourceChanges?: unknown[];
  update?: unknown;
  compileMilliseconds?: number;
  totalMilliseconds?: number;
}

// ── 动画资源 ──

export interface AnimationClip {
  name: string;
  category: string;
  animationIndex: number;
  duration: number;
  exportedDuration: number;
  frameRate: number;
  unityAsset: string;
  provenance: { description: string };
  resource: { identity: string; guid: string; localFileId: string; contentRevision: string };
}

export interface AnimationLibrary {
  schema: string;
  complete: boolean;
  file: string;
  sha256: string;
  sourceSha256: string;
  clips: AnimationClip[];
}
