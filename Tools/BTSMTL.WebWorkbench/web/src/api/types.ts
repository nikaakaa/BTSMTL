// 宿主响应类型。64 位整数、帧号、修订与 FP 原值按宿主原样保留为 string。
import type { ExecutionCall, PlayerSceneContent, PlayerTimelineContent, PreviewPosition, RouteFrame as PlayerRouteFrame,
  PreviewSampleSource, RecordedAvailability, readPlayerAck, readPlayerCommandResult, readPlayerHost, readViewerMessage, readViewerStatus } from './playerRecords';
import type { readPublishedTimelineScene, readQuickScenes } from './previewQuickRecords';

export interface Field {
  displayName: string;
  value: string;
}

export type Availability = RecordedAvailability | Omit<RecordedAvailability, 'coverage'>;

export interface Page<T> {
  items: T[];
  total: number;
  nextOffset: number | null;
}

export interface RangePage<T> extends Page<T> {
  offset: number;
}

// ── 宿主声明 ──

export type HostAvailability = ReturnType<typeof readPlayerHost>;
export type HostDescription = NonNullable<HostAvailability['description']>;
export type PreviewMode = HostDescription['modes'][number];
export type QueryField = HostDescription['filters'][number];
export type NumberInput = HostDescription['inputLimit'];

// ── 来源与输入 ──

export type RunSource = Exclude<PlayerSceneContent['source'], { kind: 'device' }>;

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
  quickScenes: ReturnType<typeof readQuickScenes>;
  timelines: TimelineSource[];
  replays: ReplayChoice[];
  sceneDirectory: string;
  replayDirectory: string;
  runDirectory: string;
}

export interface TimelineSource {
  content: PlayerTimelineContent;
  contentIdentity: string;
  contentRevision: string;
  bindingRevision: string;
  tickRate: number;
  scene: ReturnType<typeof readPublishedTimelineScene>;
  bindings: unknown;
}

export type { PlayerInputCatalog as InputCatalog } from './playerRecords';

// ── 帧记录 ──

export interface AuthoringLocation {
  displayName: string;
  documentId: string;
  elementId: string;
  call: ExecutionCall | null;
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

export type FrameGeometry = import('./playerRecords').PlayerFramesPage['items'][number]['geometry'];

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
  source: import('./playerRecords').PlayerFramesPage['items'][number]['source'];
  availability: Availability[];
}

export interface ObservationQuery {
  firstFrame: string;
  lastFrame: string;
}

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

export type { ExecutionCall, LoopScope } from './playerRecords';
export type RouteFrame = PlayerRouteFrame;
export type NodeOccurrence = RouteFrame['nodes'][number];
export type EdgeOccurrence = RouteFrame['edges'][number];
export type PortSample = RouteFrame['ports'][number];
export type RoutePlayback = RouteFrame['playbacks'][number];

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
  // 自身淡入淡出时长；只在打开的技能 JSON 中给出，运行回执没有。
  easeInSeconds?: number;
  easeOutSeconds?: number;
}

export interface AuthorTrack {
  id: string;
  // 轨道合同种类（ContractKind）。
  contract: string;
  displayName: string;
  muted: boolean;
  clips: AuthorClip[];
  markers: { id: string; seconds: number; graphId: string }[];
}

export interface AuthorTimeline {
  documentId: string;
  contentIdentity: string;
  contentRevision: string | null;
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

export type LiveProcessState = 'starting' | 'running' | 'exited' | (string & {});

export type { LivePlayer, PlayerPreviewTarget } from './playerRecords';

export interface StartLivePlayerRequest {
  requestId: string;
  playerBuildId: string;
  width: number;
  height: number;
}

export type LiveCommandResult = ReturnType<typeof readPlayerCommandResult>;
export type LiveCommandStage = LiveCommandResult['stage'];

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
  | { type: 'play' | 'pause' | 'capture' | 'stop'; commandId: string }
  | { type: 'step'; commandId: string; count: number }
  | { type: 'seek'; commandId: string; position: PreviewPosition }
  | { type: 'pick'; commandId: string; x: number; y: number }
  | ({ type: 'camera' } & CameraCommand)
  | ({ type: 'input' } & InputCommand)
  | { type: 'stream'; enabled: boolean; quality?: number; maxFps?: number };

export type PlayerState = 'idle' | 'loading' | 'paused' | 'playing' | 'stepping' | 'seeking' | 'failed';

export type ViewerLive = ReturnType<typeof readViewerStatus>['live'];
export type ViewerPlayer = NonNullable<ViewerLive['player']>;

export type PlayerAck = ReturnType<typeof readPlayerAck>;

export interface PlayerPick {
  type: 'pick';
  commandId: string;
  ok: boolean;
  hostFrame?: string | null;
  actorId: string;
  position: Vec3;
  hitCollider?: string;
  rawJson: string;
}

export type ViewerMessage = ReturnType<typeof readViewerMessage>;

export interface VideoFrame {
  hostFrame: string;
  source: PreviewSampleSource;
  width: number;
  height: number;
  flipY: boolean;
  worldToClip: number[];
  mediaType: string;
  image: Blob;
}

export type { PlayerJob, PlayerJobReceipt, StartPlayerJobRequest } from './playerRecords';

export type PlayerCodeUpdate = {
  buildId: string;
  changed?: unknown;
  sourceChanges?: unknown[];
  update?: unknown;
  compileMilliseconds?: number;
  totalMilliseconds?: number;
} & (
  | { state: 'created'; parentBuildId: string }
  | { state: 'unchanged'; parentBuildId: null }
  | { state: 'rejected'; parentBuildId: null; refusals: string[] }
);

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

export type { PlayerPoseLayout as ArchivePoseLayout } from './playerRecords';
export type ArchivePose = import('./playerRecords').PlayerPresentationPage['page']['items'][number]['actors'][number]['worldRoot'];
export type ArchiveLogicRecord = import('./playerRecords').PlayerLogicPage['page']['items'][number];
export type ArchivePresentationRecord = import('./playerRecords').PlayerPresentationPage['page']['items'][number];
