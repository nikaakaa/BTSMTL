import { numberOf, parseSkillJson, stringifySkillJson, type JsonObject, type JsonValue } from '../state/skillJson';
import type { GraphDefinition, LiveCommandResult, PlayerPreviewTarget } from './types';
import { deleteJson, getJson, postJson, postText, query } from './http';
import { readAuthoringAvailability, type AuthoringAvailability } from './authoringAvailability';
import { readVariablesValue, type VariablesValue } from './variables';
import { readWorkspaceDocumentSnapshot, workspaceDocumentKinds, type WorkspaceDocumentInfo, type WorkspaceDocumentSnapshot, type WorkspaceEditingView } from './workspaceDocuments';
import { readExecutionScopeSelection, readObjectSelectionMetadata, readResourceReferenceMetadata,
  type ResourceObjectCapability, type ResourceReferenceMetadata } from './resourceRegistration';
import { array, boolean, choice, hosted, nullable, number, object, opaque, optional, readPlayerCommandResult, readPlayerJobReceipt, readPlayerRunSource, text,
  type StartPlayerJobRequest } from './playerRecords';

// 技能作者 HTTP 接口（宿主以 --SkillAuthoring 启用）：技能文件夹内全部 JSON 文件原文与内容修订。
// 文件文本原样返回，网页只在编辑的文件上解析、改写并整份写回。

export type SkillFile = {
  path: string;
  content: string;
};

export type SkillEditingView = WorkspaceEditingView;

const readSkillFile = object({ path: text, content: text });
const readCreatedVariables = nullable(readVariablesValue);
const readSkillSnapshotValue = object({ skill: text, revision: text,
  files: array(readSkillFile), sourceFiles: array(readSkillFile) });
const readSkillSnapshot = (value: unknown, path?: string) => {
  const snapshot = readSkillSnapshotValue(value, path);
  return { ...snapshot, files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])),
    sourceFiles: Object.fromEntries(snapshot.sourceFiles.map(file => [file.path, file.content])) };
};
export type SkillSnapshot = ReturnType<typeof readSkillSnapshot>;

export type AuthoringTarget = Pick<WorkspaceDocumentInfo, 'kind' | 'documentPath'>;
const authoringDirectories = { skill: 'Skills', timeline: 'Timelines', curve: 'Curves', profile: 'Profiles', scenario: 'Scenarios', variables: 'Variables', graph: 'Graphs' };
export const authoringRootFiles = { skill: 'skill.json', timeline: 'timeline.json', curve: 'curve.json', profile: 'profile.json', scenario: null, variables: 'variables.json', graph: 'graph.json' };
export const authoringTarget = (kind: AuthoringTarget['kind'], name: string): AuthoringTarget => ({ kind, documentPath: `${authoringDirectories[kind]}/${name}` });
export function authoringOwner(documentPath: string): AuthoringTarget {
  const kind = workspaceDocumentKinds.find(kind => documentPath.startsWith(`${authoringDirectories[kind]}/`));
  if (kind === undefined) throw new Error(`作者文档路径没有正式工作区类别：${documentPath}`);
  return { kind, documentPath };
}
export const authoringTargetKey = (target: AuthoringTarget) => target.documentPath;
export const authoringDirectory = (target: AuthoringTarget) => target.documentPath.slice(authoringDirectories[target.kind].length + 1);

// 按需查询的统一外壳：availability 表示作者来源是否可读，items 为原分页结果。
export type QueryAvailability = AuthoringAvailability;

const readAdapterCatalogDomain = object({ adapter: text, domain: text, items: array(object({
  value: text, display: text, context: text, revision: text, data: opaque,
})) });
const readAdapterCatalogResponse = object({ revision: text, changed: boolean, domains: nullable(array(readAdapterCatalogDomain)) });
export type AdapterCatalogDomain = ReturnType<typeof readAdapterCatalogDomain>;
export type AdapterCatalogItem = Pick<AdapterCatalogDomain, 'adapter' | 'domain'> & AdapterCatalogDomain['items'][number];
type AdapterCatalogResponse = { revision: string } & ({ changed: true; domains: AdapterCatalogDomain[] } | { changed: false; domains: null });
export function readAdapterCatalog(value: unknown): AdapterCatalogResponse {
  const response = readAdapterCatalogResponse(value, 'AdapterCatalog');
  if (response.changed && response.domains !== null) return { ...response, changed: true, domains: response.domains };
  if (!response.changed && response.domains === null) return { ...response, changed: false, domains: response.domains };
  throw new Error('AdapterCatalog 的修订变化状态与目录正文不符合正式合同');
}

const readReferenceReplacementPlan = object({
  revision: text, domain: text, oldValue: text, newValue: text, context: nullable(text),
  files: array(object({ path: text, members: array(text), readRevision: text, writeRevision: text })),
  readFiles: array(object({ path: text, revision: text })), affectedSkills: array(text),
  runtimeState: choice('pending', 'not-required'),
});
export type ReferenceReplacementResult = ReturnType<typeof readReferenceReplacementPlan> & { success: true } & (
  { saved: false; authorReferenceState: 'prepared' } | { saved: true; authorReferenceState: 'replaced' }
);
export type ReferenceReplacementPrepared = Extract<ReferenceReplacementResult, { saved: false }>;
export type ReferenceReplacementCompleted = Extract<ReferenceReplacementResult, { saved: true }>;
export type ReferenceReplacementInput = Pick<ReferenceReplacementResult, 'domain' | 'oldValue' | 'newValue' | 'context'>;

export function readReferenceReplacement(value: unknown, action: 'prepare'): ReferenceReplacementPrepared;
export function readReferenceReplacement(value: unknown, action: 'commit'): ReferenceReplacementCompleted;
export function readReferenceReplacement(value: unknown, action: 'prepare' | 'commit'): ReferenceReplacementResult {
  const path = 'ReferenceReplacement';
  const plan = readReferenceReplacementPlan(value, path);
  const state = object({ success: readRequiredTrue, saved: boolean, authorReferenceState: choice('prepared', 'replaced') })(value, path);
  if (action === 'prepare' && !state.saved && state.authorReferenceState === 'prepared')
    return { ...plan, success: state.success, saved: state.saved, authorReferenceState: state.authorReferenceState };
  if (action === 'commit' && state.saved && state.authorReferenceState === 'replaced')
    return { ...plan, success: state.success, saved: state.saved, authorReferenceState: state.authorReferenceState };
  throw new Error(`${path}：${action} 的保存状态与作者引用阶段不一致`);
}

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
  | { availability: Extract<QueryAvailability, { available: true }>; hasMore: boolean; items: DescribedPort[]; errors?: never }
  | { availability: Extract<QueryAvailability, { available: false }>; errors: SkillFormatError[]; hasMore?: never; items?: never };

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

// 所属类型作者合同中对应该存储成员的字段：没有字段的成员不是作者内容；带图引用目标的成员是专属图槽位，
// 由图引用行新建、进入与移除。
export interface MemberAuthoring {
  fieldId: string;
  displayName: string | null;
  authoringVisible: boolean;
  readOnly: boolean;
  optional: boolean;
  allowedValues: string[] | null;
  graphTargets: GraphReferenceTarget[];
}

export interface MemberShape extends StorageShape {
  member: string;
  authoring?: MemberAuthoring | null;
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

const readRequiredTrue = (value: unknown, path = 'Authoring'): true => {
  if (value !== true) throw new Error(`${path} 需要 true`);
  return value;
};
const readResourceTarget = object({ kind: choice('Resource'), domain: text, value: text, context: nullable(text) });
export type ResourceTarget = ReturnType<typeof readResourceTarget>;
export type ReferenceChoiceTarget = ProfileEntryTarget | ResourceTarget | { kind: 'Node'; graphId: string; nodeId: string };

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

// 图引用目标：角色、归属、所有者绑定，以及从引用处新建私有图时的初始内容（宿主图模板按同一 seed 给出）。
export type GraphSeed = 'Anchors' | 'ConditionPasses';

export type GraphReferenceTarget =
  | { role: string; ownership: 'PrivateOrShared' | 'Private' | 'AbilityLocal' | 'Shared'; ownerBinding: 'None'; seed: GraphSeed }
  | { role: string; ownership: 'Private'; ownerBinding: 'ReferenceNode'; seed: GraphSeed };

export interface AuthoringSubtype {
  identity: string;
  comment: string;
  clrType: string;
  defaultValue: JsonValue;
}

export type FieldMetadata = FieldChoices & {
  resourceReference: ResourceReferenceMetadata | null;
  objectSelection: ResourceObjectCapability | null;
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
  choicesTarget: 'element' | 'value' | null;
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
  inputKind: string | null;
  availability: QueryAvailability;
  sourceRevision: string;
  authoringRevision: string | null;
  type: string;
  configuration: JsonObject;
  fields: { hasMore: boolean; items: FieldItem[] };
  ports: DescribedPorts;
  references: { fieldId: string; serializedMemberPath: string[]; identity: string; owner: string; role: string; ownership: 'Private' | 'Shared' }[];
}

// 技能存储值中出现且在作者包绑定中有条目的业务键。
export interface IdentityItem {
  identity: string;
  kind: string;
  binding: JsonObject;
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
  | { kind: 'graph'; contentDomain: string; domains: string[]; variableOwnerId: string | null }
  | { kind: 'timeline' }
  | { kind: 'curve' | 'profile' | 'variables'; asset: JsonObject }
  | { kind: 'input-plan'; schemes: { name: string; index: number }[] }
);

export interface AuthoringProject extends Page<ProjectItem> {
  snapshot: string;
  catalogRevision: string;
  resourceCatalogRevision: string;
}

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

export type SkillDeletionReference = ReferenceLocation & {
  identity: string;
} & ({ kind: 'authoring'; view: 'saved' | 'editing' } | { kind: 'carrier-grant'; view: 'saved' });

export type SkillDeleteResult = {
  success: true; deleted: true; skill: string; revision: string; authoringRevision: string;
} | {
  success: false; deleted: false; code: 'skill-referenced'; skill: string; revision: string; authoringRevision: string;
  references: SkillDeletionReference[]; shared: ProjectItem[]; message: string;
} | {
  success: false; deleted: false; code: 'source-revision-conflict'; skill: string; message: string;
};

export type ReferenceSelector = { identity: string; target?: never } | { identity?: never; target: ReferenceChoiceTarget };

export type ReferenceQueryIssue = { code: string; reason: string } &
  ({ target: ReferenceChoiceTarget } | SemanticReferenceLocation);

export type ReferrersResult = {
  items: ProjectReferrer[];
  complete: boolean;
  unresolved: ReferenceQueryIssue[];
} & ({ identity: string; target: null } | { identity: null; target: ReferenceChoiceTarget });

const readAuthoringProducts = object({ items: array(object({ value: text, name: text, source: text, revision: text })) });
export type AuthoringProduct = ReturnType<typeof readAuthoringProducts>['items'][number];

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
  // 由时间轴结构本身编辑的成员（TimelineJson 声明），不作为普通字段编辑。
  structureMembers: string[];
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
  structureMembers: string[];
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

export type InputPlanContext = { kind: 'skill' } | { kind: 'timeline'; timeline: string }
  | { kind: 'scene'; scene: string; actor: string };

export interface SkillElements {
  graphs: GraphTemplate[];
  anchors: Record<string, GraphAnchor[]>;
  // 状态机里新建的普通状态：节点类型、默认名称与默认配置；必填图引用成员随状态一并新建。
  state: ElementTemplate & { type: string; name: string };
  transition: ElementTemplate;
  // 带执行出口列表的节点：列表成员与元素形状；元素的图引用成员是从该出口连线编辑的专属图。
  flowOutputs: { type: string; member: string; element: StorageShape }[];
  timeline: ElementTemplate;
  tracks: TrackTemplate[];
  clips: ClipTemplate[];
  marker: ElementTemplate;
  referenceNodes: ReferenceNodeTemplate[];
  previewInput: { plan: ElementTemplate | null; availability: { available: boolean; reason: string | null };
    scheme: ElementTemplate; nodeType: string; keyPath: string[];
    valueFields: Record<'Button' | 'Scalar' | 'Vector2', string> };
  // 黑板声明的存储形状：作用域、生命周期、分类与输入投影。
  blackboard: StorageShape;
}

export interface GraphTemplate {
  role: string;
  seed: GraphSeed;
  ownership: 'Private' | 'Shared' | null;
  value: JsonObject;
  variables: VariablesValue | null;
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
const graphCompilationWork = choice('Unchanged', 'Sources', 'Parameters', 'Structure');
const graphCompilation = object({ work: nullable(graphCompilationWork), milliseconds: object({ frontend: number, fpAndHash: number }) });
const graphIdentities = { structureIdentity: text, parameterIdentity: text, targetParameterIdentity: text,
  sourceIdentity: text, stateLayoutIdentity: text, executionIdentity: text, contentIdentity: text };
const preparationPhases = array(object({ stage: text, milliseconds: number }));
const graphPublication = (value: unknown, path = 'Publication.graph') => typeof value === 'object' && value !== null && 'stage' in value
  ? object({ stage: choice('prepared-by-earlier-skill') })(value, path)
  : object({ work: graphCompilationWork, milliseconds: object({ encoding: number, total: number }) })(value, path);
const publicationResult = object({
  stage: text, root: text, ability: text, sourceRevision: text, entryGraphId: text,
  graphs: array(object({
    graphId: text, dependencies: array(opaque), operations: number, sources: number, encodingContract: text, nodeContract: text,
    numericTarget: text, numericProfile: opaque, ...graphIdentities, compilation: graphCompilation,
    publication: graphPublication,
  })),
  output: text, publicationOutput: text, publicationHash: text, publicationEncoding: text,
  files: array(object({ graphId: nullable(text), kind: text, path: text, stage: text })),
  spawns: array(opaque), carrierDefinitions: array(opaque), timelineIds: array(text), scalarConversions: array(opaque),
  scalarRoundingMessage: nullable(text), diagnostics: array(opaque), editorInstallation: text, contentLoad: text, sessionAdoption: text,
  milliseconds: object({ read: number, compile: number, prepare: number }), success: optional(boolean),
  receiptFile: optional(object({ path: text, stage: text })),
  transaction: optional(object({ scope: text, requestCount: number, writtenFileCount: number,
    milliseconds: object({ prepare: number, commit: number }) })),
});
const unityPublication = hosted({
  stage: choice('unity-assets-committed'), success: boolean, projectPath: text, requests: array(opaque), graphCount: number,
  graphs: array(hosted({ graphId: text, assetPath: text, assetGuid: text, sourceRevision: text, numericTarget: text,
    ...graphIdentities, previousContentIdentity: nullable(text), compilation: graphCompilation,
    publication: object({ work: graphCompilationWork,
      files: array(object({ kind: text, identity: text, assetReference: text, path: nullable(text), stage: choice('written', 'unchanged') })),
      milliseconds: object({ read: number, encodingAndHash: number, prepare: number, total: number }),
    }),
  })),
  preparedFiles: array(text), writtenFiles: array(text), receiptFile: text,
  editorImport: choice('not-performed'), sessionAdoption: choice('not-performed'),
  milliseconds: object({ prepare: number, commit: number, total: number, encodingAndHash: number }),
});
const readSkillCompilation = object({
  compilationKey: text, sourceRevision: text, artifactReceiptPath: text, artifactReused: boolean,
  publicationResult: nullable(publicationResult), unityPublication: nullable(unityPublication), phases: preparationPhases,
});
export type SkillCompilation = ReturnType<typeof readSkillCompilation>;

const readSkillExportHeader = object({
  stage: choice('export-ready'), kind: choice('authoring', 'runtime'), sourceRevision: text,
  product: nullable(object({ value: text, revision: text })), execution: nullable(readExecutionScopeSelection),
  tickRate: number, effectProfileGuid: text, fileName: text, downloadUrl: text,
  files: array(object({ path: text, downloadUrl: text })),
});
const readNoExportCompilation = (value: unknown, path = 'SkillExport.compilation'): null => {
  if (value !== null) throw new Error(`${path}：作者内容导出不包含运行编译结果`);
  return null;
};
function readSkillExport(value: unknown) {
  const header = readSkillExportHeader(value, 'SkillExport');
  switch (header.kind) {
    case 'authoring': return { ...header, kind: header.kind,
      ...object({ compilation: readNoExportCompilation })(value, 'SkillExport') };
    case 'runtime': return { ...header, kind: header.kind,
      ...object({ compilation: readSkillCompilation })(value, 'SkillExport') };
  }
}
export type SkillExport = ReturnType<typeof readSkillExport>;
export type SkillExportKind = SkillExport['kind'];

export interface CompiledSkillSaveResult {
  saved: true;
  compilationState: 'succeeded';
  content: SkillSnapshot;
  documents: WorkspaceDocumentSnapshot[];
  compilation: SkillCompilation;
  preview: PlayerSkillRefreshReceipt | null;
  jobError: string | null;
}

export interface InputRevision { planId: string; path: string; revision: string }
const readSaved = (value: unknown, path = 'SkillSave.saved'): true => {
  if (value !== true) throw new Error(`${path} 需要 true`);
  return value;
};
const savedContentFields = { saved: readSaved, content: readSkillSnapshot, documents: array(readWorkspaceDocumentSnapshot) };
const readInputSaveResult = object({
  ...savedContentFields, compilationState: choice('not-required'),
  inputs: array(object({ planId: text, path: text, revision: text })),
  preview: nullable(readPlayerCommandResult), job: nullable(readPlayerJobReceipt),
  previewError: nullable(text), jobError: nullable(text),
});
export type InputSaveResult = ReturnType<typeof readInputSaveResult>;

const readNull = (value: unknown, path = 'Authoring'): null => {
  if (value !== null) throw new Error(`${path} 需要 null`);
  return null;
};
export type SkillSaveResult = CompiledSkillSaveResult | InputSaveResult;

export type PlayerSkillRefreshTarget = Pick<StartPlayerJobRequest, 'scene' | 'sceneRevision' | 'source' | 'channels'> & {
  requestId: string;
  liveId: PlayerPreviewTarget['liveId'];
};

export interface SkillSaveRequest {
  editingView: SkillEditingView;
  preview: PlayerSkillRefreshTarget | null;
  job?: StartPlayerJobRequest;
}

const readPreviewInputPlan = object({ id: text, path: text, revision: text, schemes: array(text) });
const readPreviewAuthorTimeline = object({
  documentId: text, contentIdentity: text, contentRevision: text, displayName: text,
  tracks: array(object({ id: text, displayName: text, muted: boolean,
    clips: array(object({ id: text, contract: text, displayName: nullable(text),
      startSeconds: number, endSeconds: number, graphId: nullable(text) })),
    markers: array(object({ id: text, seconds: number, graphId: text })),
  })),
});
const readPreviewAuthorProgram = object({
  ownerKind: text, ownerId: text, programIndex: number, programAssetId: text, programId: text,
  programRevision: text, contentHash: text, layoutRevision: nullable(text),
  graphs: array(object({ id: text, displayName: nullable(text), role: text,
    nodes: array(object({ id: text, displayName: nullable(text), type: text,
      position: nullable(object({ x: number, y: number })) })),
    ports: array(object({ nodeId: text, id: text, displayName: text, direction: text, type: text })),
    edges: array(object({ id: text, sourceNodeId: text, sourcePortId: nullable(text), targetNodeId: text, targetPortId: nullable(text) })),
  })),
  timelines: array(readPreviewAuthorTimeline), inputPlans: array(readPreviewInputPlan),
});
const readSkillRefreshPackage = object({
  stage: choice('offline-scene-written'), scenePath: text, sceneRevision: text, skillIdentity: text,
  program: nullable(object({ programAssetId: text, graphId: text, programRevision: text, contentHash: text })),
  inputPlans: array(readPreviewInputPlan), programs: array(readPreviewAuthorProgram),
});
type SkillRefreshPackage = ReturnType<typeof readSkillRefreshPackage>;
export type PreviewAuthorProgram = ReturnType<typeof readPreviewAuthorProgram>;

type SkillRefreshCommand = LiveCommandResult;

interface SkillRefreshIdentity {
  requestId: string;
  liveId: string;
  playerBuildId: string | null;
  sourceRevision: string;
  baseSceneRevision: string;
  source: PlayerSkillRefreshTarget['source'];
  phases: ReturnType<typeof preparationPhases>;
}

export type PlayerSkillRefreshReceipt = SkillRefreshIdentity & (
  | { stage: 'accepted'; adoptedSceneRevision: null; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'submitted' }; error: null; statusPath: string }
  | { stage: 'adopted'; adoptedSceneRevision: string; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'applied' }; error: null; statusPath: string }
  | { stage: 'failed' | 'unavailable'; adoptedSceneRevision: null; package: SkillRefreshPackage | null; command: SkillRefreshCommand | null; error: string; statusPath: string | null }
);

function readSkillRefreshReceipt(value: unknown, path = 'SkillRefresh'): PlayerSkillRefreshReceipt {
  const receipt = object({ requestId: text, liveId: text, playerBuildId: nullable(text), sourceRevision: text,
    baseSceneRevision: text, source: readPlayerRunSource, phases: preparationPhases, adoptedSceneRevision: nullable(text),
    stage: choice('accepted', 'adopted', 'failed', 'unavailable'), package: opaque, command: nullable(readPlayerCommandResult) })(value, path);
  if (receipt.stage !== 'adopted' && receipt.adoptedSceneRevision !== null)
    throw new Error('SkillRefresh 尚未采用时不能提供已采用场景修订');
  const command = receipt.command;
  switch (receipt.stage) {
    case 'accepted':
      if (command?.stage !== 'submitted') throw new Error('SkillRefresh.accepted.command.stage 需要 submitted');
      return { ...receipt, ...object({ error: readNull, statusPath: text })(value, path), stage: receipt.stage, adoptedSceneRevision: null,
        package: readSkillRefreshPackage(receipt.package, `${path}.package`),
        command: { ...command, stage: command.stage } };
    case 'adopted': {
      if (receipt.adoptedSceneRevision === null) throw new Error('SkillRefresh.adopted 缺少实际采用场景修订');
      if (command?.stage !== 'applied') throw new Error('SkillRefresh.adopted.command.stage 需要 applied');
      const prepared = readSkillRefreshPackage(receipt.package, `${path}.package`);
      if (command.content?.revision !== prepared.sceneRevision)
        throw new Error('SkillRefresh.adopted.command.content.revision 与 package.sceneRevision 不一致');
      return { ...receipt, ...object({ error: readNull, statusPath: text })(value, path), stage: receipt.stage,
        adoptedSceneRevision: receipt.adoptedSceneRevision, package: prepared,
        command: { ...command, stage: command.stage } };
    }
    case 'failed':
    case 'unavailable': return { ...receipt, ...object({ error: text, statusPath: nullable(text) })(value, path),
      stage: receipt.stage, adoptedSceneRevision: null, command,
      package: receipt.package === null ? null : readSkillRefreshPackage(receipt.package, `${path}.package`) };
  }
}

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

const readSkillFormatError = object({
  code: text, message: text, authoring: optional(nullable(text)), skill: optional(nullable(text)),
  type: optional(nullable(text)), file: optional(nullable(text)), graphId: optional(nullable(text)),
  nodeId: optional(nullable(text)), path: optional(nullable(text)),
});
const readSavedContent = object(savedContentFields);
const readCompilerFailure = object({ exitCode: number, output: text, error: text, outputPath: text, errorPath: text });

function readSaveReceipt(value: unknown) {
  const receipt = object({ saved: boolean, content: nullable(readSkillSnapshot), documents: array(readWorkspaceDocumentSnapshot) })(value);
  if (receipt.saved && receipt.content !== null)
    return { ...receipt, saved: true as const, content: receipt.content };
  if (!receipt.saved && receipt.content === null)
    return { ...receipt, saved: false as const, content: null };
  throw new Error('SkillSave.saved 与 content 不一致');
}

export const readSkillSaveConflict = (value: unknown) => ({
  ...readSaveReceipt(value), ...object({ code: text, message: text })(value),
});

function readSourceRejection(value: unknown) {
  const { diagnostic } = object({ diagnostic: optional(opaque) })(value);
  const detail = diagnostic === undefined
    ? { message: object({ error: text })(value).error, errors: [] }
    : object({ message: text, errors: array(readSkillFormatError) })(diagnostic);
  const { compilationState } = object({ compilationState: choice('not-started', 'not-required') })(value);
  if (compilationState === 'not-required') return { ...readSavedContent(value), compilationState, detail };
  return { ...readSaveReceipt(value), compilationState, detail };
}

export function readSkillSaveRejection(value: unknown) {
  const { stage } = object({ stage: choice('source-rejected', 'compilation-failed',
    'unity-publication-failed', 'unity-publication-rejected') })(value);
  switch (stage) {
    case 'source-rejected': return { ...readSourceRejection(value), stage };
    case 'compilation-failed': return { ...readSavedContent(value), ...readCompilerFailure(value), stage,
      ...object({ compilationState: choice('failed') })(value) };
    case 'unity-publication-failed': return { ...readSavedContent(value), ...readCompilerFailure(value), stage,
      ...object({ compilationState: choice('succeeded'), compilation: readSkillCompilation, publicationState: choice('failed') })(value) };
    case 'unity-publication-rejected': return { ...readSavedContent(value), stage,
      ...object({ compilationState: choice('succeeded'), compilation: readSkillCompilation, publicationState: choice('failed'), error: text })(value) };
  }
}

export interface SkillDocumentGraph extends GraphDefinition {
  authoringPath: string;
  owner: string;
  ownerName: string;
  file: string;
  role: string | null;
  contentDomain: string | null;
  domains: string[] | null;
  variableOwnerId: string | null;
  declaredOwner: { graphId: string; nodeId: string } | null;
}

export interface SkillDocumentRelation {
  source: {
    owner: string; ownerName: string; file: string; graphId: string; nodeId: string;
    fieldId: string; serializedMemberPath: string[];
  };
  target: {
    id: string; displayName: string | null;
    owner: string; ownerName: string; file: string; ownership: 'Private' | 'Shared' | null;
  } & ({ kind: 'graph'; role: string | null } | { kind: 'timeline'; role: null });
}

export interface SkillDocument {
  skill: string;
  programId: string | null;
  sourceRevision: string;
  inputRevision: string;
  tickRate: number;
  graphs: SkillDocumentGraph[];
  relations: SkillDocumentRelation[];
  sources: SkillSnapshot[];
}

const skillPath = (documentPath: string) => `/api/authoring/skills/${encodeURIComponent(documentPath.slice('Skills/'.length))}`;
const documentQueryPath = (documentPath: string, operation: string, parameters: Record<string, string | undefined> = {}) =>
  `/api/authoring/documents/${operation}${query({ documentPath, ...parameters })}`;

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
  variables: VariablesValue;
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
  // 作者包与其声明的准入键；技能的适配者容器在首次保存时登记绑定。
  authorPackages: { name: string; admissionProfiles: string[]; revision: string }[];
  skillCreation: { parameters: { name: string; shape: StorageShape }[] };
  availability: QueryAvailability;
}

export interface SkillCreationRequest {
  parameters: {
    name: string;
    abilityId: string;
    authorPackage: string;
    displayName: string;
    admissionProfile: string;
    candidateOrder: number;
    sourceOrder: number;
    entryGraph: { name: string } | null;
    startupInputs: JsonObject[];
    trajectory: JsonObject | null;
  };
}

interface SkillCreationDraft {
  stage: 'draft';
  files: SkillFile[];
  variables: VariablesValue | null;
  availability: QueryAvailability;
}

export const authoringApi = {
  catalog: async (revision: string | null, signal?: AbortSignal) =>
    readAdapterCatalog(await getJson<unknown>(`/api/authoring/catalog${revision === null ? '' : query({ revision })}`, signal)),
  prepareReferenceReplacement: async (input: ReferenceReplacementInput, signal?: AbortSignal) =>
    readReferenceReplacement(await postJson<unknown>('/api/authoring/references/replace', { action: 'prepare', ...input }, signal), 'prepare'),
  commitReferenceReplacement: async (plan: ReferenceReplacementPrepared) =>
    readReferenceReplacement(await postJson<unknown>('/api/authoring/references/replace', { action: 'commit',
      domain: plan.domain, oldValue: plan.oldValue, newValue: plan.newValue, context: plan.context, expectedRevision: plan.revision }), 'commit'),
  export: async (documentPath: string, kind: SkillExportKind, editingView: SkillEditingView) =>
    readSkillExport(await postJson<unknown>(`${skillPath(documentPath)}/exports/${kind}`, { editingView })),
  products: async (signal?: AbortSignal) => readAuthoringProducts(await getJson<unknown>('/api/authoring/products', signal)),
  carriers: (signal?: AbortSignal) => getJson<unknown>('/api/authoring/carriers', signal)
    .then(value => object({ availability: readAuthoringAvailability, authoringRevision: text })(value, 'Carriers')),
  skillCreationCatalog: (signal?: AbortSignal) => getJson<SkillCreationCatalog>('/api/authoring/skills/creation-options', signal),
  createSkillDraft: (request: SkillCreationRequest) => postJson<SkillCreationDraft>('/api/authoring/skills/draft-create', request)
    .then(draft => ({ ...draft, variables: readCreatedVariables(draft.variables, 'SkillCreationDraft.variables') })),
  scenarioDraft: (name: string, request: ScenarioDraftRequest) =>
    postJson<{ files: SkillFile[]; variables: VariablesValue[] }>(`/api/authoring/scenarios/${encodeURIComponent(name)}/draft`, request)
      .then(draft => ({ ...draft, variables: array(readVariablesValue)(draft.variables, 'ScenarioDraft.variables') })),
  inputFragment: async (request: InputFragmentRequest) => {
    const fragment = parseSkillJson(await postText('/api/authoring/input-fragments', request)) as unknown as InputFragment;
    return { ...fragment, variables: readVariablesValue(fragment.variables, 'InputFragment.variables') };
  },
  deleteSkill: (documentPath: string, request: { expectedRevision: string; editingView: SkillEditingView }) =>
    deleteJson<SkillDeleteResult>(skillPath(documentPath), request, [409]),
  document: (documentPath: string, editingView: SkillEditingView, signal?: AbortSignal, graphId?: string) =>
    postJson<Omit<SkillDocument, 'sources'> & { sources: unknown[] }>(documentQueryPath(documentPath, 'document'), { offset: 0, limit: 128, editingView, graphId }, signal)
      .then(document => ({ ...document, sources: document.sources.map(source => readSkillSnapshot(source)) })),
  save: async (target: AuthoringTarget, request: SkillSaveRequest): Promise<SkillSaveResult> => {
    const result = await postJson<unknown>(documentQueryPath(target.documentPath, 'apply'), request);
    const { compilationState } = object({ compilationState: choice('not-required', 'succeeded') })(result, 'SkillSave');
    switch (compilationState) {
      case 'not-required': return readInputSaveResult(result, 'InputSave');
      case 'succeeded': return { ...object({ ...savedContentFields, compilation: readSkillCompilation,
        jobError: nullable(text), preview: nullable(readSkillRefreshReceipt) })(result, 'SkillSave'), compilationState };
    }
  },
  refreshStatus: (statusPath: string, signal?: AbortSignal) => getJson<PlayerSkillRefreshReceipt>(statusPath, signal).then(readSkillRefreshReceipt),
  types: (target: AuthoringTarget, offset: number, editingView: SkillEditingView, signal?: AbortSignal) =>
    postJson<TypePage>(documentQueryPath(target.documentPath, 'queries/types'), { offset, limit: 128, editingView }, signal),
  // 默认配置写进技能文件，按技能 JSON 规则解析以保留 1.0 这类浮点。
  describeType: async (target: AuthoringTarget, type: string, graphId: string, editingView: SkillEditingView, signal?: AbortSignal) =>
    readTypeDescription(await postText(documentQueryPath(target.documentPath, 'queries/type'), { type, graphId, offset: 0, limit: 128, editingView }, signal)),
  // 按当前未保存配置描述节点或普通作者值，保留技能 JSON 的数值格式。
  describeConfiguration: async (target: AuthoringTarget, type: string, configuration: JsonObject, graphId: string | null, editingView: SkillEditingView, signal?: AbortSignal) =>
    readTypeDescription(await postText(documentQueryPath(target.documentPath, 'queries/type'),
      stringifySkillJson({ type, configuration, graphId, offset: 0, limit: 128, editingView }, '\n'), signal)),
  describeNode: async (target: AuthoringTarget, graphId: string, nodeId: string, editingView: SkillEditingView, signal?: AbortSignal) => {
    const page = <T>(operation: 'fields' | 'ports') => allPages<T>(async offset =>
      parseSkillJson(await postText(documentQueryPath(target.documentPath, `queries/${operation}`),
        { graphId, nodeId, offset, limit: 128, editingView }, signal)) as unknown as Page<T>);
    const [fields, ports] = await Promise.all([page<FieldItem>('fields'), page<DescribedPort>('ports')]);
    return { fields: { ...fields, items: readFieldItems(fields.items) }, ports };
  },
  profile: async (documentPath: string, editingView: SkillEditingView, signal?: AbortSignal) => {
    const items: FieldItem[] = [];
    for (let offset = 0; ; offset += 128) {
      const result = parseSkillJson(await postText(documentQueryPath(documentPath, 'queries/profile'),
        { offset, limit: 128, editingView }, signal)) as unknown as {
        success: boolean; document: WorkspaceDocumentSnapshot; value: JsonObject; fields: Page<FieldItem>; availability: QueryAvailability;
      };
      items.push(...result.fields.items);
      if (!result.fields.hasMore) return { ...result,
        document: readWorkspaceDocumentSnapshot(result.document, 'Profile.document'),
        availability: readAuthoringAvailability(result.availability, 'Profile.availability'),
        fields: { ...result.fields, items: readFieldItems(items) } };
    }
  },
  identities: (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) =>
    allPages<IdentityItem>(offset => postJson(documentQueryPath(target.documentPath, 'queries/references'),
      { editingView, offset, limit: 128 }, signal)).then(page => page.items),
  blackboards: (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) =>
    allPages<BlackboardItem>(offset => postJson(documentQueryPath(target.documentPath, 'queries/blackboards'),
      { editingView, offset, limit: 128 }, signal)).then(page => page.items),
  // 首页按编辑视图计算并取得快照，后续页凭快照切片，不重复上传编辑视图。
  project: async (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal) => {
    const path = documentQueryPath(target.documentPath, 'queries/project');
    const first = await postJson<AuthoringProject>(path, { editingView, offset: 0, limit: 128 }, signal);
    return allPages<ProjectItem, AuthoringProject>(offset => offset === 0 ? Promise.resolve(first)
      : postJson(path, { snapshot: first.snapshot, offset, limit: 128 }, signal));
  },
  referrers: async (target: AuthoringTarget, selector: ReferenceSelector, editingView: SkillEditingView, signal?: AbortSignal): Promise<ReferrersResult> => {
    const items: ProjectReferrer[] = [];
    const unresolved = new Map<string, ReferenceQueryIssue>();
    let complete = true;
    for (let offset = 0; ; offset += 128) {
      const page = await postJson<ReferrersResult & { hasMore: boolean }>(documentQueryPath(target.documentPath, 'queries/referrers'),
        { ...selector, editingView, offset, limit: 128 }, signal);
      items.push(...page.items);
      for (const issue of page.unresolved) unresolved.set(JSON.stringify(issue), issue);
      complete = complete && page.complete;
      if (!page.hasMore) return { ...page, items, complete, unresolved: [...unresolved.values()] };
    }
  },
  referenceCurves: (target: AuthoringTarget, identity: string, editingView: SkillEditingView, signal?: AbortSignal) =>
    postJson<ReferenceCurves>(documentQueryPath(target.documentPath, 'queries/reference-curves'), { identity, editingView }, signal),
  // 模板值保留浮点写法以便原样写回；曲线通道的范围与默认值只是查询元数据，读成普通数字。
  elements: async (target: AuthoringTarget, editingView: SkillEditingView, signal?: AbortSignal, context?: InputPlanContext) => {
    const parameters = context?.kind === 'timeline' ? { timelineId: context.timeline } : {};
    const elements = parseSkillJson(await postText(documentQueryPath(target.documentPath, 'queries/elements', parameters),
      { offset: 0, limit: 128, editingView }, signal)) as unknown as SkillElements;
    elements.graphs = elements.graphs.map((graph, index) => ({ ...graph,
      variables: readCreatedVariables(graph.variables, `Elements.graphs[${index}].variables`) }));
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
  readFieldItems(description.fields.items);
  return description;
}

function readFieldItems(items: FieldItem[]): FieldItem[] {
  const fields = items.flatMap(item => item.computed ? [item] : item.fields);
  const valueOf = (value: JsonValue, clrType: string) =>
    typeof value === 'string' && (clrType === 'System.Int64' || clrType === 'System.UInt64') ? parseSkillJson(value) : value;
  for (const field of fields) {
    field.resourceReference = nullable(readResourceReferenceMetadata)(field.resourceReference, `${field.fieldId}.resourceReference`);
    field.objectSelection = nullable(readObjectSelectionMetadata)(field.objectSelection, `${field.fieldId}.objectSelection`);
    if (field.choices === null) continue;
    field.choices = field.choices.map(choice => ({ ...choice, value: valueOf(choice.value, field.clrType),
      associated: choice.associated.map(associated => ({ ...associated,
        value: valueOf(associated.value, fields.find(target => target.fieldId === associated.fieldId)!.clrType),
      })),
    }));
  }
  return items;
}

async function allPages<T, Result extends Page<T> = Page<T>>(readPage: (offset: number) => Promise<Result>): Promise<Result> {
  const items: T[] = [];
  for (let offset = 0; ; offset += 128) {
    const page = await readPage(offset);
    items.push(...page.items);
    if (!page.hasMore) return { ...page, items };
  }
}
