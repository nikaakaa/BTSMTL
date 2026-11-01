import {
  Background, BackgroundVariant, BaseEdge, getBezierPath, Handle, MiniMap, NodeResizer, Position, ReactFlow,
  ReactFlowProvider, SelectionMode, applyNodeChanges, useConnection, useInternalNode, useReactFlow, useStore as useFlowStore, type Edge, type EdgeProps, type Node, type NodeChange,
  type Connection, type NodeProps, type Viewport,
} from '@xyflow/react';
import '@xyflow/react/dist/base.css';
import {
  createContext, memo, useCallback, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore,
  type DragEvent as ReactDragEvent, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, authoringTargetKey, describedPortsAvailable, type AuthoringTarget, type NodeTypeInfo, type ReferenceNodeTemplate, type SkillDocumentGraph, type SkillElements } from '../../api/authoring';
import { isAbort } from '../../api/http';
import type { PlayerJobId } from '../../api/playerRecords';
import type { ExecutionCall, GraphDefinition, GraphEdge, GraphPort } from '../../api/types';
import {
  applyGraphEdits, authoringActions, authoringDocument, currentDocument, graphAuthoring, graphGroups, graphPath, layoutPath, ownsAuthoringGraph, skillElements, useAuthoring, useCurrentDocument, useSkillElements,
  useDocumentGraphHierarchy, useDocumentObservationBinding, type CanvasGroup,
} from '../../state/authoring';
import { cloneJson, setIn, type JsonObject } from '../../state/skillJson';
import { acceptsReferenceDrag, readReferenceDrag, type ReferenceDrag } from '../../state/referenceSelection';
import { usePanelId, usePanelParams } from '../../shell/panel';
import { useFrameReport } from '../../shell/readiness';
import {
  belongsTo, callKey, callLabel, edgeSelectId, graphCrumbs, graphKey, graphName, graphRecords, graphReferrers,
  isStateMachine, layoutGraph, matchesDocumentCall, nodeGraphs, nodeName, nodeSelectId, parentCallMatches, parentGraphCalls, portColor,
  recordedGraphCalls, type GraphHierarchy, type GraphNavigationTarget, type LaidNode, type RecordedGraphCall,
} from '../../state/graphModel';
import { projectActions } from '../../state/project';
import { consoleActions, getState, selectionActions, useWorkbench, type GraphNavigationOrigin } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, ContextMenu, MenuButton, Select, TextInput, type MenuEntry } from '../../ui/controls';
import { CloseIcon } from '../../ui/icons';
import { BlackboardFields } from '../inspector/FieldEditors';
import { RouteStore, type RouteHistory } from './routeStore';
import styles from './graph.module.css';

interface Params {
  graph: string | null;
  call: string | null;
  pin: { runId: PlayerJobId; graph: string; call: ExecutionCall } | null;
  caller: { runId: PlayerJobId; call: ExecutionCall } | null;
  origin: GraphNavigationOrigin | null;
  // 视图位置按所看的图与图实例保存。
  viewport: (Viewport & { view: string }) | null;
  values: boolean;
  dim: boolean;
  relations: boolean;
  minimap: boolean;
}
const defaults: Params = {
  graph: null, call: null, pin: null, caller: null, origin: null, viewport: null, values: true, dim: false, relations: false, minimap: false,
};

interface ViewOptions {
  store: RouteStore;
  values: boolean;
  dim: boolean;
  related: Set<string> | null;
  enter(graph: GraphNavigationTarget, nodeId?: string): void;
  openNode(data: NodeData): void;
  hierarchy: GraphHierarchy;
  graph: GraphDefinition;
  // 作者模式下端口可拖出连线。
  editable?: boolean;
  // 按住分组标题拖动前选中分组及其中的节点，一起拖动。
  grabGroup?(group: CanvasGroup): void;
  renameGroup?(group: CanvasGroup, name: string): void;
  resizeGroup?(group: CanvasGroup, rect: CanvasGroup['rect']): void;
}
const ViewContext = createContext<ViewOptions | null>(null);
const useView = () => useContext(ViewContext)!;

interface NodeData extends Record<string, unknown> {
  laid: LaidNode;
  index: number;
  selectId: string;
  system: boolean;
  children: GraphNavigationTarget[];
}
type FlowNode = Node<NodeData, 'flow' | 'state'>;
interface GroupData extends Record<string, unknown> {
  group: CanvasGroup;
}
type GroupNode = Node<GroupData, 'canvasGroup'>;
type CanvasNode = FlowNode | GroupNode;
const isGroupNode = (node: CanvasNode): node is GroupNode => node.type === 'canvasGroup';
const groupNodeId = (group: CanvasGroup) => `group:${group.index}`;

// 分组包含左上角落在矩形内的节点（照 NodeCanvas）。
const inside = (rect: CanvasGroup['rect'], position: { x: number; y: number }) =>
  position.x >= rect.x && position.y >= rect.y && position.x <= rect.x + rect.width && position.y <= rect.y + rect.height;
interface EdgeData extends Record<string, unknown> {
  edge: GraphEdge;
  // 同一对状态之间多条转移时的错开量。
  offset: number;
}
type FlowEdge = Edge<EdgeData, 'route' | 'transition'>;

export function GraphPanel() {
  const opened = useAuthoring(useShallow(state => ({ target: state.target,
    loading: authoringDocument(state).loading, error: authoringDocument(state).documentError,
    graphs: authoringDocument(state).documentGraphs })));
  if (!opened.target) return <ReportEmpty text="选择要编辑的内容" />;
  if (opened.target.kind === 'scenario') return <ReportEmpty text="此场景方案没有技能节点图" />;
  if (opened.loading || !opened.graphs) return <Empty text="读取中" />;
  if (!opened.graphs.length) return <ReportEmpty text={opened.error ?? '此技能没有节点图'} />;
  return <GraphView graphs={opened.graphs} />;
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

const graphCallKey = ({ graph, call }: RecordedGraphCall) => JSON.stringify([graphKey(graph), callKey(call)]);

function GraphView({ graphs }: { graphs: SkillDocumentGraph[] }) {
  const binding = useDocumentObservationBinding();
  const runId = useWorkbench(state => state.runId);
  const observation = useWorkbench(useShallow(state => ({ current: state.frame?.route ?? null, frames: state.routeFrames })));
  const [params, setParams] = usePanelParams<Params>(defaults);
  const hierarchy = useDocumentGraphHierarchy();
  const programId = useAuthoring(state => authoringDocument(state).programId);

  const currentCalls = useMemo(() => recordedGraphCalls([], observation.current, graphs, binding), [observation.current, graphs, binding]);
  const followed = params.graph === null && currentCalls.length === 1 ? graphKey(currentCalls[0].graph) : null;
  const graph = graphs.find(item => graphKey(item) === (params.graph ?? followed)) ?? graphs.find(item => item.id === programId)!;
  const key = graphKey(graph);

  const caller = params.caller?.runId === runId && matchesDocumentCall(params.caller.call, binding) ? params.caller : null;
  const graphCalls = currentCalls.filter(item => graphKey(item.graph) === key && (caller === null || parentCallMatches(caller.call, item.call)));
  const calls = graphCalls.map(item => item.call);
  const pin = params.pin?.runId === runId && params.pin.graph === key && matchesDocumentCall(params.pin.call, binding)
    ? params.pin : null;
  const automatic = params.graph === null ? currentCalls.length === 1 ? currentCalls[0].call : null : calls.length === 1 ? calls[0] : null;
  const observedCall = pin?.call ?? calls.find(item => callKey(item) === params.call) ?? automatic;
  const availableCalls = params.graph === null ? currentCalls : graphCalls;
  const choices = pin && !availableCalls.some(item => callKey(item.call) === callKey(pin.call)) ? [{ graph, call: pin.call }, ...availableCalls] : availableCalls;
  const callOptions = choices.map(item => ({ value: graphCallKey(item),
    label: `${params.graph === null ? graphName(item.graph) + ' · ' : ''}${callLabel(item.call)}` }));
  const path = graph.authoringPath;
  const togglePin = pin ? () => setParams({ pin: null }) : observedCall === null || runId === null ? undefined
    : () => setParams({ graph: key, call: callKey(observedCall), pin: { runId, graph: key, call: observedCall } });
  const recorded = useMemo(() => recordedGraphCalls(observation.frames, observation.current, graphs, binding), [observation, graphs, binding]);
  const callCrumbs = useMemo(() => {
    if (observedCall === null) return null;
    const items: RecordedGraphCall[] = [{ graph, call: observedCall }];
    let current = observedCall;
    let parents = parentGraphCalls(current, recorded);
    while (parents.length === 1) {
      items.unshift(parents[0]);
      current = parents[0].call;
      parents = parentGraphCalls(current, recorded);
    }
    return { items, parent: current.parentInvocationPath ? current : null, choices: parents };
  }, [graph, observedCall, recorded]);
  const enterCall = runId === null ? undefined : ({ graph, call }: RecordedGraphCall) => setParams({ graph: graphKey(graph),
    call: callKey(call), pin: { runId, graph: graphKey(graph), call }, caller: null, origin: null, viewport: null });

  const selectCall = (value: string) => {
    const choice = choices.find(item => graphCallKey(item) === value)!;
    setParams({ graph: graphKey(choice.graph), call: callKey(choice.call), pin: null });
  };

  useEffect(() => {
    const selection = getState().selection;
    if (selection?.kind === 'node' && graph.nodes.some(node => nodeSelectId(graph, node) === selection.id) ||
      selection?.kind === 'edge' && graph.edges.some(edge => edgeSelectId(graph, edge) === selection.id)) {
      const previous = selection.call ? callKey(selection.call) : null;
      const next = observedCall ? callKey(observedCall) : null;
      if (previous !== next) selectionActions.select({ ...selection, call: observedCall ?? undefined });
    }
  }, [graph, observedCall]);

  const enter = useCallback((target: GraphNavigationTarget, nodeId?: string) => {
    const source = hierarchy.graphs.get(key)!;
    const origin = nodeId === undefined ? null : { owner: source.owner, ownerName: source.ownerName, graphId: source.id, nodeId };
    void projectActions.openGraph(target, undefined, origin).catch(reason => consoleActions.error('作者', reason));
  }, [hierarchy, key]);

  // 其它面板请求显示某张图（检查器跳转、Timeline 片段）。
  const request = useWorkbench(state => state.graphRequest);
  const handled = useRef(0);
  useEffect(() => {
    if (!request || request.sequence === handled.current) return;
    handled.current = request.sequence;
    if (graphs.some(item => graphKey(item) === request.key)) setParams({ graph: request.key,
      call: request.observation ? callKey(request.observation.call) : null,
      pin: request.observation ? { ...request.observation, graph: request.key } : null, caller: null, origin: request.origin, viewport: null });
  }, [request, graphs, setParams]);
  const crumbs = useMemo(() => graphCrumbs(hierarchy, graph), [hierarchy, graph]);
  const referrers = useMemo(() => graphReferrers(hierarchy, graph), [hierarchy, graph]);
  const [blackboard, setBlackboard] = useState(false);
  const graphOptions = [...hierarchy.graphs].map(([value, item]) => ({ value, label: `${item.ownerName} · ${graphName(item)}` }));

  const settings: MenuEntry[] = [
    { kind: 'check', label: '跟随当前帧', checked: params.graph === null, onChange: on => setParams({ graph: on ? null : key, call: null, pin: null, caller: null, origin: null }) },
    { kind: 'separator' },
    { kind: 'check', label: '端口值', checked: params.values, onChange: values => setParams({ values }) },
    { kind: 'check', label: '压暗本帧没走到的节点', checked: params.dim, onChange: dim => setParams({ dim }) },
    { kind: 'check', label: '只突出所选节点的关系', checked: params.relations, onChange: relations => setParams({ relations }) },
    { kind: 'check', label: '小地图', checked: params.minimap, onChange: minimap => setParams({ minimap }) },
  ];

  return (
    // 视图状态随所看的图与图实例重建，保存的视图位置才能生效。
    <div className={styles.root}>
      <ReactFlowProvider key={`${key}|${path}`}>
        <PanelBar wrap>
          {graphOptions.length > 1 && <Select label="作者图" value={key} options={graphOptions}
            onChange={value => enter(hierarchy.graphs.get(value)!)} />}
          <span className={styles.crumbs} title={callCrumbs === null ? '作者层级' : '档案实际调用路径'}>
            {callCrumbs !== null ? <>
              {callCrumbs.parent && <span className={styles.crumb}>
                {callCrumbs.choices.length > 1 && enterCall ? <MenuButton label="选择父调用" entries={callCrumbs.choices.map(item => ({
                  label: `${graphName(item.graph)} · ${callLabel(item.call)}`, onSelect: () => enterCall(item),
                }))}>选择父调用</MenuButton>
                  : <span title={`路径：${callCrumbs.parent.parentInvocationPath}\n代次：${callCrumbs.parent.parentInvocationGeneration ?? '未记录'}`}>
                    {callCrumbs.parent.parentInvocationGeneration === null ? '父调用代次未记录' : '已加载记录中没有父调用'}
                  </span>}
                <i className={styles.crumbArrow} />
              </span>}
              {callCrumbs.items.map((item, index) => <span key={callKey(item.call)} className={styles.crumb} title={callLabel(item.call)}>
                {index > 0 && <i className={styles.crumbArrow} />}
                {index < callCrumbs.items.length - 1 && enterCall
                  ? <button className={styles.crumbLink} onClick={() => enterCall(item)}>{graphName(item.graph)}</button>
                  : <span data-current>{graphName(item.graph)}</span>}
              </span>)}
            </> : crumbs.map((crumb, index) => (
              <span key={index} className={styles.crumb}>
                {index > 0 && <i className={styles.crumbArrow} />}
                {crumb.graph !== graph
                  ? <button className={styles.crumbLink} onClick={() => enter(crumb.graph)}>{crumb.label}</button>
                  : <span data-current={crumb.graph === graph || undefined}>{crumb.label}</span>}
              </span>
            ))}
          </span>
          {params.origin !== null && <Button variant="ghost" tip={`${params.origin.ownerName} · ${params.origin.nodeId}`} onClick={() => {
            const source = params.origin!;
            void projectActions.openGraph({ owner: source.owner, ownerName: source.ownerName, id: source.graphId }, source.nodeId)
              .catch(reason => consoleActions.error('作者', reason));
          }}>返回引用节点</Button>}
          {referrers.length > 0 && <MenuButton label="已加载引用" caret entries={referrers.map(({ source }) => ({
            label: `${source.ownerName} · ${source.file} · ${source.nodeId} · ${source.fieldId}`,
            onSelect: () => {
              void projectActions.openGraph({ owner: source.owner, ownerName: source.ownerName, id: source.graphId }, source.nodeId)
                .catch(reason => consoleActions.error('作者', reason));
            },
          }))}>已加载引用（{referrers.length}）</MenuButton>}
          {callOptions.length > 1 && <Select label="实际调用" value={observedCall ? graphCallKey({ graph, call: observedCall }) : null}
            options={callOptions} placeholder="选择实际调用"
            tip={observedCall ? callLabel(observedCall) : '本帧有多个调用，选择要观察的实例'} onChange={selectCall} />}
          {observedCall !== null && <Button variant="ghost" pressed={pin !== null} disabled={togglePin === undefined}
            tip={`${pin ? '解除固定，按当前帧选择调用' : '固定这次实际调用，拖动时间游标时保留其身份'}\n${callLabel(observedCall)}`}
            onClick={togglePin}>固定调用</Button>}
          {caller !== null && calls.length === 0 && pin === null && <span className={styles.notice}>此帧没有精确匹配的子调用记录</span>}
          <FitButton />
          <Button variant="ghost" pressed={blackboard} tip="本图的黑板变量与声明" onClick={() => setBlackboard(!blackboard)}>黑板</Button>
          <MenuButton label="显示设置" caret entries={settings}>显示设置</MenuButton>
        </PanelBar>
        {/* 图名只在面包屑里出现一次；黑板浮层贴在画布右上 */}
        <div className={styles.stage}>
          {blackboard && <BlackboardPanel graph={graph} onClose={() => setBlackboard(false)} />}
          <AuthoredCanvas graph={graph} call={observedCall}
            hierarchy={hierarchy} enter={enter} params={params} setParams={setParams} />
        </div>
      </ReactFlowProvider>
    </div>
  );
}

// 添加节点的类型选择：照 NodeCanvas 的右键节点浏览器，搜索宿主按实际 C# 类型发现的全部可创建类型。
let typeCatalog: { key: string; items: Promise<NodeTypeInfo[]> } | null = null;
function loadTypes(skill: AuthoringTarget) {
  const key = authoringTargetKey(skill);
  if (typeCatalog?.key !== key) {
    const items = (async () => {
      const result: NodeTypeInfo[] = [];
      for (let offset = 0; ; offset += 128) {
        const page = await authoringApi.types(skill, offset);
        if (!page.availability.available) throw new Error(page.availability.reason ?? '类型查询不可用');
        result.push(...page.items);
        if (!page.hasMore) return result;
      }
    })();
    typeCatalog = { key, items };
    items.catch(() => { typeCatalog = null; });
  }
  return typeCatalog.items;
}

interface ReferenceNodeDrop {
  reference: Extract<ReferenceDrag, { kind: 'reference' }>;
  templates: ReferenceNodeTemplate[];
}

interface NodeCreationChoice {
  key: string;
  type: string;
  detail: string;
  clrType: string | null;
  configuration: JsonObject | null;
}

function referenceConfiguration(template: ReferenceNodeTemplate, reference: ReferenceNodeDrop['reference']): JsonObject {
  const configuration = cloneJson(template.configuration);
  setIn(configuration, template.serializedMemberPath, reference.value);
  return configuration;
}

function useNodeCreator(graphId: string) {
  const skill = useAuthoring(state => state.target)!;
  const pending = useRef(new Set<AbortController>());
  useEffect(() => {
    const abort = () => { for (const controller of pending.current) controller.abort(); };
    const unsubscribe = useAuthoring.subscribe((state, previous) => {
      if (state.target !== previous.target || state.author !== previous.author) abort();
    });
    return () => { unsubscribe(); abort(); };
  }, []);
  return async (type: string, configuration: JsonObject | null, position: { x: number; y: number }) => {
    const controller = new AbortController();
    pending.current.add(controller);
    try {
      const description = configuration === null
        ? await authoringApi.describeType(skill, type, graphId, authoringActions.editingView(), controller.signal)
        : await authoringApi.describeNode(skill, type, configuration, graphId, authoringActions.editingView(), controller.signal);
      controller.signal.throwIfAborted();
      if (!description.availability.available) throw new Error(description.availability.reason ?? `${type} 不可创建`);
      if (!describedPortsAvailable(description.ports)) throw new Error(description.ports.availability.reason);
      const ports = description.ports.items.map(entry => ({
        id: entry.port.ID, displayName: entry.port.name, direction: entry.direction, isTrigger: entry.port.type === 'Flow',
      }));
      return graphAuthoring.addNode(graphId, type, description.configuration, ports, position);
    } finally { pending.current.delete(controller); }
  };
}

function NodeTypePicker({ graph, at, referenceDrop, onCreated, onClose }: {
  graph: GraphDefinition; at: { x: number; y: number; flow: { x: number; y: number } }; referenceDrop?: ReferenceNodeDrop;
  onCreated(id: string): void; onClose(): void;
}) {
  const skill = useAuthoring(state => state.target)!;
  const createNode = useNodeCreator(graph.id);
  const [types, setTypes] = useState<NodeTypeInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (referenceDrop) return;
    const controller = new AbortController();
    loadTypes(skill).then(items => { if (!controller.signal.aborted) setTypes(items); }, (reason: unknown) => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : String(reason));
    });
    return () => controller.abort();
  }, [skill, referenceDrop]);
  const choices: NodeCreationChoice[] | null = referenceDrop
    ? referenceDrop.templates.map(template => ({ key: `${template.type}/${template.fieldId}`, type: template.type, detail: template.fieldId,
      clrType: null, configuration: referenceConfiguration(template, referenceDrop.reference) }))
    : types?.map(item => ({ key: item.type, type: item.type, detail: [item.kind, item.family, item.timelineDomains].filter(Boolean).join(' · '),
      clrType: item.clrType, configuration: null })) ?? null;
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const shown = (choices ?? []).filter(item => words.every(word => `${item.type} ${item.detail}`.toLowerCase().includes(word)));
  const add = async (item: NodeCreationChoice) => {
    setBusy(true);
    try {
      const id = await createNode(item.type, item.configuration, at.flow);
      onCreated(id);
      onClose();
    } catch (reason) {
      if (isAbort(reason)) return;
      consoleActions.error('作者', reason);
      setBusy(false);
    }
  };
  return (
    <div className={styles.picker} style={{ left: at.x, top: at.y }} onKeyDown={event => {
      if (event.key === 'Escape') onClose();
      if (event.key === 'Enter' && shown[0] && !busy) void add(shown[0]);
      event.stopPropagation();
    }}>
      <div className={styles.pickerHead}>
        <TextInput autoFocus placeholder="搜索节点类型" value={search} onChange={event => setSearch(event.currentTarget.value)}
          aria-label="搜索节点类型" className={styles.pickerSearch} />
        <button className={styles.pickerClose} onClick={onClose} aria-label="关闭"><CloseIcon /></button>
      </div>
      <div className={styles.pickerList}>
        {error ? <div className={styles.pickerNote}>{error}</div>
          : !choices ? <div className={styles.pickerNote}>读取节点类型中</div>
            : !shown.length ? <div className={styles.pickerNote}>没有匹配的类型</div>
              : shown.map(item => (
                <button key={item.key} className={styles.pickerItem} disabled={busy} title={item.clrType ?? undefined}
                  onClick={() => void add(item)}>
                  <span>{item.type}</span>
                  <em>{item.detail}</em>
                </button>
              ))}
      </div>
    </div>
  );
}

// 黑板浮层：读打开技能里这张图的文件；作者模式可增删、改名、改默认值与声明。
function BlackboardPanel({ graph, onClose }: { graph: GraphDefinition; onClose(): void }) {
  const author = useAuthoring(useShallow(state => ({
    own: ownsAuthoringGraph(state, graph.id), editable: state.author,
  })));
  const elements = useSkillElements(author.own && author.editable);
  return (
    <div className={styles.blackboard}>
      <div className={styles.pickerHead}>
        <b>黑板 · {graphName(graph)}</b>
        <button className={styles.pickerClose} onClick={onClose} aria-label="关闭"><CloseIcon /></button>
      </div>
      <div className={styles.blackboardBody}>
        {author.own ? <BlackboardFields graphId={graph.id} shape={elements?.blackboard ?? null} editable={author.editable && !!elements} />
          : <div className={styles.pickerNote}>这张图不属于打开的技能</div>}
      </div>
    </div>
  );
}

function FitButton() {
  const flow = useReactFlow();
  return <Button variant="ghost" tip="适配视图" onClick={() => void flow.fitView({ padding: 0.12 })}>全部居中</Button>;
}

const nodeTypes = { flow: memo(FlowNodeView), state: memo(StateNodeView), canvasGroup: memo(GroupView) };
const edgeTypes = { route: memo(RouteEdgeView), transition: memo(TransitionEdgeView) };

function isSystemNode(graph: GraphDefinition, hierarchy: GraphHierarchy, laid: LaidNode) {
  return !nodeGraphs(hierarchy, graph, laid.node.id).length;
}

// 作者模式：当前技能的图（流图与状态机）叠加网页中未保存的修改后显示，并可编辑；分组取自布局文件。
function AuthoredCanvas(props: Omit<GraphCanvasProps, 'editable' | 'groups' | 'elements'>) {
  const { graph } = props;
  const author = useAuthoring(useShallow(state => ({
    on: state.author && ownsAuthoringGraph(state, graph.id),
    edited: authoringDocument(state).edited, ports: authoringDocument(state).addedPorts,
    own: ownsAuthoringGraph(state, graph.id),
  })));
  const editable = author.on;
  const shown = useMemo(() => editable ? applyGraphEdits(graph, author.edited, author.ports) : graph,
    [editable, graph, author.edited, author.ports]);
  const { document: layout } = useCurrentDocument(author.own ? layoutPath : null);
  const groupsText = JSON.stringify(graphGroups(layout, graph.id));
  const groups = useMemo(() => JSON.parse(groupsText) as CanvasGroup[], [groupsText]);
  // 新建状态、转移与图的锚点和默认值。
  const elements = useSkillElements(editable);
  return <GraphCanvas {...props} graph={shown} editable={editable} groups={groups} elements={elements} />;
}

interface GraphCanvasProps {
  graph: SkillDocumentGraph;
  call: ExecutionCall | null;
  hierarchy: GraphHierarchy;
  enter(graph: GraphNavigationTarget, nodeId?: string): void;
  params: Params;
  setParams(patch: Partial<Params>): void;
  editable: boolean;
  groups: CanvasGroup[];
  elements: SkillElements | null;
}

function GraphCanvas({ graph, call, hierarchy, enter, params, setParams, editable, groups, elements }: GraphCanvasProps) {
  const path = graph.authoringPath;
  const binding = useDocumentObservationBinding();
  const panelId = usePanelId();
  const view = `${graphKey(graph)}|${path}`;
  const canvasSelection = useWorkbench(state => state.canvasSelections[panelId]);
  const selectCanvas = useCallback((nodes: readonly string[], edges: readonly string[]) =>
    selectionActions.selectCanvas(panelId, { view, nodes, edges }), [panelId, view]);
  const runId = useWorkbench(state => state.runId);
  const minimapWidth = useFlowStore(state => Math.min(204, state.width * 0.3, state.height * 0.3 * 204 / 104));
  const machine = isStateMachine(graph) || (currentDocument(graphPath(graph.id)) as JsonObject | null)?.Role === 'StateMachine';
  const layout = useMemo(() => layoutGraph(graph, path), [graph, path]);
  const store = useMemo(() => new RouteStore(), []);
  const build = useCallback((previous: CanvasNode[]) => {
    const selected = new Set(canvasSelection?.view === view ? canvasSelection.nodes : []);
    // 重建保留已测量的尺寸：否则节点要等重新测量才显示，连线在此之前不画。
    const measured = new Map(previous.map(node => [node.id, node.measured]));
    // 分组画在节点下面，只有标题可以拖动。
    const groupNodes = groups.map((group): GroupNode => ({
      id: groupNodeId(group), type: 'canvasGroup', position: { x: group.rect.x, y: group.rect.y },
      width: group.rect.width, height: group.rect.height, zIndex: -1, selected: selected.has(groupNodeId(group)),
      dragHandle: `.${styles.groupHead}`, className: styles.groupNode, data: { group },
    }));
    return [...groupNodes, ...layout.nodes.map((laid, index): FlowNode => {
      const children = nodeGraphs(hierarchy, graph, laid.node.id);
      return {
        id: laid.node.id, type: machine ? 'state' : 'flow', position: { x: laid.x, y: laid.y }, selected: selected.has(laid.node.id),
        measured: measured.get(laid.node.id),
        data: {
          laid, index, selectId: nodeSelectId(graph, laid.node), system: machine && isSystemNode(graph, hierarchy, laid),
          children,
        },
      };
    })];
  }, [layout, hierarchy, graph, machine, groups, canvasSelection, view]);
  const [nodes, setNodes] = useState<CanvasNode[]>(() => build([]));
  // 作者修改后图结构或分组变化：重建节点，保留选中。
  const built = useRef(build);
  useEffect(() => {
    if (built.current === build) return;
    built.current = build;
    setNodes(build);
  }, [build]);
  const edges = useMemo<FlowEdge[]>(() => {
    const pairs = new Map<string, number>();
    const counts = new Map<string, number>();
    const pairKey = (edge: GraphEdge) => [edge.source!.nodeId, edge.target!.nodeId].sort().join('|');
    for (const edge of layout.edges) counts.set(pairKey(edge), (counts.get(pairKey(edge)) ?? 0) + 1);
    return layout.edges.map(edge => {
      if (machine) {
        const pair = pairKey(edge);
        const index = pairs.get(pair) ?? 0;
        pairs.set(pair, index + 1);
        return {
          id: edge.id, type: 'transition', source: edge.source!.nodeId, target: edge.target!.nodeId,
          sourceHandle: 'out:@node', targetHandle: 'in:@node',
          data: { edge, offset: (index - (counts.get(pair)! - 1) / 2) * 16 },
        };
      }
      const source = layout.nodes.find(item => item.node.id === edge.source!.nodeId)!;
      const target = layout.nodes.find(item => item.node.id === edge.target!.nodeId)!;
      return {
        id: edge.id, type: 'route', source: edge.source!.nodeId, target: edge.target!.nodeId,
        sourceHandle: source.outputs.some(port => port.id === edge.source!.portId) ? `out:${edge.source!.portId}` : 'out:@node',
        targetHandle: target.inputs.some(port => port.id === edge.target!.portId) ? `in:${edge.target!.portId}` : 'in:@node',
        data: { edge, offset: 0 },
      };
    });
  }, [layout, graph, machine]);

  // 帧变化时只更新路线状态，节点数组不重建；状态机另按到本帧为止的路线标出走过的状态与转移。
  useEffect(() => {
    const apply = () => {
      const state = getState();
      const records = graphRecords(state.frame?.route, graph, binding).filter(item => call !== null && callKey(item.call) === callKey(call));
      const history: RouteHistory = { visited: new Set(), traveled: new Set() };
      if (machine && state.frame !== null) {
        for (const frame of state.routeFrames) {
          if (frame.seconds > state.frame.seconds) continue;
          const matches = (item: typeof frame.nodes[number] | typeof frame.edges[number]) => item.call !== null && item.graphId === graph.id &&
            belongsTo(item.call, graph, binding) && call !== null && callKey(item.call) === callKey(call);
          for (const node of frame.nodes) if (node.kind === 'Enter' && matches(node)) history.visited.add(node.nodeId);
          for (const edge of frame.edges) if (edge.passed && matches(edge)) history.traveled.add(edge.edgeId);
        }
      }
      store.update(layout.nodes, layout.edges, records, history);
    };
    apply();
    return useWorkbench.subscribe((state, previous) => {
      if (state.frame !== previous.frame || state.frameIndex !== previous.frameIndex || state.routeFrames !== previous.routeFrames) apply();
    });
  }, [store, graph, layout, call, machine, binding]);
  useFrameReport(`${graphKey(graph)}|${path}|${call === null ? '' : callKey(call)}`);

  const selectedNode = useWorkbench(state => state.selection?.kind === 'node' ? state.selection.id : null);
  const related = useMemo(() => {
    if (!selectedNode || (!params.relations && !machine)) return null;
    const own = layout.nodes.find(item => nodeSelectId(graph, item.node) === selectedNode);
    if (!own) return null;
    const result = new Set([own.node.id]);
    for (const edge of layout.edges) {
      if (edge.source!.nodeId === own.node.id) { result.add(edge.target!.nodeId); result.add(edge.id); }
      if (edge.target!.nodeId === own.node.id) { result.add(edge.source!.nodeId); result.add(edge.id); }
    }
    return result;
  }, [params.relations, machine, selectedNode, layout, graph]);

  const openNode = useCallback((data: NodeData) => {
    if (data.children.length === 1) enter(data.children[0], data.laid.node.id);
  }, [enter]);
  const options = useMemo<ViewOptions>(() => ({
    store, values: params.values, dim: params.dim, related, enter, openNode, hierarchy, graph,
  }), [store, params.values, params.dim, related, enter, openNode, hierarchy, graph]);
  const onNodesChange = useCallback((changes: NodeChange<CanvasNode>[]) => {
    setNodes(current => applyNodeChanges(changes, current));
    const selectionChanges = changes.filter(change => change.type === 'select');
    if (selectionChanges.length) {
      const current = getState().canvasSelections[panelId];
      const selected = new Set(current?.view === view ? current.nodes : []);
      for (const change of selectionChanges) {
        if (change.selected) selected.add(change.id); else selected.delete(change.id);
      }
      selectCanvas([...selected], current?.view === view ? current.edges : []);
    }
  }, [panelId, selectCanvas, view]);
  const onNodeClick = useCallback((event: ReactMouseEvent, node: CanvasNode) => {
    if (editable && !event.ctrlKey && !event.shiftKey) selectCanvas([node.id], []);
    if (!isGroupNode(node)) selectionActions.select({ kind: 'node', id: node.data.selectId, call: call ?? undefined });
  }, [call, editable, selectCanvas]);
  const onNodeDoubleClick = useCallback((_: unknown, node: CanvasNode) => {
    if (isGroupNode(node)) return;
    openNode(node.data);
  }, [openNode]);
  const saved = params.viewport?.view === view ? params.viewport : null;
  const onMoveEnd = useCallback((_: unknown, viewport: Viewport) => setParams({ viewport: { ...viewport, view } }), [setParams, view]);
  // 框选结束：选中了一个节点时同步给检查器；框到多个时检查器显示第一个。
  const onSelectionEnd = useCallback(() => {
    const picked = nodes.filter((node): node is FlowNode => node.selected === true && !isGroupNode(node));
    if (picked.length) selectionActions.select({ kind: 'node', id: picked[0].data.selectId, call: call ?? undefined });
  }, [nodes, call]);
  const { fitView, screenToFlowPosition } = useReactFlow();

  // ── 作者编辑（照 NodeCanvas）：右键添加节点、拖端口连线、Delete 删除、Ctrl+C／Ctrl+V 复制粘贴、拖动写布局 ──
  const selectedEdges = useMemo(() => new Set(canvasSelection?.view === view ? canvasSelection.edges : []), [canvasSelection, view]);
  const [picker, setPicker] = useState<{ x: number; y: number; flow: { x: number; y: number }; referenceDrop?: ReferenceNodeDrop } | null>(null);
  const createNode = useNodeCreator(graph.id);
  useEffect(() => { if (!editable) setPicker(null); }, [editable]);
  const menuAt = useRef({ x: 0, y: 0 });
  const shownEdges = useMemo(() => edges.map(edge => selectedEdges.has(edge.id) ? { ...edge, selected: true } : edge), [edges, selectedEdges]);
  // 单击连线选中（Ctrl／Shift 加选）；状态机的转移同时交给检查器。
  const onEdgeClick = useCallback((event: ReactMouseEvent, edge: FlowEdge) => {
    if (machine) selectionActions.select({ kind: 'edge', id: edgeSelectId(graph, edge.data!.edge), call: call ?? undefined });
    if (!editable) return;
    const additive = event.ctrlKey || event.shiftKey;
    const next = new Set(additive ? selectedEdges : []);
    if (!next.delete(edge.id)) next.add(edge.id);
    selectCanvas(additive ? nodes.filter(node => node.selected).map(node => node.id) : [], [...next]);
  }, [editable, machine, graph, call, nodes, selectCanvas, selectedEdges]);
  const portOf = (handle: string | null | undefined) => handle?.slice(handle.indexOf(':') + 1) ?? '';
  const onConnect = useCallback((connection: Connection) => {
    if (!connection.source || !connection.target) return;
    if (machine) {
      if (elements) graphAuthoring.connectStates(graph.id, elements, connection.source, connection.target);
      return;
    }
    graphAuthoring.connect(graph.id, { nodeId: connection.source, portId: portOf(connection.sourceHandle) },
      { nodeId: connection.target, portId: portOf(connection.targetHandle) });
  }, [graph.id, machine, elements]);
  const onReconnect = useCallback((edge: FlowEdge, connection: Connection) => {
    graphAuthoring.reconnect(graph.id, edge.id,
      { nodeId: connection.source, portId: portOf(connection.sourceHandle) },
      { nodeId: connection.target, portId: portOf(connection.targetHandle) });
  }, [graph.id]);
  // 拖动结束写布局：分组写矩形，分组里没有一起拖动的节点按同一位移移动。
  const onNodeDragStop = useCallback((_: unknown, __: CanvasNode, dragged: CanvasNode[]) => {
    if (!editable) return;
    const moved: Record<string, { x: number; y: number }> = Object.fromEntries(dragged.filter(node => !isGroupNode(node))
      .map(node => [node.id, node.position]));
    for (const node of dragged.filter(isGroupNode)) {
      const { rect } = node.data.group;
      const dx = node.position.x - rect.x, dy = node.position.y - rect.y;
      graphAuthoring.updateGroup(graph.id, node.data.group.index, { rect: { ...rect, x: node.position.x, y: node.position.y } });
      for (const laid of layout.nodes)
        if (!moved[laid.node.id] && inside(rect, laid)) moved[laid.node.id] = { x: laid.x + dx, y: laid.y + dy };
    }
    if (Object.keys(moved).length) graphAuthoring.move(graph.id, moved);
  }, [editable, graph.id, layout]);
  const portsOf = useCallback((ids: string[]) => Object.fromEntries(ids.map(id => [id, graph.ports
    .filter(port => port.nodeId === id && port.invocationPath === path)
    .map(port => ({ id: port.id, displayName: port.displayName, direction: port.direction as 'input' | 'output', isTrigger: port.isTrigger }))])), [graph, path]);
  // 系统锚点（入口、出口、任意状态、状态体与片段图的固定节点）不能删除，与 Unity 一致。
  const anchorIds = useMemo(() => new Set(Object.values(elements?.anchors ?? {}).flat().map(anchor => anchor.uid)), [elements]);
  const deleteSelected = () => {
    const picked = nodes.filter(node => node.selected);
    const groupIndices = picked.filter(isGroupNode).map(node => node.data.group.index);
    const ids = picked.filter(node => !isGroupNode(node)).map(node => node.id);
    const kept = ids.filter(id => anchorIds.has(id));
    if (kept.length) consoleActions.log('warn', '作者', `${kept.length} 个系统锚点不能删除，已跳过`);
    const removable = ids.filter(id => !anchorIds.has(id));
    if (removable.length) graphAuthoring.deleteNodes(graph.id, removable);
    if (groupIndices.length) graphAuthoring.deleteGroups(graph.id, groupIndices);
    if (selectedEdges.size) graphAuthoring.disconnect(graph.id, [...selectedEdges]);
    selectCanvas(kept, []);
  };
  const selectedFlowNodes = nodes.filter((node): node is FlowNode => node.selected === true && !isGroupNode(node));
  const copySelected = () => graphAuthoring.copy(graph.id, selectedFlowNodes.map(node => node.id),
    Object.fromEntries(selectedFlowNodes.map(node => [node.id, node.position])), portsOf(selectedFlowNodes.map(node => node.id)));
  const selectCreated = (id: string) => {
    selectCanvas([id], []);
    selectionActions.select({ kind: 'node', id: nodeSelectId(graph, { id, invocationPath: path }) });
  };
  const dropTemplates = (event: ReactDragEvent) => !editable || machine || !elements ||
    (event.target as Element).closest(`.react-flow__node, .react-flow__edge, .react-flow__panel, .${styles.picker}, button, input, textarea`)
    ? [] : elements.referenceNodes.filter(template => template.referenceKind === 'graph' && acceptsReferenceDrag(event.dataTransfer, template.referenceKind));
  const onReferenceDrop = (event: ReactDragEvent) => {
    const templates = dropTemplates(event);
    if (templates.length === 0) return;
    event.preventDefault();
    const reference = readReferenceDrag(event.dataTransfer);
    if (reference.kind === 'rejected') { consoleActions.error('作者', new Error(reference.reason)); return; }
    const offered = templates.filter(template => template.referenceKind === reference.referenceKind);
    if (offered.length === 0) { consoleActions.error('作者', new Error('拖放标记与引用类型不一致，未创建节点')); return; }
    const bounds = event.currentTarget.getBoundingClientRect();
    const at = { x: event.clientX - bounds.left, y: event.clientY - bounds.top,
      flow: screenToFlowPosition({ x: event.clientX, y: event.clientY }) };
    if (offered.length > 1) { setPicker({ ...at, referenceDrop: { reference, templates: offered } }); return; }
    setPicker(null);
    const template = offered[0];
    void createNode(template.type, referenceConfiguration(template, reference), at.flow).then(selectCreated, reason => {
      if (!isAbort(reason)) consoleActions.error('作者', reason);
    });
  };
  const paste = () => {
    const pasted = graphAuthoring.paste(graph.id, { x: 40, y: 40 });
    const [first] = pasted;
    if (first !== undefined) {
      selectCanvas(pasted, []);
      selectionActions.select({ kind: 'node', id: nodeSelectId(graph, { id: first, invocationPath: path }) });
    }
  };
  const onKeyDown = (event: ReactKeyboardEvent) => {
    if (!editable || (event.target as HTMLElement).closest('input, textarea')) return;
    if (event.key === 'Delete') deleteSelected();
    else if ((event.ctrlKey || event.metaKey) && event.key === 'c' && selectedFlowNodes.length && !machine) copySelected();
    else if ((event.ctrlKey || event.metaKey) && event.key === 'v' && !machine) paste();
    else return;
    event.preventDefault();
  };
  // 添加状态：默认配置取宿主对状态类型的描述。
  const addState = async (position: { x: number; y: number }) => {
    try {
      const skill = useAuthoring.getState().target!;
      const description = await authoringApi.describeType(skill, 'BtsmtlSkillNativeState', graph.id, authoringActions.editingView());
      if (!description.availability.available) throw new Error(description.availability.reason ?? '状态类型不可创建');
      const elementsNow = elements ?? await skillElements();
      selectCreated(graphAuthoring.addState(graph.id, elementsNow, description.configuration, position));
    } catch (error) {
      consoleActions.error('作者', error);
    }
  };
  // 分组：框住选中节点（留出标题与边距），没有选中时在右键位置新建。
  const addGroup = () => {
    const picked = layout.nodes.filter(laid => nodes.some(node => node.id === laid.node.id && node.selected));
    const at = screenToFlowPosition(menuAt.current);
    const rect = picked.length ? (() => {
      const measured = (id: string) => nodes.find(node => node.id === id)?.measured;
      const left = Math.min(...picked.map(laid => laid.x)) - 30, top = Math.min(...picked.map(laid => laid.y)) - 50;
      const right = Math.max(...picked.map(laid => laid.x + (measured(laid.node.id)?.width ?? 200))) + 30;
      const bottom = Math.max(...picked.map(laid => laid.y + (measured(laid.node.id)?.height ?? 60))) + 30;
      return { x: left, y: top, width: right - left, height: bottom - top };
    })() : { x: at.x, y: at.y, width: 400, height: 260 };
    graphAuthoring.addGroup(graph.id, rect, '新分组');
  };
  const blocked = '只能编辑顶栏所选技能的图';
  const menu: MenuEntry[] = [
    !editable ? { label: machine ? '添加状态' : '添加节点…', onSelect: () => {}, disabled: true, tip: blocked }
      : machine ? { label: '添加状态', onSelect: () => void addState(screenToFlowPosition(menuAt.current)) }
        : { label: '添加节点…', onSelect: () => setPicker({ ...menuAt.current, flow: screenToFlowPosition(menuAt.current) }) },
    ...(machine ? [] : [
      { label: '复制', disabled: !editable || !selectedFlowNodes.length, tip: editable ? undefined : blocked, onSelect: copySelected },
      { label: '粘贴', disabled: !editable, tip: editable ? undefined : blocked, onSelect: paste },
    ]),
    { label: '新建分组', disabled: !editable, tip: editable ? '框住选中的节点；没有选中时在此处新建' : blocked, onSelect: addGroup },
    { label: '删除所选', disabled: !editable, tip: editable ? undefined : blocked, onSelect: deleteSelected },
    { kind: 'separator' },
    { label: '全部居中', onSelect: () => void fitView({ padding: 0.12, maxZoom: 1, duration: 200 }) },
    { label: '全选', onSelect: () => selectCanvas(nodes.filter(node => !isGroupNode(node)).map(node => node.id), []) },
  ];
  const grabGroup = useCallback((group: CanvasGroup) => selectCanvas(nodes.filter(node =>
    node.id === groupNodeId(group) || (!isGroupNode(node) && inside(group.rect, node.position))).map(node => node.id), []), [nodes, selectCanvas]);
  const renameGroup = useCallback((group: CanvasGroup, name: string) => graphAuthoring.updateGroup(graph.id, group.index, { name }), [graph.id]);
  const resizeGroup = useCallback((group: CanvasGroup, rect: CanvasGroup['rect']) =>
    graphAuthoring.updateGroup(graph.id, group.index, { rect }), [graph.id]);
  const viewOptions = useMemo(() => ({ ...options, editable, grabGroup, renameGroup, resizeGroup }),
    [options, editable, grabGroup, renameGroup, resizeGroup]);

  return (
    <ViewContext.Provider value={viewOptions}>
      <ContextMenu entries={menu}>
      <div className={styles.canvas} tabIndex={-1} onKeyDown={onKeyDown} data-author={editable || undefined}
        onDragOver={event => { if (dropTemplates(event).length > 0) { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; } }}
        onDrop={onReferenceDrop}
        onContextMenuCapture={event => { menuAt.current = { x: event.clientX, y: event.clientY }; }}>
        {/* 照 NodeCanvas：中键拖动平移，左键在空白处拖出框选，滚轮缩放，Ctrl／Shift 加选 */}
        <ReactFlow<CanvasNode, FlowEdge> nodes={nodes} edges={shownEdges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
          onNodesChange={onNodesChange} onNodeClick={onNodeClick} onNodeDoubleClick={onNodeDoubleClick}
          onEdgeClick={onEdgeClick} onPaneClick={() => selectCanvas([], [])}
          onMoveEnd={onMoveEnd} onSelectionEnd={onSelectionEnd} zoomOnDoubleClick={false}
          onConnect={onConnect} onReconnect={onReconnect} edgesReconnectable={editable && !machine}
          onNodeDragStop={onNodeDragStop} nodesDraggable
          panOnDrag={[1]} panActivationKeyCode="Alt" selectionOnDrag selectionMode={SelectionMode.Partial}
          defaultViewport={saved ?? undefined} fitView={!saved} fitViewOptions={readableFocus}
          minZoom={0.1} maxZoom={2} nodesConnectable={editable} edgesFocusable={false} elementsSelectable
          deleteKeyCode={null} selectionKeyCode={null} multiSelectionKeyCode={['Control', 'Shift']} proOptions={{ hideAttribution: true }}
          colorMode="dark">
          <Background id="minor" variant={BackgroundVariant.Lines} gap={20} patternClassName={styles.gridMinor} />
          <Background id="major" variant={BackgroundVariant.Lines} gap={200} patternClassName={styles.gridMajor} />
          {params.minimap && minimapWidth > 0 && (
            <MiniMap className={styles.minimap} style={{ width: minimapWidth, height: minimapWidth * 104 / 204 }} pannable zoomable
              bgColor="#181818" maskColor="rgb(255 255 255 / 4%)"
              maskStrokeColor="#9a9a9a" nodeColor={node => node.type === 'canvasGroup' ?'rgb(255 255 255 / 6%)'
                : (node.data as NodeData).system ? '#8a8a8a' : machine ? '#f0634f' : '#5a5a5a'}
              nodeStrokeWidth={0} />
          )}
        </ReactFlow>
        {picker && <NodeTypePicker graph={graph} at={picker} referenceDrop={picker.referenceDrop} onCreated={selectCreated} onClose={() => setPicker(null)} />}
      </div>
      </ContextMenu>
    </ViewContext.Provider>
  );
}

// 首次显示照本项目 NodeCanvas 的 FocusReadableGraph：缩放 1、以全部节点的中心居中，字号可读；看全图用“全部居中”。
const readableFocus = { minZoom: 1, maxZoom: 1 };

function useNodeState(store: RouteStore, id: string) {
  return useSyncExternalStore(store.subscribe(`node:${id}`), () => store.node(id));
}

// 状态机的状态：NodeCanvas 状态块，正在运行的加黄框，走过的加金色外框。
function StateNodeView({ data, selected: boxed }: NodeProps<FlowNode>) {
  const { laid, index, selectId, system, children } = data;
  const { store, related, editable } = useView();
  // 拖转移时整个状态块都是落点；平时不挡状态的拖动。
  const connecting = useConnection(connection => connection.inProgress);
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId) || boxed;
  const running = state.occurred > 0;
  const faded = related !== null && !related.has(laid.node.id);
  return (
    <div className={styles.state} data-system={system || undefined} data-running={running || undefined}
      data-visited={(state.visited && !running) || undefined} data-selected={selected || undefined} data-faded={faded || undefined}
      title={`${nodeName(laid.node)}\n${laid.node.id}${children.length ? '\n双击进入状态体' : ''}${state.detail ? `\n${state.detail}` : ''}`}>
      <span className={styles.stateId}>#{index}</span>
      {/* 作者模式：从右侧圆点拖出转移，松开在目标状态任意位置 */}
      <Handle id="in:@node" type="target" position={Position.Left} isConnectable={!!editable} isConnectableStart={false}
        className={editable && connecting ? styles.stateTarget : styles.hiddenHandle} />
      <Handle id="out:@node" type="source" position={Position.Right} isConnectable={!!editable}
        className={editable ? styles.stateSource : styles.hiddenHandle} />
      <div className={styles.stateTitle}>{running && <i className={styles.clock} />}{nodeName(laid.node)}</div>
      <NodeGraphLinks data={data} />
    </div>
  );
}

// 分组（NodeCanvas CanvasGroup）：标题可拖动（连同组内节点）、双击改名，作者模式选中后可拉伸。
function GroupView({ data, selected }: NodeProps<GroupNode>) {
  const { group } = data;
  const { editable, grabGroup, renameGroup, resizeGroup } = useView();
  const [naming, setNaming] = useState(false);
  const tint = group.color ?? undefined;
  return (
    <div className={styles.group} data-selected={selected || undefined} style={tint ? { borderColor: tint } : undefined}>
      {editable && (
        <NodeResizer isVisible={!!selected} minWidth={120} minHeight={80} lineClassName={styles.groupResizeLine}
          handleClassName={styles.groupResizeHandle}
          onResizeEnd={(_, size) => resizeGroup?.(group, { x: size.x, y: size.y, width: size.width, height: size.height })} />
      )}
      <div className={styles.groupHead} style={tint ? { background: tint } : undefined}
        onPointerDownCapture={event => { if (editable && event.button === 0 && !naming) grabGroup?.(group); }}
        onDoubleClick={() => { if (editable) setNaming(true); }}>
        {naming ? (
          <TextInput autoFocus defaultValue={group.name} className="nodrag"
            onBlur={event => { setNaming(false); if (event.currentTarget.value !== group.name) renameGroup?.(group, event.currentTarget.value); }}
            onKeyDown={event => {
              if (event.key === 'Enter') event.currentTarget.blur();
              else if (event.key === 'Escape') { event.currentTarget.value = group.name; event.currentTarget.blur(); }
            }} />
        ) : group.name || '分组'}
      </div>
    </div>
  );
}

// 流图节点：NodeCanvas 节点，标题下按行列出输入与输出端口。
function FlowNodeView({ data, selected: boxed }: NodeProps<FlowNode>) {
  const { laid, selectId, children } = data;
  const { store, values, dim, related } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId) || boxed;
  const faded = (dim && !state.occurred) || (related !== null && !related.has(laid.node.id));
  const rows = Math.max(laid.inputs.length, laid.outputs.length);
  const inputs = laid.inputs.filter(port => !port.isTrigger), outputs = laid.outputs.filter(port => !port.isTrigger);
  const triggerIn = laid.inputs.some(port => port.isTrigger), triggerOut = laid.outputs.some(port => port.isTrigger);
  return (
    <div className={styles.node} data-selected={selected || undefined} data-faded={faded || undefined}
      data-occurred={state.occurred || undefined} data-failed={state.failed || undefined}
      title={`${nodeName(laid.node)}\n${laid.node.id}${children.length === 1 ? '\n双击打开引用图' : ''}${state.detail ? `\n${state.detail}` : ''}`}>
      <div className={styles.header}>
        {triggerIn && <i className={styles.flowIn} />}
        <b>{nodeName(laid.node)}</b>
        {triggerOut && <i className={styles.flowOut} />}
        <NodeGraphLinks data={data} />
        <Handle id="in:@node" type="target" position={Position.Left} className={styles.hiddenHandle} isConnectable={false} />
        <Handle id="out:@node" type="source" position={Position.Right} className={styles.hiddenHandle} isConnectable={false} />
      </div>
      {rows > 0 && (
        <div className={styles.body}>
          {laid.inputs.filter(port => port.isTrigger).map(port => <HiddenPort key={`i${port.id}`} port={port} />)}
          {laid.outputs.filter(port => port.isTrigger).map(port => <HiddenPort key={`o${port.id}`} port={port} />)}
          {Array.from({ length: Math.max(inputs.length, outputs.length) }, (_, index) => (
            <div key={index} className={styles.portRow}>
              <PortCell port={inputs[index]} state={state} values={values} />
              <PortCell port={outputs[index]} state={state} values={values} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function NodeGraphLinks({ data }: { data: NodeData }) {
  const { enter, openNode } = useView();
  const { children, laid } = data;
  if (children.length === 0) return null;
  return <span className="nodrag nopan" onPointerDown={event => event.stopPropagation()}
    onDoubleClick={event => event.stopPropagation()}
    onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') event.stopPropagation(); }}>
    {children.length === 1 ? <button type="button" className={styles.openNode}
      title={`打开图：${children[0].ownerName} · ${graphName(children[0])}`}
      aria-label={`打开图：${nodeName(laid.node)}`}
      onClick={event => { event.stopPropagation(); if (event.detail < 2) openNode(data); }}>打开图</button>
      : <MenuButton label="选择引用图" className={styles.openNode} entries={children.map(target => ({
        label: `${target.ownerName} · ${graphName(target)}`, onSelect: () => enter(target, laid.node.id),
      }))}>打开图（{children.length}）</MenuButton>}
  </span>;
}

// 执行端口画在标题两侧的箭头上，连线仍接到对应端口。
function HiddenPort({ port }: { port: GraphPort }) {
  const input = port.direction === 'input';
  const { editable } = useView();
  return <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
    position={input ? Position.Left : Position.Right} isConnectable={!!editable} className={input ? styles.triggerIn : styles.triggerOut} />;
}

function PortCell({ port, state, values }: {
  port: GraphPort | undefined;
  state: ReturnType<RouteStore['node']>;
  values: boolean;
}) {
  const { editable } = useView();
  if (!port) return <span className={styles.portEmpty} />;
  const input = port.direction === 'input';
  const sample = state.ports[`${port.direction}:${port.id}`];
  const color = portColor(sample?.valueType);
  return (
    <span className={input ? styles.portIn : styles.portOut} title={sample ? `${port.id}\n${sample.valueType} · ${sample.value}\n${sample.sequence}` : port.id}
      style={{ ['--pc' as string]: color }}>
      <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
        position={input ? Position.Left : Position.Right} isConnectable={!!editable}
        className={styles.valuePort} data-filled={sample ? true : undefined} />
      <span className={styles.portName}>{port.displayName}</span>
      {values && sample && <span className={styles.portValue}>{sample.value}</span>}
    </span>
  );
}

function RouteEdgeView({ id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data, selected }: EdgeProps<FlowEdge>) {
  const { store, related } = useView();
  const state = useSyncExternalStore(store.subscribe(`edge:${id}`), () => store.edge(id));
  const [path] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
  const status = state.passed ? 'passed' : state.rejected ? 'rejected' : 'idle';
  const faded = related !== null && !related.has(id);
  return (
    <path d={path} className={styles.edge} data-status={status} data-faded={faded || undefined}
      data-selected={state.selected || undefined} data-picked={selected || undefined}>
      <title>{`${data!.edge.id} · ${data!.edge.kind}${state.occurrences ? ` · 本帧 ${state.occurrences} 次` : ''}`}</title>
    </path>
  );
}

// 转移：从源状态边框指向目标状态边框的曲线箭头。
function TransitionEdgeView({ id, source, target, data }: EdgeProps<FlowEdge>) {
  const { store, related } = useView();
  const state = useSyncExternalStore(store.subscribe(`edge:${id}`), () => store.edge(id));
  const from = useInternalNode(source), to = useInternalNode(target);
  const [hover, setHover] = useState(false);
  if (!from || !to) return null;
  const box = (node: typeof from) => ({
    x: node.internals.positionAbsolute.x, y: node.internals.positionAbsolute.y,
    w: node.measured.width ?? 80, h: node.measured.height ?? 26,
  });
  const a = box(from), b = box(to);
  const ax = a.x + a.w / 2, ay = a.y + a.h / 2, bx = b.x + b.w / 2, by = b.y + b.h / 2;
  // 错开同一对状态之间的多条转移：沿垂直方向平移。
  const length = Math.hypot(bx - ax, by - ay) || 1;
  const nx = -(by - ay) / length * data!.offset, ny = (bx - ax) / length * data!.offset;
  const exit = (box: typeof a, cx: number, cy: number, dx: number, dy: number) => {
    const scale = Math.min(Math.abs((box.w / 2 + 6) / (dx || 1e-6)), Math.abs((box.h / 2 + 6) / (dy || 1e-6)));
    return { x: cx + dx * scale, y: cy + dy * scale };
  };
  const start = exit(a, ax + nx, ay + ny, bx - ax, by - ay);
  const end = exit(b, bx + nx, by + ny, ax - bx, ay - by);
  const mx = (start.x + end.x) / 2 + nx * 0.6, my = (start.y + end.y) / 2 + ny * 0.6;
  const path = `M${start.x} ${start.y} Q${mx * 2 - (start.x + end.x) / 2} ${my * 2 - (start.y + end.y) / 2} ${end.x} ${end.y}`;
  const angle = Math.atan2(end.y - my, end.x - mx);
  const arrow = `M${end.x} ${end.y} L${end.x - 11 * Math.cos(angle - 0.45)} ${end.y - 11 * Math.sin(angle - 0.45)} L${end.x - 11 * Math.cos(angle + 0.45)} ${end.y - 11 * Math.sin(angle + 0.45)}Z`;
  const status = state.passed ? 'live' : state.traveled ? 'route' : 'idle';
  const highlighted = hover || (related !== null && related.has(id));
  const faded = related !== null && !related.has(id) && status === 'idle';
  return (
    <g className={styles.transition} data-status={status} data-hi={highlighted || undefined} data-faded={faded || undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}>
      <BaseEdge path={path} interactionWidth={12} />
      <path d={arrow} className={styles.arrow} />
    </g>
  );
}
