import { getJson } from './http';

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

const base = '/api/authoring/skills';
const skillPath = (skill: string) => `${base}/${encodeURIComponent(skill)}`;

export const authoringApi = {
  list: (signal?: AbortSignal) => getJson<string[]>(base, signal),
  read: (skill: string, signal?: AbortSignal) => getJson<SkillSnapshot>(skillPath(skill), signal),
  changes: (skill: string, revision: string, signal?: AbortSignal) =>
    getJson<SkillChanges>(`${skillPath(skill)}/changes?revision=${encodeURIComponent(revision)}`, signal),
};
