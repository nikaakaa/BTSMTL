import { numberOf, parseSkillJson, stringifySkillJson, type JsonObject, type JsonValue } from '../state/skillJson';
import type { GraphDefinition, LiveCommandStage, PlayerPreviewTarget, SourceFile } from './types';
import { getJson, getText, postJson, postText, putJson, query } from './http';
import { readPlayerCommandReceipt, readPlayerJobReceipt, type PlayerCommandReceipt, type PlayerJobReceipt, type StartPlayerJobRequest } from './playerRecords';

// 技能作者 HTTP 接口（宿主以 --SkillAuthoring 启用）：技能文件夹内全部 JSON 文件原文与内容修订。
// 文件文本原样返回，网页只在编辑的文件上解析、改写并整份写回。

export type SkillFile = {
  path: string;
  content: string;
};

export type SkillEditingView = {
  skills: Record<string, SkillSaveRequest['content']>;
  previewDefinitionGuid: string | null;
  scene?: Pick<SourceFile, 'path' | 'revision'>;
};

export interface SkillSnapshot {
  skill: string;
  revision: string;
  files: SkillFile[];
}

export type AuthoringTarget = { kind: 'skill'; name: string } | { kind: 'scenario'; name: string };
export const authoringTargetKey = (target: AuthoringTarget) => `${target.kind}/${target.name}`;

export interface SkillChanges {
  skill: string;
  revision: string;
  changed: boolean;
  files: { path: string; revision: string }[];
}

// 按需查询的统一外壳：availability 表示作者来源是否可读，items 为原分页结果。
export type QueryAvailability =
  | { available: true; reason: null }
  | { available: false; reason: string };

// 可创建的节点类型（原 Types）：按实际 C# 类型与原生节点声明发现。
export interface NodeTypeInfo {
  type: string;
  clrType: string | null;
  family: string;
  kind: string | null;
  nativeType: string | null;
  timelineDomains: string | null;
}

export interface TypePage {
  availability: QueryAvailability;
  hasMore: boolean;
  items: NodeTypeInfo[];
}

export interface DescribedPort {
  direction: 'input' | 'output';
  port: { ID: string; name: string; type: string; serializedValue: unknown };
}

export type DescribedPorts =
  | { availability: { available: true; reason: null }; hasMore: boolean; items: DescribedPort[]; errors?: never }
  | { availability: { available: false; reason: string }; errors: SkillFormatError[]; hasMore?: never; items?: never };

export const describedPortsAvailable = (ports: DescribedPorts): ports is Extract<DescribedPorts, { items: DescribedPort[] }> =>
  ports.availability.available;

// 字段的存储形状（C# 存储类型逐层展开）：集合给出 element，复合类型给出 members。
export interface StorageShape {
  clrType: string;
  hasAuthoringFields: boolean;
  defaultValue?: JsonValue;
  enumValues: string[] | null;
  elementType: string | null;
  element?: StorageShape;
  members?: MemberShape[];
}

export interface MemberShape extends StorageShape {
  member: string;
}

export interface FieldReference {
  kind: string;
  optional: boolean;
  objectType: string | null;
  errorMessage: string;
}

// 节点类的作者字段合同：显示名、值种类、所在存储路径、枚举与选择器、条件显示和引用声明。
export interface ProfileEntryTarget {
  kind: 'ProfileEntry';
  carrierIdentity: string;
  profileSource: string;
  domain: 'Vfx' | 'Camera' | 'Audio' | 'Input';
  entryKind: string;
  key: string;
  resourceIdentity: string | null;
}

export type ReferenceChoiceTarget = ProfileEntryTarget | { kind: 'Node'; graphId: string; nodeId: string };

export interface ReferenceChoice {
  value: JsonValue;
  name: string;
  detail: string;
  source: string;
  conflict: string | null;
  targets: ReferenceChoiceTarget[];
  associated: { fieldId: string; serializedMemberPath: string[]; value: JsonValue }[];
}

type FieldChoices =
  | { choiceKind: null; choicesAvailability: null; choices: null }
  | { choiceKind: string; choicesAvailability: QueryAvailability; choices: ReferenceChoice[] };

export type GraphReferenceTarget =
  | { role: string; ownership: 'PrivateOrShared' | 'Private' | 'AbilityLocal' | 'Shared'; ownerBinding: 'None' }
  | { role: string; ownership: 'Private'; ownerBinding: 'ReferenceNode' };

export interface AuthoringSubtype {
  identity: string;
  comment: string;
  clrType: string;
  defaultValue: JsonValue;
}

export type FieldMetadata = FieldChoices & {
  fieldId: string;
  displayName: string | null;
  type: string;
  clrType: string;
  valueKind: string;
  serializedMemberPath: string[];
  authoringVisible: boolean;
  readOnly: boolean;
  optional: boolean;
  allowedValues: string[] | null;
  pickerKind: string | null;
  pickerOwnerField: string | null;
  objectType: string | null;
  visibleWhenField: string | null;
  visibleWhenValue: string | null;
  references: FieldReference[];
  graphTargets: GraphReferenceTarget[];
  flowOutputList: boolean;
  selectedSubtype: string | null;
  subtypes: AuthoringSubtype[];
};

// 节点 JSON 的一个存储成员及落在其中的作者字段；computed 为由配置推出的只读字段。
export interface StoredField {
  member: string;
  value: JsonValue;
  shape: StorageShape | null;
  fields: FieldMetadata[];
  computed?: undefined;
}

export type ComputedField = FieldMetadata & {
  computed: true;
  value: JsonValue;
};

export type FieldItem = StoredField | ComputedField;

// 未保存节点的类型描述（原 DescribeType）：默认配置，本次配置下的字段与实际端口。
export interface TypeDescription {
  availability: QueryAvailability;
  sourceRevision: string;
  authoringRevision: string | null;
  type: string;
  configuration: JsonObject;
  fields: { hasMore: boolean; items: FieldItem[] };
  ports: DescribedPorts;
  references: { fieldId: string; serializedMemberPath: string[]; identity: string; owner: string; role: string; ownership: 'Private' | 'Shared' }[];
}

// 身份表（references.json）一项：作者 JSON 中引用的资产身份。
export interface IdentityItem {
  identity: string;
  asset: { AssetPath?: string };
  referenceCount: number;
}

interface ProjectItemBase {
  identity: string;
  name: string;
  owner: string;
  ownerName: string;
  file: string;
  ownership: 'Private' | 'Shared';
  referenceCount: number;
}

export type ProjectItem = ProjectItemBase & (
  | { kind: 'skill'; asset: JsonObject }
  | { kind: 'graph'; role: string }
  | { kind: 'timeline' }
  | { kind: 'input-plan'; schemes: { name: string; index: number }[] }
);

interface ReferenceLocation {
  owner: string;
  ownerName: string;
  file: string;
  path: string;
}

export interface SemanticReferenceLocation extends ReferenceLocation {
  contextOwner: string;
  contextOwnerName: string;
  graphId: string;
  nodeId: string;
  fieldId: string;
  serializedMemberPath: string[];
  value: JsonValue;
}

export type ProjectReferrer = (ReferenceLocation & { identity: string }) |
  (SemanticReferenceLocation & { targets: ReferenceChoiceTarget[] });

export type ReferenceSelector = { identity: string; target?: never } | { identity?: never; target: ReferenceChoiceTarget };

export type ReferenceQueryIssue = { code: string; reason: string } &
  ({ target: ReferenceChoiceTarget } | SemanticReferenceLocation);

export type ReferrersResult = {
  items: ProjectReferrer[];
  complete: boolean;
  unresolved: ReferenceQueryIssue[];
} & ({ identity: string; target: null } | { identity: null; target: ReferenceChoiceTarget });

export interface CarrierResource { key: string; name: string; identity: string }

export interface AuthoringCarrier {
  identity: string;
  name: string;
  source: string;
  preview: boolean;
  vfx: { source: string; resources: CarrierResource[]; anchors: { key: string; bone: string }[];
    stateModifiers: { id: string; resourceIds: string[] }[] } | null;
  camera: { source: string; sequences: CarrierResource[]; effects: { kind: string; resource: CarrierResource }[];
    targets: { key: string; space: string; anchorKey: string; aimPointKey: string; preferredBoneKey: string }[] } | null;
  audio: { source: string; events: CarrierResource[]; rules: CarrierResource[]; parameters: CarrierResource[];
    actionSoundSlotCount: number } | null;
  input: { source: string; keys: { name: string; kind: string; actionIdentity: string }[] } | null;
}

export interface AuthoringCarriers {
  availability: QueryAvailability;
  authoringRevision: string;
  items: AuthoringCarrier[];
  locateAvailability: QueryAvailability;
}

export interface BlackboardItem {
  identity: string;
  graphId: string;
  id: string;
  key: string;
  type: string;
  category: string;
}

// 图角色必须带的系统锚点（节点类型短名、稳定身份与默认坐标），与 Unity 作者工具同一份。
export interface GraphAnchor {
  kind: string;
  type: string;
  uid: string;
  // 按技能 JSON 规则解析：1.0 形式为浮点对象，用 numberOf 取值。
  x: JsonValue;
  y: JsonValue;
}

// 可新建元素的原 C# 默认值与存储形状；轨道、片段带 ContractKind。
export interface ElementTemplate {
  kind: string | null;
  clrType: string;
  value: JsonObject;
  shape: StorageShape;
}

// 轨道重叠策略（TimelineTrackOverlapPolicy）：禁止重叠、并行、重叠处混合。
export type TrackOverlap = 'Reject' | 'Parallel' | 'Blend';

// 轨道与片段带正式 Timeline 合同，与 Unity 合同目录同一份定义。
export interface TrackTemplate extends ElementTemplate {
  kind: string;
  contract: { overlap: TrackOverlap; domain: string; domains: string[]; clipKinds: string[] };
}

// 采样片段的曲线通道（TimelineSampledCurveChannel）：存储字段、显示名、角色、颜色、取值范围，
// 默认曲线是 (0, defaultStart) 到 (1, defaultEnd) 的直线。
export interface ClipCurveChannel {
  member: string; name: string; role: 'Weight' | 'EaseIn' | 'EaseOut'; color: string;
  min: number; max: number; defaultStart: number; defaultEnd: number;
}

export interface ClipTemplate extends ElementTemplate {
  kind: string;
  contract: { trackKind: string; domains: string[] };
  curves: ClipCurveChannel[];
  // 正式 ClipCapabilities：起止按逻辑 tick 量化；可设 ClipIn（素材时间按 ClipIn 映射）。
  tickQuantized: boolean;
  clipInable: boolean;
  sourceAsset: { serializedMemberPath: string[]; choiceKind: string } | null;
  // 引用源曲线资产的成员（有只读参考曲线的片段，如位移曲线的 m_SourceCurve）；没有为 null。
  referenceCurves: string | null;
}

// 片段参考曲线（queries/reference-curves，照 MotionCurveClip.ReferenceCurves）：源资产名、源时长与按源时间的曲线。
export interface ReferenceCurves {
  identity: string; label: string; duration: number;
  curves: { id: string; name: string; keys: { time: number; value: number; inTangent: number | string; outTangent: number | string }[] }[];
}

export interface SkillElements {
  graphs: GraphTemplate[];
  anchors: Record<string, GraphAnchor[]>;
  transition: ElementTemplate;
  timeline: ElementTemplate;
  tracks: TrackTemplate[];
  clips: ClipTemplate[];
  marker: ElementTemplate;
  referenceNodes: ReferenceNodeTemplate[];
  previewInput: { plan: ElementTemplate; scheme: ElementTemplate; nodeType: string; keyPath: string[];
    valueFields: Record<'Button' | 'Scalar' | 'Vector2', string> };
  // 黑板声明的存储形状：作用域、生命周期、分类与输入投影。
  blackboard: StorageShape;
}

export interface GraphTemplate {
  role: string;
  ownership: 'Private' | 'Shared' | null;
  value: JsonObject;
  layout: JsonObject;
  requiredContext: ('OwnerGraphId' | 'OwnerNodeId')[];
}

export interface ReferenceNodeTemplate {
  type: string;
  referenceKind: string;
  fieldId: string;
  serializedMemberPath: string[];
  pickerOwnerField: string | null;
  configuration: JsonObject;
}

interface Page<T> {
  hasMore: boolean;
  items: T[];
}

// 保存：宿主写入文件后用正式编译器编译本技能。成功为 200 与编译回执；
// 严格读取或编译拒绝为 422，文件仍已保存（saved=true），附原诊断。
export interface SkillCompilation {
  compilationKey: string;
  sourceRevision: string;
  artifactReceiptPath: string;
  reused: boolean;
}

export interface CompiledSkillSaveResult {
  saved: true;
  compilationState: 'succeeded';
  content: SkillSnapshot;
  compilation: SkillCompilation;
  preview: PlayerSkillRefreshReceipt | null;
  jobError: string | null;
}

export interface InputRevision { planId: string; path: string; revision: string }
export interface InputSaveResult {
  saved: true;
  compilationState: 'not-required';
  content: SkillSnapshot;
  inputs: InputRevision[];
  preview: PlayerCommandReceipt | null;
  job: PlayerJobReceipt | null;
}

export type SkillSaveResult = CompiledSkillSaveResult | InputSaveResult;

export type PlayerSkillRefreshTarget = Pick<StartPlayerJobRequest, 'scene' | 'sceneRevision' | 'source' | 'channels'> & {
  requestId: string;
  liveId: PlayerPreviewTarget['liveId'];
};

export interface SkillSaveRequest {
  content: { files: SkillFile[]; removedFiles: string[] };
  expectedRevision: string | null;
  preview: PlayerSkillRefreshTarget | null;
  previewDefinitionGuid?: string;
  scene?: SkillEditingView['scene'];
  job?: StartPlayerJobRequest;
}

interface SkillRefreshPackage {
  stage: 'offline-scene-written';
  scenePath: string;
  sceneRevision: string;
  skillIdentity: string;
  program: { programAssetId: string; graphId: string; programRevision: string; contentHash: string } | null;
  inputPlans: { id: string; path: string; revision: string; schemes: string[] }[];
}

interface SkillRefreshCommand {
  stage: LiveCommandStage;
  commandId: string | null;
  ack: unknown;
}

interface SkillRefreshIdentity {
  requestId: string;
  liveId: string;
  playerBuildId: string | null;
  sourceRevision: string;
  baseSceneRevision: string;
  source: PlayerSkillRefreshTarget['source'];
}

export type PlayerSkillRefreshReceipt = SkillRefreshIdentity & (
  | { stage: 'accepted'; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'submitted' }; error: null; statusPath: string }
  | { stage: 'adopted'; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'applied' }; error: null; statusPath: string }
  | { stage: 'failed' | 'unavailable'; package: SkillRefreshPackage | null; command: SkillRefreshCommand | null; error: string; statusPath: string | null }
);

export interface SkillFormatError {
  code: string;
  authoring?: string | null;
  skill?: string | null;
  type?: string | null;
  file?: string | null;
  graphId?: string | null;
  nodeId?: string | null;
  path?: string | null;
  message: string;
}

export type SkillSaveRejection = { saved: true; content: SkillSnapshot } & (
  | { stage: 'source-rejected'; compilationState: 'not-started' | 'not-required' } & (
    | { diagnostic: { message: string; errors: SkillFormatError[] }; error?: never }
    | { diagnostic?: never; error: string }
  )
  | { stage: 'compilation-failed'; compilationState: 'failed'; diagnostic?: never; exitCode: number; output: string; error: string; outputPath: string; errorPath: string }
);

export interface SkillDocumentGraph extends GraphDefinition {
  authoringPath: string;
  owner: string;
  ownerName: string;
  file: string;
  role: string;
  declaredOwner: { graphId: string; nodeId: string } | null;
}

export interface SkillDocumentRelation {
  source: {
    owner: string; ownerName: string; file: string; graphId: string; nodeId: string;
    fieldId: string; serializedMemberPath: string[];
  };
  target: {
    kind: 'graph'; id: string; displayName: string | null; role: string;
    owner: string; ownerName: string; file: string; ownership: 'Private' | 'Shared' | null;
  };
}

export interface SkillDocument {
  skill: string;
  programId: string | null;
  sourceRevision: string;
  tickRate: number;
  graphs: SkillDocumentGraph[];
  relations: SkillDocumentRelation[];
}

const folderPath = (kind: AuthoringTarget['kind']) => `/api/authoring/${kind === 'skill' ? 'skills' : 'scenarios'}`;
const targetPath = (target: AuthoringTarget) => `${folderPath(target.kind)}/${encodeURIComponent(target.name)}`;

export interface InputFragmentRequest {
  scene: string;
  sceneRevision: string;
  actorId: string;
  key: string;
  operation: 'hold' | 'press';
  startSeconds: number;
  endSeconds: number;
}

export interface InputFragment {
  track: JsonObject;
  clip: JsonObject;
  graph: JsonObject;
  tickRate: number;
  sceneRevision: string;
}

export interface ScenarioDraftRequest {
  scene: string;
  sceneRevision: string;
  actorId: string;
  schemeName: string;
  rangeEndSeconds: number;
}

export interface SkillCreationCatalog {
  stage: 'creation-catalog';
  projectPath: string;
  containers: { identity: string; name: string; assetPath: string }[];
  admissionProfiles: { identity: string; name: string; assetPath: string }[];
  references: { Assets: JsonObject; Values: JsonObject };
  skillCreation: { clrType: string; parameters: { name: string; shape: StorageShape }[] };
  availability: QueryAvailability;
}

export interface SkillCreationRequest {
  parameters: {
    containerIdentity: string;
    name: string;
    abilityId: string;
    displayName: string;
    admissionProfile: string;
    candidateOrder: number;
    sourceOrder: number;
    entryGraph: { name: string } | null;
    startupInputs: JsonObject[];
    trajectory: JsonObject | null;
  };
  references: SkillCreationCatalog['references'];
}

interface SkillCreationDraft {
  stage: 'draft';
  containerIdentity: string;
  files: SkillFile[];
  availability: QueryAvailability;
}

export const authoringApi = {
  carriers: (signal?: AbortSignal) => getJson<AuthoringCarriers>('/api/authoring/carriers', signal),
  skillCreationCatalog: (signal?: AbortSignal) => getJson<SkillCreationCatalog>('/api/authoring/skills/creation-options', signal),
  createSkillDraft: (request: SkillCreationRequest) => postJson<SkillCreationDraft>('/api/authoring/skills/draft-create', request),
  scenarioDraft: (name: string, request: ScenarioDraftRequest) =>
    postJson<{ files: SkillFile[] }>(`${targetPath({ kind: 'scenario', name })}/draft`, request),
  inputFragment: async (request: InputFragmentRequest) =>
    parseSkillJson(await postText('/api/authoring/input-fragments', request)) as unknown as InputFragment,
  list: (kind: AuthoringTarget['kind'], signal?: AbortSignal) => getJson<string[]>(folderPath(kind), signal),
  read: (target: AuthoringTarget, signal?: AbortSignal) => getJson<SkillSnapshot>(targetPath(target), signal),
  changes: (target: AuthoringTarget, revision: string, signal?: AbortSignal) =>
    getJson<SkillChanges>(`${targetPath(target)}/changes?revision=${encodeURIComponent(revision)}`, signal),
  document: (skill: string, editingView: SkillEditingView, signal?: AbortSignal, graphId?: string) =>
    postJson<SkillDocument>(`${targetPath({ kind: 'skill', name: skill })}/document`, { editingView, graphId }, signal),
  save: async (target: AuthoringTarget, request: SkillSaveRequest): Promise<SkillSaveResult> => {
    const result = await putJson<SkillSaveResult>(targetPath(target), request);
    return result.compilationState === 'not-required' ? { ...result,
      preview: result.preview === null ? null : readPlayerCommandReceipt(result.preview),
      job: result.job === null ? null : readPlayerJobReceipt(result.job),
    } : result;
  },
  refreshStatus: (statusPath: string, signal?: AbortSignal) => getJson<PlayerSkillRefreshReceipt>(statusPath, signal),
  types: (target: AuthoringTarget, offset: number, signal?: AbortSignal) =>
    getJson<TypePage>(`${targetPath(target)}/queries/types${query({ offset, limit: 128 })}`, signal),
  // 默认配置写进技能文件，按技能 JSON 规则解析以保留 1.0 这类浮点。
  describeType: async (target: AuthoringTarget, type: string, graphId: string, editingView: SkillEditingView, signal?: AbortSignal) =>
    readTypeDescription(await postText(`${targetPath(target)}/queries/type`, { type, graphId, offset: 0, limit: 128, editingView }, signal)),
  // 按当前未保存配置描述节点或普通作者值，保留技能 JSON 的数值格式。
  describeConfiguration: async (target: AuthoringTarget, type: string, configuration: JsonObject, graphId: string | null, editingView: SkillEditingView, signal?: AbortSignal) =>
    readTypeDescription(await postText(`${targetPath(target)}/queries/type`,
      stringifySkillJson({ type, configuration, graphId, offset: 0, limit: 128, editingView }, '\n'), signal)),
  identities: (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) =>
    allPages<IdentityItem>(`${targetPath(target)}/queries/references`, { editingView }, signal),
  blackboards: (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) =>
    allPages<BlackboardItem>(`${targetPath(target)}/queries/blackboards`, { editingView }, signal),
  project: (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) =>
    allPages<ProjectItem>(`${targetPath(target)}/queries/project`, { editingView }, signal),
  referrers: async (target: AuthoringTarget, selector: ReferenceSelector, editingView: SkillEditingView, signal?: AbortSignal): Promise<ReferrersResult> => {
    const items: ProjectReferrer[] = [];
    const unresolved = new Map<string, ReferenceQueryIssue>();
    let complete = true;
    for (let offset = 0; ; offset += 128) {
      const page = await postJson<ReferrersResult & { hasMore: boolean }>(`${targetPath(target)}/queries/referrers`,
        { ...selector, editingView, offset, limit: 128 }, signal);
      items.push(...page.items);
      for (const issue of page.unresolved) unresolved.set(JSON.stringify(issue), issue);
      complete = complete && page.complete;
      if (!page.hasMore) return { ...page, items, complete, unresolved: [...unresolved.values()] };
    }
  },
  referenceCurves: (target: AuthoringTarget, identity: string, editingView: SkillEditingView, signal?: AbortSignal) =>
    postJson<ReferenceCurves>(`${targetPath(target)}/queries/reference-curves`, { identity, editingView }, signal),
  // 模板值保留浮点写法以便原样写回；曲线通道的范围与默认值只是查询元数据，读成普通数字。
  elements: async (target: AuthoringTarget, signal?: AbortSignal) => {
    const elements = parseSkillJson(await getText(`${targetPath(target)}/queries/elements`, signal)) as unknown as SkillElements;
    for (const clip of elements.clips)
      clip.curves = clip.curves.map(channel => ({
        ...channel, min: numberOf(channel.min), max: numberOf(channel.max),
        defaultStart: numberOf(channel.defaultStart), defaultEnd: numberOf(channel.defaultEnd),
      }));
    return elements;
  },
};

function readTypeDescription(text: string): TypeDescription {
  const description = parseSkillJson(text) as unknown as TypeDescription;
  const fields = description.fields.items.flatMap(item => item.computed ? [item] : item.fields);
  const valueOf = (value: JsonValue, clrType: string) =>
    typeof value === 'string' && (clrType === 'System.Int64' || clrType === 'System.UInt64') ? parseSkillJson(value) : value;
  for (const field of fields) {
    if (field.choices === null) continue;
    field.choices = field.choices.map(choice => ({ ...choice, value: valueOf(choice.value, field.clrType),
      associated: choice.associated.map(associated => ({ ...associated,
        value: valueOf(associated.value, fields.find(target => target.fieldId === associated.fieldId)!.clrType),
      })),
    }));
  }
  return description;
}

async function allPages<T>(path: string, input: { editingView: SkillEditingView }, signal?: AbortSignal): Promise<T[]> {
  const items: T[] = [];
  for (let offset = 0; ; offset += 128) {
    const page = await postJson<Page<T>>(path, { ...input, offset, limit: 128 }, signal);
    items.push(...page.items);
    if (!page.hasMore) return items;
  }
}
