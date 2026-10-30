import { parseSkillJson, stringifySkillJson, type JsonObject, type JsonValue } from '../state/skillJson';
import type { GraphDefinition, LiveCommandStage, PlayerPreviewTarget } from './types';
import { getJson, getText, postText, putJson, query } from './http';
import type { PlayerCommandReceipt, PlayerJobReceipt, StartPlayerJobRequest } from './playerRecords';

// 技能作者 HTTP 接口（宿主以 --SkillAuthoring 启用）：技能文件夹内全部 JSON 文件原文与内容修订。
// 文件文本原样返回，网页只在编辑的文件上解析、改写并整份写回。

export interface SkillFile {
  path: string;
  content: string;
}

export interface SkillSnapshot {
  skill: string;
  revision: string;
  files: SkillFile[];
}

export interface SkillChanges {
  skill: string;
  revision: string;
  changed: boolean;
}

// 按需查询的统一外壳：availability 表示作者来源是否可读，items 为原分页结果。
export interface QueryAvailability {
  available: boolean;
  reason: string | null;
}

// 可创建的节点类型（原 Types）：按实际 C# 类型与原生节点声明发现。
export interface NodeTypeInfo {
  type: string;
  clrType: string;
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

// 字段的存储形状（C# 存储类型逐层展开）：集合给出 element，复合类型给出 members。
export interface StorageShape {
  clrType: string;
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
export interface FieldMetadata {
  fieldId: string;
  displayName: string | null;
  type: string;
  clrType: string;
  valueKind: string;
  serializedMemberPath: string[];
  readOnly: boolean;
  optional: boolean;
  allowedValues: string[] | null;
  pickerKind: string | null;
  pickerOwnerField: string | null;
  objectType: string | null;
  visibleWhenField: string | null;
  visibleWhenValue: string | null;
  choices: JsonValue;
  references: FieldReference[];
}

// 节点 JSON 的一个存储成员及落在其中的作者字段；computed 为由配置推出的只读字段。
export interface StoredField {
  member: string;
  value: JsonValue;
  shape: StorageShape | null;
  fields: FieldMetadata[];
  computed?: undefined;
}

export interface ComputedField extends FieldMetadata {
  computed: true;
  value: JsonValue;
}

export type FieldItem = StoredField | ComputedField;

// 未保存节点的类型描述（原 DescribeType）：默认配置，本次配置下的字段与实际端口。
export interface TypeDescription {
  availability: QueryAvailability;
  type: string;
  configuration: JsonObject;
  fields: { hasMore: boolean; items: FieldItem[] };
  ports: { availability: QueryAvailability; hasMore: boolean; items?: DescribedPort[] };
}

// 身份表（references.json）一项：作者 JSON 中引用的资产身份。
export interface IdentityItem {
  identity: string;
  asset: { AssetPath?: string };
  referenceCount: number;
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

export interface SkillElements {
  anchors: Record<string, GraphAnchor[]>;
  transition: ElementTemplate;
  tracks: ElementTemplate[];
  clips: ElementTemplate[];
  marker: ElementTemplate;
  // 黑板声明的存储形状：作用域、生命周期、分类与输入投影。
  blackboard: StorageShape;
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

export type PlayerSkillRefreshTarget = Omit<PlayerPreviewTarget, 'playerBuildId'> & { requestId: string };

export interface SkillSaveRequest {
  content: { files: SkillFile[]; removedFiles: string[] };
  preview: PlayerSkillRefreshTarget | null;
  job?: StartPlayerJobRequest;
}

interface SkillRefreshPackage {
  scenePath: string;
  sceneRevision: string;
  programAssetId: string;
  skillIdentity: string;
  programRevision: string;
  contentHash: string;
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
  source: PlayerPreviewTarget['source'];
}

export type PlayerSkillRefreshReceipt = SkillRefreshIdentity & (
  | { stage: 'accepted'; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'submitted' }; error: null; statusPath: string }
  | { stage: 'adopted'; package: SkillRefreshPackage; command: SkillRefreshCommand & { stage: 'applied' }; error: null; statusPath: string }
  | { stage: 'failed' | 'unavailable'; package: SkillRefreshPackage | null; command: SkillRefreshCommand | null; error: string; statusPath: string | null }
);

export interface SkillFormatError {
  code: string;
  file?: string;
  graphId?: string;
  nodeId?: string;
  path?: string;
  message: string;
}

export type SkillSaveRejection = { saved: true; content: SkillSnapshot } & (
  | { stage: 'source-rejected'; compilationState: 'not-started' | 'not-required' } & (
    | { diagnostic: { message: string; errors: SkillFormatError[] }; error?: never }
    | { diagnostic?: never; error: string }
  )
  | { stage: 'compilation-failed'; compilationState: 'failed'; diagnostic?: never; exitCode: number; output: string; error: string; outputPath: string; errorPath: string }
);

// 不经运行、按编译器同一展开得到的技能全部图实例；调用路径与运行记录同一语法。
export interface SkillDocument {
  skill: string;
  programId: string;
  sourceRevision: string;
  tickRate: number;
  graphs: GraphDefinition[];
}

const base = '/api/authoring/skills';
const skillPath = (skill: string) => `${base}/${encodeURIComponent(skill)}`;

export const authoringApi = {
  list: (signal?: AbortSignal) => getJson<string[]>(base, signal),
  read: (skill: string, signal?: AbortSignal) => getJson<SkillSnapshot>(skillPath(skill), signal),
  changes: (skill: string, revision: string, signal?: AbortSignal) =>
    getJson<SkillChanges>(`${skillPath(skill)}/changes?revision=${encodeURIComponent(revision)}`, signal),
  document: (skill: string, signal?: AbortSignal) => getJson<SkillDocument>(`${skillPath(skill)}/document`, signal),
  save: (skill: string, request: SkillSaveRequest) => putJson<SkillSaveResult>(skillPath(skill), request),
  refreshStatus: (statusPath: string, signal?: AbortSignal) => getJson<PlayerSkillRefreshReceipt>(statusPath, signal),
  types: (skill: string, offset: number, signal?: AbortSignal) =>
    getJson<TypePage>(`${skillPath(skill)}/queries/types${query({ offset, limit: 128 })}`, signal),
  // 默认配置写进技能文件，按技能 JSON 规则解析以保留 1.0 这类浮点。
  describeType: async (skill: string, type: string, graphId: string, signal?: AbortSignal) =>
    parseSkillJson(await postText(`${skillPath(skill)}/queries/type`, { type, graphId, offset: 0, limit: 128 }, signal)) as unknown as TypeDescription,
  // 按当前（可能未保存的）配置描述节点：字段值、条件字段与端口随配置变化。配置按技能 JSON 规则写出。
  describeNode: async (skill: string, type: string, configuration: JsonObject, graphId: string, signal?: AbortSignal) =>
    parseSkillJson(await postText(`${skillPath(skill)}/queries/type`,
      stringifySkillJson({ type, configuration, graphId, offset: 0, limit: 128 }, '\n'), signal)) as unknown as TypeDescription,
  identities: (skill: string, signal?: AbortSignal) => allPages<IdentityItem>(`${skillPath(skill)}/queries/references`, signal),
  blackboards: (skill: string, signal?: AbortSignal) => allPages<BlackboardItem>(`${skillPath(skill)}/queries/blackboards`, signal),
  elements: async (skill: string, signal?: AbortSignal) =>
    parseSkillJson(await getText(`${skillPath(skill)}/queries/elements`, signal)) as unknown as SkillElements,
};

async function allPages<T>(path: string, signal?: AbortSignal): Promise<T[]> {
  const items: T[] = [];
  for (let offset = 0; ; offset += 128) {
    const page = await getJson<Page<T>>(`${path}${query({ offset, limit: 128 })}`, signal);
    items.push(...page.items);
    if (!page.hasMore) return items;
  }
}
