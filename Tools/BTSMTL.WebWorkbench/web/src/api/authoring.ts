import { parseSkillJson, type JsonObject } from '../state/skillJson';
import type { GraphDefinition } from './types';
import { getJson, postText, putJson, query } from './http';

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

// 未保存节点的类型描述（原 DescribeType）：默认配置与本次配置下的实际端口。
export interface TypeDescription {
  availability: QueryAvailability;
  type: string;
  configuration: JsonObject;
  ports: { availability: QueryAvailability; hasMore: boolean; items?: DescribedPort[] };
}

// 保存：宿主写入文件后用正式编译器编译本技能。成功为 200 与编译回执；
// 严格读取或编译拒绝为 422，文件仍已保存（saved=true），附原诊断。
export interface SkillCompilation {
  compilationKey: string;
  sourceRevision: string;
  artifactReceiptPath: string;
  reused: boolean;
}

export interface SkillSaveResult {
  content: SkillSnapshot;
  compilation: SkillCompilation;
}

export interface SkillFormatError {
  code: string;
  file?: string;
  graphId?: string;
  nodeId?: string;
  path?: string;
  message: string;
}

export interface SkillSaveRejection {
  saved: boolean;
  content: SkillSnapshot;
  stage: 'source-rejected' | 'compilation-failed';
  diagnostic?: { message: string; errors: SkillFormatError[] };
  exitCode?: number;
  output?: string;
  error?: string;
}

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
  save: (skill: string, files: SkillFile[], removedFiles: string[]) =>
    putJson<SkillSaveResult>(skillPath(skill), { content: { files, removedFiles } }),
  types: (skill: string, offset: number, signal?: AbortSignal) =>
    getJson<TypePage>(`${skillPath(skill)}/queries/types${query({ offset, limit: 128 })}`, signal),
  // 默认配置写进技能文件，按技能 JSON 规则解析以保留 1.0 这类浮点。
  describeType: async (skill: string, type: string, graphId: string, signal?: AbortSignal) =>
    parseSkillJson(await postText(`${skillPath(skill)}/queries/type`, { type, graphId, offset: 0, limit: 128 }, signal)) as unknown as TypeDescription,
};
