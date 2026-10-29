import { useMemo } from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type SkillFormatError, type SkillSaveRejection, type SkillSnapshot } from '../api/authoring';
import { ApiError, isAbort } from '../api/http';
import type { AuthorTimeline, GraphDefinition, GraphNode, GraphPort } from '../api/types';
import {
  JsonFloat, cloneJson, fpRaw, fpSeconds, numberOf, parseSkillJson, shapeOf, writeSkillJson, type JsonObject, type JsonValue,
} from './skillJson';
import { consoleActions, useWorkbench } from './store';

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
  // 网页新增（含粘贴）节点的端口：来自类型描述或复制源，保存并重跑前用于显示与连线。
  addedPorts: Record<string, AddedPort[]>;
  saving: boolean;
  // 最近一次保存的编译结论；文件写入后由正式编译器判断。
  lastSave: SaveOutcome | null;
  // 技能文件夹；null 为尚未读取。
  skills: string[] | null;
  // 不经运行从 JSON 展开的全部图实例（未叠加网页修改）；严格读取被拒时为空并给出原因。
  documentGraphs: GraphDefinition[] | null;
  documentError: string | null;
  tickRate: number | null;
}

export interface SaveOutcome {
  ok: boolean;
  message: string;
  errors: SkillFormatError[];
}

export interface AddedPort {
  id: string;
  displayName: string;
  direction: 'input' | 'output';
  isTrigger: boolean;
}

export const useAuthoring = create<AuthoringState>()(() => ({
  available: null, unavailableReason: null, author: false, programId: null, skill: null, revision: null,
  files: {}, edited: {}, version: 0, loading: false, externalChange: false, addedPorts: {}, saving: false, lastSave: null,
  skills: null, documentGraphs: null, documentError: null, tickRate: null,
}));

const get = useAuthoring.getState;
const set = useAuthoring.setState;

const abilityOf = (programId: string) => programId.slice(programId.indexOf(':') + 1);

export const timelinePath = (documentId: string) => `timelines/${documentId}.json`;

let loadController: AbortController | null = null;

// 技能 JSON 中的 Timeline：轨道、片段与标记，时间由 FP 原始值换算；片段与标记的图为 m_AssetTree／m_Graph。
export function documentTimelines(files: Record<string, string>): AuthorTimeline[] {
  return Object.entries(files).filter(([path]) => path.startsWith('timelines/') && path.endsWith('.json')).map(([path, text]) => {
    const document = parseSkillJson(text) as JsonObject;
    const documentId = path.slice('timelines/'.length, -'.json'.length);
    return {
      documentId, contentIdentity: documentId, contentRevision: '', displayName: document.m_Name as string,
      tracks: (document.m_Tracks as JsonObject[]).map(track => ({
        id: track.m_AuthoringId as string, displayName: track.Name as string, muted: track.m_PersistentMuted === true,
        clips: (track.m_Clips as JsonObject[]).map(clip => ({
          id: clip.m_AuthoringId as string, contract: clip.ContractKind as string, displayName: null,
          startSeconds: fpSeconds(numberOf(clip.m_StartTimeFPRawValue)), endSeconds: fpSeconds(numberOf(clip.m_EndTimeFPRawValue)),
          graphId: (clip.m_AssetTree as string | undefined) ?? null,
        })),
        markers: ((track.m_Markers as JsonObject[] | undefined) ?? []).map(marker => ({
          id: marker.m_AuthoringId as string, seconds: fpSeconds(numberOf(marker.m_TimeFPRawValue)), graphId: marker.m_Graph as string,
        })),
      })),
    };
  });
}

// 没有选运行时，各面板直接显示打开的技能：图与 Timeline 取自 JSON，运行结果只在选了运行后叠加。
function publishDocument() {
  if (useWorkbench.getState().runId !== null) return;
  const { documentGraphs, files, skill } = get();
  useWorkbench.setState({
    graphs: skill ? documentGraphs : null, graphAvailability: [], timelines: skill ? documentTimelines(files) : [],
  });
}

useWorkbench.subscribe((state, previous) => {
  if (previous.runId !== null && state.runId === null) publishDocument();
});

async function readDocument(skill: string, signal?: AbortSignal) {
  try {
    const document = await authoringApi.document(skill, signal);
    set({ documentGraphs: document.graphs, documentError: null, tickRate: document.tickRate, programId: document.programId });
  } catch (error) {
    if (isAbort(error)) throw error;
    set({ documentGraphs: [], documentError: error instanceof ApiError ? error.reason : String(error) });
    consoleActions.error('作者', error);
  }
}

// 保存或外部修改后重新展开图（端口、新节点与连线以宿主读取为准）。
async function reloadDocument() {
  const skill = get().skill;
  if (!skill) return;
  await readDocument(skill);
  if (get().skill === skill) publishDocument();
}

export const authoringActions = {
  async listSkills() {
    try {
      set({ skills: await authoringApi.list(), available: true, unavailableReason: null });
    } catch (error) {
      set({ skills: [], available: false, unavailableReason: error instanceof ApiError && error.status === 404
        ? '宿主未启用作者接口（启动时需 --SkillAuthoring true）' : error instanceof Error ? error.message : String(error) });
    }
  },

  // 直接打开技能文件夹：读原文件与图展开，不需要运行；进入作者模式。
  async openSkill(skill: string) {
    if (get().skill === skill && get().documentGraphs) return;
    if (Object.keys(get().edited).length) {
      consoleActions.log('warn', '作者', `有未保存的修改，未切换到 ${skill}`);
      return;
    }
    loadController?.abort();
    const controller = loadController = new AbortController();
    set({ skill: null, revision: null, files: {}, edited: {}, addedPorts: {}, documentGraphs: null, documentError: null,
      loading: true, externalChange: false, lastSave: null });
    try {
      const snapshot = await authoringApi.read(skill, controller.signal);
      set({ skill: snapshot.skill, revision: snapshot.revision, files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])) });
      await readDocument(skill, controller.signal);
      set({ loading: false, author: true });
      publishDocument();
    } catch (error) {
      if (isAbort(error)) return;
      set({ loading: false });
      consoleActions.error('作者', error);
    }
  },

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
    set({ edited: {}, version: get().version + 1, externalChange: false, addedPorts: {} });
  },

  // 编辑过的文件按原文格式写出；与原文相同的不算修改。
  changedFiles(): { path: string; content: string }[] {
    const state = get();
    return Object.entries(state.edited).map(([path, document]) => ({
      path, content: writeSkillJson(document, shapeOf(state.files[path])),
    })).filter(file => file.content !== state.files[file.path]);
  },

  // 写回编辑过的文件；宿主保存后编译。被拒绝时文件也已写入，网页改用宿主回读的内容并列出诊断。
  async save() {
    const { skill, saving } = get();
    const files = authoringActions.changedFiles();
    if (!skill || saving || !files.length) return;
    set({ saving: true });
    const accept = (content: SkillSnapshot, lastSave: SaveOutcome) => {
      set({
        revision: content.revision, files: Object.fromEntries(content.files.map(file => [file.path, file.content])),
        edited: {}, addedPorts: {}, externalChange: false, version: get().version + 1, saving: false, lastSave,
      });
      void reloadDocument();
    };
    try {
      const result = await authoringApi.save(skill, files, []);
      accept(result.content, { ok: true, message: `已保存 ${files.length} 个文件，编译通过`, errors: [] });
      consoleActions.log('info', '作者', `${skill} 已保存并编译：${result.compilation.sourceRevision}`);
    } catch (error) {
      if (error instanceof ApiError && error.status === 422) {
        const rejection = JSON.parse(error.raw) as SkillSaveRejection;
        const errors = rejection.diagnostic?.errors ?? [];
        const message = rejection.stage === 'source-rejected'
          ? `已保存，严格读取拒绝 ${errors.length || 1} 项：${rejection.diagnostic?.message ?? rejection.error ?? ''}`
          : `已保存，编译失败（结束代码 ${rejection.exitCode}）`;
        accept(rejection.content, { ok: false, message, errors });
        consoleActions.log('error', '作者', message, [...errors.map(item => `${item.file ?? ''} ${item.path ?? ''}：${item.message}`),
          rejection.error ?? ''].filter(Boolean).join('\n'));
        return;
      }
      set({ saving: false, lastSave: { ok: false, message: error instanceof Error ? error.message : String(error), errors: [] } });
      consoleActions.error('作者', error);
    }
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
        await reloadDocument();
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

  // 检查器字段：片段对象内按键路径写值。
  setClipValue(documentId: string, clipId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      setIn(clip, keys, value);
    });
  },
};

export type JsonKey = string | number;

// 按键路径写值；路径上缺的对象按需建出。
function setIn(target: JsonValue, keys: JsonKey[], value: JsonValue) {
  let current = target as Record<JsonKey, JsonValue>;
  for (const key of keys.slice(0, -1)) {
    current[key] ??= {};
    current = current[key] as Record<JsonKey, JsonValue>;
  }
  current[keys[keys.length - 1]] = value;
}

// 文件的当前内容：编辑过的取编辑版本，否则解析原文；只读使用。
export function currentDocument(path: string): JsonValue | null {
  const state = get();
  return state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
}

// 编辑在原文档对象上原地修改，version 变化即重新读取。
export function useCurrentDocument(path: string | null): { document: JsonValue | null; version: number } {
  const { edited, text, version } = useAuthoring(useShallow(state => ({
    edited: path ? state.edited[path] : undefined, text: path ? state.files[path] : undefined, version: state.version,
  })));
  return useMemo(() => ({ document: edited ?? (text !== undefined ? parseSkillJson(text) : null), version }), [edited, text, version]);
}

// ── 节点图（流图）：节点在 graphs/<图>.json 的 allNodes，连线在源节点的 outConnections，位置在 layout.json ──

export const graphPath = (graphId: string) => `graphs/${graphId}.json`;
export const layoutPath = 'layout.json';

// 节点身份为 32 位十六进制，连线身份为带连字符的 UUID，与现有文件一致。
const nodeUid = () => crypto.randomUUID().replaceAll('-', '');
const connectionUid = () => crypto.randomUUID();

// 节点对象的键按序号顺序排列，与宿主存盘一致。
const sortedObject = (value: JsonObject): JsonObject =>
  Object.fromEntries(Object.keys(value).sort((left, right) => left < right ? -1 : left > right ? 1 : 0).map(key => [key, value[key]]));

const nodesOf = (document: JsonValue) => (document as JsonObject).allNodes as JsonObject[];
const connectionsOf = (node: JsonObject) => node.outConnections as JsonObject[];

function layoutNodes(document: JsonValue, graphId: string): JsonObject {
  const layout = document as JsonObject;
  const graph = (layout[graphId] ??= { allNodes: {}, canvasGroups: [], translation: { x: new JsonFloat(0), y: new JsonFloat(0) }, zoomFactor: new JsonFloat(1) }) as JsonObject;
  return graph.allNodes as JsonObject;
}

const positionValue = (position: { x: number; y: number }) =>
  ({ comments: null, position: { x: new JsonFloat(Math.round(position.x)), y: new JsonFloat(Math.round(position.y)) } });

// 复制缓冲：节点原对象、相对位置与端口，跨图粘贴时生成新身份。
let clipboard: { nodes: JsonObject[]; positions: Record<string, { x: number; y: number }>; ports: Record<string, AddedPort[]> } | null = null;

export const graphAuthoring = {
  // 按类型描述新建节点：配置取描述的默认配置，端口取描述结果。
  addNode(graphId: string, type: string, configuration: JsonObject, ports: AddedPort[], position: { x: number; y: number }): string {
    const uid = nodeUid();
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(sortedObject({ ...cloneJson(configuration), UID: uid, outConnections: [], type }));
    });
    authoringActions.edit(layoutPath, document => { layoutNodes(document, graphId)[uid] = positionValue(position); });
    set({ addedPorts: { ...get().addedPorts, [uid]: ports } });
    return uid;
  },

  // 删除节点及所有指向它们的连线与布局。
  deleteNodes(graphId: string, nodeIds: string[]) {
    const removed = new Set(nodeIds);
    authoringActions.edit(graphPath(graphId), document => {
      const graph = document as JsonObject;
      graph.allNodes = nodesOf(graph).filter(node => !removed.has(node.UID as string));
      for (const node of nodesOf(graph)) node.outConnections = connectionsOf(node).filter(connection => !removed.has(connection.targetNode as string));
    });
    authoringActions.edit(layoutPath, document => {
      const nodes = layoutNodes(document, graphId);
      for (const id of nodeIds) delete nodes[id];
    });
  },

  connect(graphId: string, source: { nodeId: string; portId: string }, target: { nodeId: string; portId: string }) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === source.nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${source.nodeId}`);
      const duplicate = connectionsOf(node).some(connection => connection.sourcePortID === source.portId &&
        connection.targetNode === target.nodeId && connection.targetPortID === target.portId);
      if (duplicate) return;
      connectionsOf(node).push({ UID: connectionUid(), sourcePortID: source.portId, targetNode: target.nodeId, targetPortID: target.portId });
    });
  },

  disconnect(graphId: string, connectionIds: string[]) {
    const removed = new Set(connectionIds);
    authoringActions.edit(graphPath(graphId), document => {
      for (const node of nodesOf(document)) node.outConnections = connectionsOf(node).filter(connection => !removed.has(connection.UID as string));
    });
  },

  // 检查器字段：节点对象内按键路径写值（成员、端口默认值或节点名）。
  setNodeValue(graphId: string, nodeId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${nodeId}`);
      setIn(node, keys, value);
    });
  },

  move(graphId: string, positions: Record<string, { x: number; y: number }>) {
    authoringActions.edit(layoutPath, document => {
      const nodes = layoutNodes(document, graphId);
      for (const [id, position] of Object.entries(positions)) nodes[id] = { ...(nodes[id] as JsonObject | undefined), ...positionValue(position) };
    });
  },

  copy(graphId: string, nodeIds: string[], positions: Record<string, { x: number; y: number }>, ports: Record<string, AddedPort[]>) {
    const state = get();
    const path = graphPath(graphId);
    const document = state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
    if (!document) return 0;
    const wanted = new Set(nodeIds);
    clipboard = { nodes: nodesOf(document).filter(node => wanted.has(node.UID as string)).map(node => cloneJson(node)), positions, ports };
    return clipboard.nodes.length;
  },

  // 粘贴：新身份，复制集合内部的连线重接到新节点，指向集合外的连线丢弃；位置整体偏移。
  paste(graphId: string, offset: { x: number; y: number }): string[] {
    if (!clipboard) return [];
    const ids = new Map(clipboard.nodes.map(node => [node.UID as string, nodeUid()]));
    const nodes = clipboard.nodes.map(node => sortedObject({
      ...cloneJson(node), UID: ids.get(node.UID as string)!,
      outConnections: connectionsOf(node).filter(connection => ids.has(connection.targetNode as string))
        .map(connection => ({ ...connection, UID: connectionUid(), targetNode: ids.get(connection.targetNode as string)! })),
    }));
    const source = clipboard;
    authoringActions.edit(graphPath(graphId), document => { nodesOf(document).push(...nodes); });
    authoringActions.edit(layoutPath, document => {
      const layout = layoutNodes(document, graphId);
      for (const [from, to] of ids) {
        const position = source.positions[from] ?? { x: 0, y: 0 };
        layout[to] = positionValue({ x: position.x + offset.x, y: position.y + offset.y });
      }
    });
    const ports = { ...get().addedPorts };
    for (const [from, to] of ids) ports[to] = source.ports[from] ?? [];
    set({ addedPorts: ports });
    return [...ids.values()];
  },
};

// 运行的图叠加未保存修改：节点与连线以编辑后的图文件为准，新节点的端口取 addedPorts，位置取编辑后的布局。
// 只处理当前显示的图实例（invocationPath）。
export function applyGraphEdits(graph: GraphDefinition, path: string, edited: Record<string, JsonValue>,
  addedPorts: Record<string, AddedPort[]>): GraphDefinition {
  const document = edited[graphPath(graph.id)] as JsonObject | undefined;
  const layout = (edited[layoutPath] as JsonObject | undefined)?.[graph.id] as JsonObject | undefined;
  if (!document && !layout) return graph;
  const positions = (layout?.allNodes ?? {}) as Record<string, JsonObject>;
  const positionOf = (id: string, fallback: GraphNode['position']) => {
    const position = positions[id]?.position as JsonObject | undefined;
    return position ? { x: numberOf(position.x), y: numberOf(position.y) } : fallback;
  };
  const sourceNodes = document ? nodesOf(document) : null;
  const sources = new Map((sourceNodes ?? []).map(node => [node.UID as string, node]));
  const kept = sourceNodes ? new Set(sources.keys()) : null;
  const known = new Set(graph.nodes.filter(node => node.invocationPath === path).map(node => node.id));
  const nodes: GraphNode[] = graph.nodes.filter(node => node.invocationPath !== path || !kept || kept.has(node.id))
    .map(node => node.invocationPath === path ? {
      ...node, position: positionOf(node.id, node.position),
      displayName: (sources.get(node.id)?._name as string | undefined) || node.displayName,
    } : node);
  const ports: GraphPort[] = graph.ports.filter(port => port.invocationPath !== path || !kept || kept.has(port.nodeId));
  for (const node of sourceNodes ?? []) {
    const id = node.UID as string;
    if (known.has(id)) continue;
    nodes.push({ id, displayName: (node._name as string | undefined) ?? (node.type as string), invocationPath: path, position: positionOf(id, { x: 0, y: 0 }) });
    for (const port of addedPorts[id] ?? [])
      ports.push({ nodeId: id, id: port.id, displayName: port.displayName, direction: port.direction, isTrigger: port.isTrigger, invocationPath: path });
  }
  let edges = graph.edges;
  if (sourceNodes) {
    const triggers = new Set(ports.filter(port => port.isTrigger).map(port => `${port.nodeId}/${port.direction}/${port.id}`));
    const connections = sourceNodes.flatMap(node => connectionsOf(node).map(connection => ({ node, connection })));
    const ids = new Set(connections.map(item => item.connection.UID as string));
    const existing = new Set(graph.edges.filter(edge => edge.invocationPath === path).map(edge => edge.id));
    edges = [
      ...graph.edges.filter(edge => edge.invocationPath !== path || ids.has(edge.id)),
      ...connections.filter(item => !existing.has(item.connection.UID as string)).map(({ node, connection }) => ({
        id: connection.UID as string,
        kind: triggers.has(`${node.UID}/output/${connection.sourcePortID}`) ? 'Child' : 'Value',
        invocationPath: path, available: true, reason: '',
        source: { graphId: graph.id, nodeId: node.UID as string, portId: connection.sourcePortID as string },
        target: { graphId: graph.id, nodeId: connection.targetNode as string, portId: connection.targetPortID as string },
      })),
    ];
  }
  return { ...graph, nodes, ports, edges };
}

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
