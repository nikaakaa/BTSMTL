import { create } from 'zustand';
import { authoringApi } from '../api/authoring';
import { ApiError, isAbort } from '../api/http';
import type { AuthorTimeline } from '../api/types';
import {
  fpRaw, fpSeconds, numberOf, parseSkillJson, shapeOf, writeSkillJson, type JsonObject, type JsonValue,
} from './skillJson';
import { consoleActions } from './store';

// 作者编辑（共享文档 9.x）：当前运行所属技能的 JSON 文件。原文按路径保存；编辑过的文件解析后改写，
// 保存时整份写回。宿主未启用作者接口时 available=false，作者模式不可用。

export interface AuthoringState {
  // null 为尚未探测；false 为宿主未启用作者接口。
  available: boolean | null;
  unavailableReason: string | null;
  // 作者模式：Timeline、节点图可编辑；运行观察时只读。
  author: boolean;
  programId: string | null;
  skill: string | null;
  revision: string | null;
  files: Record<string, string>;
  // 编辑过的文件（路径 → 解析后的文档），逐次修改递增 version。
  edited: Record<string, JsonValue>;
  version: number;
  loading: boolean;
  // 外部修改了技能文件而本地有未保存修改。
  externalChange: boolean;
}

export const useAuthoring = create<AuthoringState>()(() => ({
  available: null, unavailableReason: null, author: false, programId: null, skill: null, revision: null,
  files: {}, edited: {}, version: 0, loading: false, externalChange: false,
}));

const get = useAuthoring.getState;
const set = useAuthoring.setState;

const abilityOf = (programId: string) => programId.slice(programId.indexOf(':') + 1);

export const timelinePath = (documentId: string) => `timelines/${documentId}.json`;

// 网页还不能保存的原因：保存请求需要所属程序的 programAssetId，宿主尚未向网页提供。
export const saveBlockedReason = '宿主还没有向网页提供程序的 programAssetId，保存请求无法组成；修改先保留在网页中';

let loadController: AbortController | null = null;

export const authoringActions = {
  async probe() {
    try {
      await authoringApi.list();
      set({ available: true, unavailableReason: null });
    } catch (error) {
      set({ available: false, unavailableReason: error instanceof ApiError && error.status === 404
        ? '宿主未启用作者接口（启动时需 --SkillAuthoring true）' : error instanceof Error ? error.message : String(error) });
    }
  },

  // 打开运行所属技能：技能文件夹按 skill.json 的 m_AbilityId 与程序身份对应。
  async open(programId: string) {
    if (get().programId === programId && get().skill) return;
    if (Object.keys(get().edited).length) {
      consoleActions.log('warn', '作者', `有未保存的修改，未切换到 ${programId}`);
      return;
    }
    loadController?.abort();
    const controller = loadController = new AbortController();
    set({ programId, skill: null, revision: null, files: {}, edited: {}, loading: true, externalChange: false });
    try {
      const ability = abilityOf(programId);
      const folders = await authoringApi.list(controller.signal);
      const ordered = [...folders.filter(name => name.endsWith(ability)), ...folders.filter(name => !name.endsWith(ability))];
      for (const folder of ordered) {
        const snapshot = await authoringApi.read(folder, controller.signal);
        const root = snapshot.files.find(file => file.path === 'skill.json');
        const declared = root ? (parseSkillJson(root.content) as JsonObject).m_AbilityId : null;
        if (declared !== ability) continue;
        set({
          skill: snapshot.skill, revision: snapshot.revision, loading: false,
          files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])),
        });
        return;
      }
      set({ loading: false });
      consoleActions.log('warn', '作者', `没有找到 m_AbilityId 为 ${ability} 的技能文件夹`);
    } catch (error) {
      if (isAbort(error)) return;
      set({ loading: false });
      consoleActions.error('作者', error);
    }
  },

  setAuthor(author: boolean) {
    set({ author });
  },

  // 取可编辑的文档：首次编辑时从原文解析。
  edit(path: string, mutate: (document: JsonValue) => void) {
    const state = get();
    const document = state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
    if (document === null) {
      consoleActions.log('error', '作者', `技能 ${state.skill ?? '—'} 中没有文件 ${path}`);
      return;
    }
    try {
      mutate(document);
    } catch (error) {
      consoleActions.error('作者', error);
      return;
    }
    set({ edited: { ...state.edited, [path]: document }, version: state.version + 1 });
  },

  revert() {
    set({ edited: {}, version: get().version + 1, externalChange: false });
  },

  // 编辑过的文件按原文格式写出；与原文相同的不算修改。
  changedFiles(): { path: string; content: string }[] {
    const state = get();
    return Object.entries(state.edited).map(([path, document]) => ({
      path, content: writeSkillJson(document, shapeOf(state.files[path])),
    })).filter(file => file.content !== state.files[file.path]);
  },

  // 外部修改检测：无本地修改时直接重读，有修改时标出冲突。
  async checkExternal() {
    const { skill, revision } = get();
    if (!skill || !revision) return;
    try {
      const changes = await authoringApi.changes(skill, revision);
      if (!changes.changed) return;
      if (Object.keys(get().edited).length) set({ externalChange: true });
      else {
        const snapshot = await authoringApi.read(skill);
        set({ revision: snapshot.revision, files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])) });
      }
    } catch (error) {
      consoleActions.error('作者', error);
    }
  },

  // ── Timeline ──

  setClipTime(documentId: string, clipId: string, startSeconds: number, endSeconds: number) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      clip.m_StartTimeFPRawValue = fpRaw(startSeconds);
      clip.m_EndTimeFPRawValue = fpRaw(endSeconds);
    });
  },
};

function findTimelineClip(timeline: JsonObject, clipId: string): JsonObject | null {
  for (const track of (timeline.m_Tracks as JsonObject[] | undefined) ?? [])
    for (const clip of (track.m_Clips as JsonObject[] | undefined) ?? [])
      if (clip.m_AuthoringId === clipId) return clip;
  return null;
}

// 运行中的作者 Timeline 叠加未保存修改：片段起止取编辑后的 FP 时间。
export function applyTimelineEdits(timelines: AuthorTimeline[], edited: Record<string, JsonValue>): AuthorTimeline[] {
  if (!Object.keys(edited).length) return timelines;
  return timelines.map(timeline => {
    const document = edited[timelinePath(timeline.documentId)] as JsonObject | undefined;
    if (!document) return timeline;
    return {
      ...timeline,
      tracks: timeline.tracks.map(track => ({
        ...track,
        clips: track.clips.map(clip => {
          const source = findTimelineClip(document, clip.id);
          if (!source) return clip;
          return { ...clip, startSeconds: fpSeconds(numberOf(source.m_StartTimeFPRawValue)), endSeconds: fpSeconds(numberOf(source.m_EndTimeFPRawValue)) };
        }),
      })),
    };
  });
}

// 已编辑过的片段（`documentId/clipId`），Timeline 上标出未保存。
export function editedClipKeys(timelines: AuthorTimeline[], edited: Record<string, JsonValue>, files: Record<string, string>): Set<string> {
  const result = new Set<string>();
  for (const timeline of timelines) {
    const path = timelinePath(timeline.documentId);
    const document = edited[path] as JsonObject | undefined;
    if (!document || files[path] === undefined) continue;
    const original = parseSkillJson(files[path]) as JsonObject;
    for (const track of timeline.tracks)
      for (const clip of track.clips) {
        const now = findTimelineClip(document, clip.id), before = findTimelineClip(original, clip.id);
        if (now && before && (numberOf(now.m_StartTimeFPRawValue) !== numberOf(before.m_StartTimeFPRawValue) ||
          numberOf(now.m_EndTimeFPRawValue) !== numberOf(before.m_EndTimeFPRawValue)))
          result.add(`${timeline.documentId}/${clip.id}`);
      }
  }
  return result;
}
