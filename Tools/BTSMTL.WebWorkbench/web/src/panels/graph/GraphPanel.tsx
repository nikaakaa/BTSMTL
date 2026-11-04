import {
  Background, BackgroundVariant, BaseEdge, EdgeLabelRenderer, getBezierPath, Handle, MiniMap, NodeResizer, Position, ReactFlow,
  ReactFlowProvider, SelectionMode, ViewportPortal, applyNodeChanges, useConnection, useInternalNode, type ConnectionLineComponentProps, useReactFlow, useStore as useFlowStore, type Edge, type EdgeProps,
  type Node, type NodeChange, type Connection, type InternalNode, type NodeProps, type Viewport, type XYPosition,
} from '@xyflow/react';
import '@xyflow/react/dist/base.css';
import {
  createContext, memo, useCallback, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore,
  type DragEvent as ReactDragEvent, type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent, type PointerEvent as ReactPointerEvent,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, authoringTargetKey, describedPortsAvailable, type AuthoringTarget, type NodeTypeInfo, type ReferenceNodeTemplate, type SkillDocumentGraph, type SkillElements } from '../../api/authoring';
import { isAbort } from '../../api/http';
import type { PlayerJobId } from '../../api/playerRecords';
import type { ExecutionCall, GraphDefinition, GraphEdge, GraphPort, RouteFrame } from '../../api/types';
import {
  applyGraphEdits, authoringActions, authoringDocument, currentDocument, edgeStep, graphAuthoring, graphGroups, graphPath, layoutPath, ownsAuthoringGraph, useAuthoring, useCurrentDocument, useSkillElements,
  useDocumentGraphHierarchy, useDocumentObservationBinding, type CanvasGroup,
} from '../../state/authoring';
import { cloneJson, setIn, type JsonObject, type JsonValue } from '../../state/skillJson';
import { acceptsReferenceDrag, readReferenceDrag, type ReferenceDrag } from '../../state/referenceSelection';
import { usePanelId, usePanelParams } from '../../shell/panel';
import { useFrameReport } from '../../shell/readiness';
import {
  callKey, callLabel, edgeSelectId, graphCallsAtFrame, graphCrumbs, graphKey, graphName, graphRecords, graphReferrers,
  isEdgeOccurrence, isStateMachine, layoutGraph, loopScopeLabel, matchesDocumentCall, nodeReferences, nodeName, nodeSelectId, nodeTypeName, parentCallMatches, parentGraphCalls, portColor, portSampleKey, portSampleLabel,
  documentObservationNotice, recordedGraphCalls, routeObservation, type GraphHierarchy, type GraphNavigationTarget, type NodeNavigationTarget, type LaidNode, type RecordedGraphCall,
} from '../../state/graphModel';
import { projectActions } from '../../state/project';
import { consoleActions, getState, selectionActions, useWorkbench, type GraphNavigationOrigin } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, ContextMenu, MenuButton, Select, TextInput, Tip, type MenuEntry } from '../../ui/controls';
import { CloseIcon, SubgraphIcon, TimelineIcon } from '../../ui/icons';
import { BlackboardFields } from '../inspector/FieldEditors';
import { RouteStore, type RouteHistory } from './routeStore';
import { QuickGraphRoute, useQuickPlan } from '../timeline/QuickPreview';
import { ObservationRecords } from '../records/records';
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
  observations: boolean;
  dim: boolean;
  relations: boolean;
  minimap: boolean;
  labels: 'brief' | 'all' | 'none';
}
const defaults: Params = {
  graph: null, call: null, pin: null, caller: null, origin: null, viewport: null, values: true, observations: true, dim: false, relations: false, minimap: false, labels: 'brief',
};

interface ViewOptions {
  store: RouteStore;
  observationScope: string;
  values: boolean;
  dim: boolean;
  related: Set<string> | null;
  enter(graph: GraphNavigationTarget, nodeId?: string): void;
  openReference(target: NodeNavigationTarget, nodeId: string): void;
  openNode(data: NodeData): void;
  hierarchy: GraphHierarchy;
  graph: GraphDefinition;
  // 连了线的端口：`节点|方向:端口`。
  connected: Set<string>;
  planned: ReadonlySet<string>;
  // 作者模式下端口可拖出连线。
  editable?: boolean;
  // 按住分组标题拖动前选中分组及其中的节点，一起拖动。
  grabGroup?(group: CanvasGroup): void;
  renameGroup?(group: CanvasGroup, name: string): void;
  resizeGroup?(group: CanvasGroup, rect: CanvasGroup['rect']): void;
  labels: Params['labels'];
  // 状态机作者模式：在状态上按住右键拖到目标状态建立转移。
  beginLink?(source: string, event: ReactPointerEvent): void;
}
const ViewContext = createContext<ViewOptions | null>(null);
const useView = () => useContext(ViewContext)!;

interface NodeData extends Record<string, unknown> {
  laid: LaidNode;
  selectId: string;
  children: NodeNavigationTarget[];
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
  label: string;
  // 线上的条件图：转移的条件、组合步骤出口的步骤条件，均按作者 JSON 读取。
  conditions: GraphNavigationTarget[];
  // 同一对状态之间多条转移时的错开量。
  offset: number;
  // 连着入口、出口或任意状态的转移：数量多、信息少，平时画淡。
  minor: boolean;
  // 流图连线从执行端口出发：画执行色；否则按源端口的值类型着色。
  trigger: boolean;
}
type FlowEdge = Edge<EdgeData, 'route' | 'transition'>;

export function GraphPanel() {
  const opened = useAuthoring(useShallow(state => ({ target: state.target,
    loading: authoringDocument(state).loading, error: authoringDocument(state).documentError,
    graphs: authoringDocument(state).documentGraphs, programId: authoringDocument(state).programId })));
  const [params, setParams] = usePanelParams<Params>(defaults);
  const panelId = usePanelId();
  const request = useWorkbench(state => state.graphRequest);
  const handled = useRef(0);
  useEffect(() => {
    if (!request || request.panelId !== panelId || request.sequence === handled.current) return;
    handled.current = request.sequence;
    setParams({ graph: request.key, call: request.observation === null ? null : callKey(request.observation.call),
      pin: request.observation === null ? null : { ...request.observation, graph: request.key },
      caller: request.caller, origin: request.origin, viewport: null });
  }, [request, panelId, setParams]);
  if (!opened.target) return <ReportEmpty text="选择要编辑的内容" />;
  if (opened.target.kind === 'scenario') return <ReportEmpty text="此场景方案没有技能节点图" />;
  if (opened.loading || !opened.graphs) return <Empty text="读取中" />;
  if (!opened.graphs.length) return <ReportEmpty text={opened.error ?? '此技能没有节点图'} />;
  const graphs = opened.graphs;
  const selected = params.graph === null ? graphs.find(graph => graph.id === opened.programId)
    : graphs.find(graph => graphKey(graph) === params.graph);
  if (selected === undefined) return <div className={styles.root}>
    <PanelBar><Select label="作者图" value={null} placeholder="选择作者图"
      options={graphs.map(graph => ({ value: graphKey(graph), label: `${graph.ownerName} · ${graphName(graph)}` }))}
      onChange={key => {
        const graph = graphs.find(item => graphKey(item) === key)!;
        void projectActions.openGraph(graph, undefined, { origin: null, observation: null, caller: null, panelId })
          .catch(reason => consoleActions.error('作者', reason));
      }} /></PanelBar>
    <ReportEmpty text={params.graph === null ? '当前技能没有声明根执行图，请选择要查看的作者图。'
      : '原先选择的图已不在当前作者文档中；原选择已保留，请明确选择要查看的作者图。'} />
  </div>;
  return <GraphView graphs={graphs} defaultGraph={selected} params={params} setParams={setParams} />;
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

const graphCallKey = ({ graph, call }: RecordedGraphCall) => JSON.stringify([graphKey(graph), callKey(call)]);

function GraphView({ graphs, defaultGraph, params, setParams }: {
  graphs: SkillDocumentGraph[]; defaultGraph: SkillDocumentGraph; params: Params; setParams(patch: Partial<Params>): void;
}) {
  const binding = useDocumentObservationBinding();
  const panelId = usePanelId();
  const runId = useWorkbench(state => state.runId);
  const observation = useWorkbench(useShallow(routeObservation));
  const frameSource = useWorkbench(state => state.frame?.source ?? null);
  const routeFrames = useWorkbench(state => state.routeFrames);
  const sourceNotice = observation.kind === 'ready' && (frameSource === null || frameSource.content === null)
    ? '当前帧未记录正式采用的内容来源，运行叠加不可用' : null;
  const currentRoute = observation.kind === 'ready' && sourceNotice === null ? observation.route : null;
  const hierarchy = useDocumentGraphHierarchy();

  const currentCalls = useMemo(() => recordedGraphCalls(currentRoute === null ? [] : [currentRoute], graphs, binding), [currentRoute, graphs, binding]);
  const followed = params.graph === null && currentCalls.length === 1 ? graphKey(currentCalls[0].graph) : null;
  const graph = graphs.find(item => graphKey(item) === (params.graph ?? followed)) ?? defaultGraph;
  const key = graphKey(graph);

  const caller = params.caller;
  const callerNotice = caller === null ? null : caller.runId !== runId ? '原父调用属于另一份运行档案，未选择其它实例'
    : documentObservationNotice(currentRoute, binding, caller.call);
  const graphCalls = callerNotice !== null ? [] : currentCalls.filter(item => graphKey(item.graph) === key &&
    (caller === null || parentCallMatches(caller.call, item.call)));
  const calls = graphCalls.map(item => item.call);
  const pin = params.pin;
  const pinNotice = pin === null ? null : pin.runId !== runId ? '固定调用属于另一份运行档案，固定身份已保留'
    : pin.graph !== key ? '固定调用不属于当前作者图，固定身份已保留'
    : documentObservationNotice(currentRoute, binding, pin.call);
  const automatic = params.graph === null ? currentCalls.length === 1 ? currentCalls[0].call : null : calls.length === 1 ? calls[0] : null;
  const quickMode = useWorkbench(state => state.previewMode === 'quick');
  const observedCall = quickMode || currentRoute === null || pinNotice !== null || callerNotice !== null ? null
    : pin !== null ? calls.find(item => callKey(item) === callKey(pin.call)) ?? null
      : params.call !== null ? calls.find(item => callKey(item) === params.call) ?? null : automatic;
  const shownCall = pin === null ? observedCall : pin.call;
  const availableCalls = params.graph === null ? currentCalls : graphCalls;
  const choices = quickMode ? [] : pin && !availableCalls.some(item => callKey(item.call) === callKey(pin.call)) ? [{ graph, call: pin.call }, ...availableCalls] : availableCalls;
  const callOptions = choices.map(item => ({ value: graphCallKey(item),
    label: `${params.graph === null ? graphName(item.graph) + ' · ' : ''}${callLabel(item.call)}` }));
  const path = graph.authoringPath;
  const togglePin = pin ? () => setParams({ pin: null, call: null, caller: null }) : observedCall === null || runId === null ? undefined
    : () => setParams({ graph: key, call: callKey(observedCall), pin: { runId, graph: key, call: observedCall } });
  const callNotice = quickMode ? null : pinNotice ?? callerNotice ?? sourceNotice ?? documentObservationNotice(currentRoute, binding)
    ?? (observation.kind === 'unavailable' ? observation.reason
      : observedCall !== null ? calls.some(item => callKey(item) === callKey(observedCall)) ? null : '此帧没有固定调用的发生记录，固定身份已保留'
        : params.call !== null ? '此帧没有所选调用的记录，请重新选择实例或开启跟随当前帧'
          : availableCalls.length > 1 ? '本帧有多个实际调用，请选择要观察的实例'
            : caller !== null ? '此帧没有精确匹配原父调用的子调用记录' : '本帧没有对应作者图的实际调用记录');
  const availabilityNotice = currentRoute?.availability.filter(item => !item.available || !item.complete).map(item => item.reason).join('\n');
  const objectAvailability = observedCall?.objectAvailability;
  const objectNotice = observedCall === null ? null : objectAvailability == null ? '未记录对象来源资格'
    : objectAvailability.reason || (!objectAvailability.available || !objectAvailability.complete
      ? `${objectAvailability.channel} · Available=${objectAvailability.available} · Complete=${objectAvailability.complete}` : null);
  const loadedCalls = useMemo(() => recordedGraphCalls(routeFrames, graphs, binding), [routeFrames, graphs, binding]);
  const recorded = useMemo(() => graphCallsAtFrame(loadedCalls, currentRoute, currentCalls), [loadedCalls, currentRoute, currentCalls]);
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
    if (selection?.panelId === panelId && (selection.kind === 'node' && graph.nodes.some(node => nodeSelectId(graph, node) === selection.id) ||
      selection.kind === 'edge' && graph.edges.some(edge => edgeSelectId(graph, edge) === selection.id))) {
      const previous = selection.call ? callKey(selection.call) : null;
      const next = observedCall ? callKey(observedCall) : null;
      if (previous !== next || selection.call === undefined) selectionActions.select({ ...selection, call: observedCall });
    }
  }, [graph, observedCall, panelId]);

  const enter = useCallback((target: GraphNavigationTarget, nodeId?: string) => {
    const source = hierarchy.graphs.get(key)!;
    const observation = pin !== null ? { runId: pin.runId, call: pin.call }
      : observedCall === null || runId === null ? null : { runId, call: observedCall };
    const origin = nodeId === undefined ? null : { owner: source.owner, ownerName: source.ownerName, graphId: source.id, nodeId, observation };
    void projectActions.openGraph(target, undefined, { origin, observation: null, caller: observation, panelId })
      .catch(reason => consoleActions.error('作者', reason));
  }, [hierarchy, key, observedCall, pin, runId, panelId]);
  const crumbs = useMemo(() => graphCrumbs(hierarchy, graph), [hierarchy, graph]);
  const referrers = useMemo(() => graphReferrers(hierarchy, graph), [hierarchy, graph]);
  const selection = useWorkbench(state => state.selection?.panelId === panelId ? state.selection : null);
  const selectedNode = selection?.kind === 'node' ? graph.nodes.find(node => nodeSelectId(graph, node) === selection.id) : undefined;
  const selectedEdge = selection?.kind === 'edge' ? graph.edges.find(edge => edgeSelectId(graph, edge) === selection.id) : undefined;
  const observedCallKey = observedCall === null ? null : callKey(observedCall);
  const selectedRecords = useMemo(() => graphRecords(currentRoute, graph, binding).filter(record =>
    callKey(record.call) === observedCallKey && (selectedNode !== undefined
      ? isEdgeOccurrence(record) ? record.sourceNodeId === selectedNode.id : record.nodeId === selectedNode.id
      : selectedEdge !== undefined && isEdgeOccurrence(record) && record.edgeId === selectedEdge.id)),
  [currentRoute, graph, binding, observedCallKey, selectedNode, selectedEdge]);
  const [blackboard, setBlackboard] = useState(false);
  const graphOptions = [...hierarchy.graphs].map(([value, item]) => ({ value, label: `${item.ownerName} · ${graphName(item)}` }));

  const settings: MenuEntry[] = [
    { kind: 'check', label: '跟随当前帧', checked: params.graph === null, onChange: on => setParams({ graph: on ? null : key, call: null, pin: null, caller: null, origin: null }) },
    { kind: 'separator' },
    { kind: 'check', label: '端口值', checked: params.values, onChange: values => setParams({ values }) },
    { kind: 'check', label: '所选节点／连线的实际记录', checked: params.observations, onChange: observations => setParams({ observations }) },
    { kind: 'check', label: '压暗本帧没走到的节点', checked: params.dim, onChange: dim => setParams({ dim }) },
    { kind: 'check', label: '只突出所选节点的关系', checked: params.relations, onChange: relations => setParams({ relations }) },
    { kind: 'check', label: '小地图', checked: params.minimap, onChange: minimap => setParams({ minimap }) },
    { kind: 'separator' },
    { kind: 'check', label: '连线标签：简写', checked: params.labels === 'brief', onChange: () => setParams({ labels: 'brief' }) },
    { kind: 'check', label: '连线标签：全文', checked: params.labels === 'all', onChange: () => setParams({ labels: 'all' }) },
    { kind: 'check', label: '连线标签：隐藏', checked: params.labels === 'none', onChange: () => setParams({ labels: 'none' }) },
  ];

  return (
    // 视图状态随所看的图与图实例重建，保存的视图位置才能生效。
    <div className={styles.root}>
      <ReactFlowProvider key={`${key}|${path}`}>
        <PanelBar wrap>
          {callNotice && <span role="status" className={styles.notice}>{callNotice}</span>}
          {!quickMode && availabilityNotice && <Tip content={availabilityNotice}><span role="status" className={styles.notice}>运行记录有缺项</span></Tip>}
          {objectNotice !== null && <Tip content={objectNotice}><span role="status" className={styles.notice}>{objectNotice}</span></Tip>}
          {graphOptions.length > 1 && <Select label="作者图" value={key} options={graphOptions}
            onChange={value => {
              void projectActions.openGraph(hierarchy.graphs.get(value)!, undefined, { origin: null, observation: null, caller: null, panelId })
                .catch(reason => consoleActions.error('作者', reason));
            }} />}
          <span className={styles.crumbs}>
            {callCrumbs !== null ? <>
              {callCrumbs.parent && <span className={styles.crumb}>
                {callCrumbs.choices.length > 1 && enterCall ? <MenuButton label="选择父调用" entries={callCrumbs.choices.map(item => ({
                  label: `${graphName(item.graph)} · ${callLabel(item.call)}`, onSelect: () => enterCall(item),
                }))}>选择父调用</MenuButton>
                  : <Tip content={`路径：${callCrumbs.parent.parentInvocationPath}\n代次：${callCrumbs.parent.parentInvocationGeneration ?? '未记录'}`}><span>
                    {callCrumbs.parent.parentInvocationGeneration === null ? '父调用代次未记录' : '已加载记录中没有父调用'}
                  </span></Tip>}
                <i className={styles.crumbArrow} />
              </span>}
              {callCrumbs.items.map((item, index) => <Tip key={callKey(item.call)} content={callLabel(item.call)}><span className={styles.crumb}>
                {index > 0 && <i className={styles.crumbArrow} />}
                {index < callCrumbs.items.length - 1 && enterCall
                  ? <button className={styles.crumbLink} onClick={() => enterCall(item)}>{graphName(item.graph)}</button>
                  : <span data-current>{graphName(item.graph)}</span>}
              </span></Tip>)}
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
            void projectActions.openGraph({ owner: source.owner, ownerName: source.ownerName, id: source.graphId }, source.nodeId,
              { origin: null, observation: source.observation, caller: null, panelId })
              .catch(reason => consoleActions.error('作者', reason));
          }}>返回引用节点</Button>}
          {referrers.length > 0 && <MenuButton label="已加载引用" caret entries={referrers.map(({ source }) => ({
            label: `${source.ownerName} · ${source.file} · ${source.nodeId} · ${source.fieldId}`,
            onSelect: () => {
              void projectActions.openGraph({ owner: source.owner, ownerName: source.ownerName, id: source.graphId }, source.nodeId,
                { origin: null, observation: null, caller: null, panelId })
                .catch(reason => consoleActions.error('作者', reason));
            },
          }))}>已加载引用（{referrers.length}）</MenuButton>}
          {callOptions.length > 1 && <Select label="实际调用" value={observedCall ? graphCallKey({ graph, call: observedCall }) : null}
            options={callOptions} placeholder="选择实际调用"
            tip={observedCall ? callLabel(observedCall) : '本帧有多个调用，选择要观察的实例'} onChange={selectCall} />}
          {shownCall !== null && <Button variant="ghost" pressed={pin !== null} disabled={togglePin === undefined}
            tip={`${pin ? '解除固定，按当前帧选择调用' : '固定这次实际调用，拖动时间游标时保留其身份'}\n${callLabel(shownCall)}`}
            onClick={togglePin}>固定调用</Button>}
          <FitButton />
          <Button variant="ghost" pressed={blackboard} tip="本图的黑板变量与声明" onClick={() => setBlackboard(!blackboard)}>黑板</Button>
          <MenuButton label="显示设置" caret entries={settings}>显示设置</MenuButton>
        </PanelBar>
        <QuickGraphRoute graph={graph} />
        {/* 图名只在面包屑里出现一次；黑板浮层贴在画布右上 */}
        <div className={styles.stage}>
          {blackboard && <BlackboardPanel graph={graph} onClose={() => setBlackboard(false)} />}
          <AuthoredCanvas graph={graph} call={observedCall}
            route={currentRoute} hierarchy={hierarchy} enter={enter} params={params} setParams={setParams} />
          {!quickMode && params.observations && (selectedNode !== undefined || selectedEdge !== undefined) &&
            <div className={styles.observationDock}>
              <PanelBar><span>{selectedNode === undefined ? '分支决策' : nodeName(selectedNode)} · 实际记录</span>
                <Button variant="ghost" tip="收起实际记录" onClick={() => setParams({ observations: false })}><CloseIcon /></Button>
              </PanelBar>
              {callNotice !== null && <div className={styles.notice} role="status">{callNotice}</div>}
              <ObservationRecords key={JSON.stringify([runId, frameSource?.execution, frameSource?.content,
                currentRoute?.hostFrame, key, observedCallKey, selectedNode?.id, selectedEdge?.id])}
                records={selectedRecords} />
            </div>}
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
    const request = { key, items };
    typeCatalog = request;
    void items.catch(() => { if (typeCatalog === request) typeCatalog = null; });
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
        : await authoringApi.describeConfiguration(skill, type, configuration, graphId, authoringActions.editingView(), controller.signal);
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
                <Tip key={item.key} content={item.clrType ?? undefined}>
                  <button className={styles.pickerItem} disabled={busy} onClick={() => void add(item)}>
                    <span>{item.type}</span>
                    <em>{item.detail}</em>
                  </button>
                </Tip>
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
  return !nodeReferences(hierarchy, graph, laid.node.id).length;
}

// 连线的条件：转移存在连线的 Payload 上，组合步骤存在出口所属的步骤上。
function edgeCondition(document: JsonValue | null, edgeId: string, machine: boolean) {
  if (!machine) {
    const step = edgeStep(document, edgeId);
    return step && { condition: (step.step.m_Condition as string | null | undefined) ?? null, step };
  }
  const connection = (document ? ((document as JsonObject).allNodes as JsonObject[]).flatMap(node => node.outConnections as JsonObject[]) : [])
    .find(item => item.UID === edgeId);
  return connection ? { condition: ((connection.Payload as JsonObject).m_Condition as string | null | undefined) ?? null, step: null } : null;
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
  route: RouteFrame | null;
  hierarchy: GraphHierarchy;
  enter(graph: GraphNavigationTarget, nodeId?: string): void;
  params: Params;
  setParams(patch: Partial<Params>): void;
  editable: boolean;
  groups: CanvasGroup[];
  elements: SkillElements | null;
}

function GraphCanvas({ graph, call, route, hierarchy, enter, params, setParams, editable, groups, elements }: GraphCanvasProps) {
  const plan = useQuickPlan();
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  const planned = useMemo(() => new Set(plan?.segments.flatMap(segment => segment.stateSource !== null &&
    segment.stateSource.graphId === graph.id && segment.start <= seconds && seconds < segment.end
    ? [segment.stateSource.nodeId] : [])), [plan, graph.id, seconds]);
  const path = graph.authoringPath;
  const binding = useDocumentObservationBinding();
  const panelId = usePanelId();
  const view = `${graphKey(graph)}|${path}`;
  const canvasSelection = useWorkbench(state => state.canvasSelections[panelId]);
  const selectCanvas = useCallback((nodes: readonly string[], edges: readonly string[]) =>
    selectionActions.selectCanvas(panelId, { view, nodes, edges }), [panelId, view]);
  const runId = useWorkbench(state => state.runId);
  const minimapWidth = useFlowStore(state => Math.min(204, state.width * 0.3, state.height * 0.3 * 204 / 104));
  // 缩得很小时字已经读不清：只留节点名与连线，隐藏转移标签与端口值。
  const far = useFlowStore(state => state.transform[2] < 0.6);
  const machine = isStateMachine(graph) || (currentDocument(graphPath(graph.id)) as JsonObject | null)?.Role === 'StateMachine';
  const layout = useMemo(() => layoutGraph(graph, path), [graph, path]);
  const store = useMemo(() => new RouteStore(), []);
  const routeFrames = useWorkbench(state => state.routeFrames);
  const observedCallKey = call === null ? null : callKey(call);
  const generation = useWorkbench(state => state.generation);
  const contentRevision = useWorkbench(state => state.summary?.contentRevision ?? null);
  const sourceContent = useWorkbench(state => state.frame?.source?.content ?? null);
  const observationScope = JSON.stringify([runId, generation, contentRevision, sourceContent, graphKey(graph), route?.hostFrame, observedCallKey]);
  const history = useMemo(() => {
    const visited = new Map<string, number>(), traveled = new Map<string, number>();
    if (machine && observedCallKey !== null)
      for (const frame of routeFrames) {
        const matches = (item: typeof frame.nodes[number] | typeof frame.edges[number]) => item.call !== null && item.graphId === graph.id &&
          matchesDocumentCall(item.call, binding) && callKey(item.call) === observedCallKey;
        for (const node of frame.nodes)
          if ((node.kind === 'NodeEntered' || node.kind === 'StateScopeEntered') && matches(node)) {
            const previous = visited.get(node.nodeId);
            if (previous === undefined || frame.seconds < previous) visited.set(node.nodeId, frame.seconds);
          }
        for (const edge of frame.edges)
          if (edge.passed && matches(edge)) {
            const previous = traveled.get(edge.edgeId);
            if (previous === undefined || frame.seconds < previous) traveled.set(edge.edgeId, frame.seconds);
          }
      }
    return { visited, traveled };
  }, [routeFrames, graph.id, binding, observedCallKey, machine]);
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
    return [...groupNodes, ...layout.nodes.map((laid): FlowNode => ({
      id: laid.node.id, type: machine ? 'state' : 'flow', position: { x: laid.x, y: laid.y }, selected: selected.has(laid.node.id),
      measured: measured.get(laid.node.id),
      data: { laid, selectId: nodeSelectId(graph, laid.node), children: nodeReferences(hierarchy, graph, laid.node.id) },
    }))];
  }, [layout, hierarchy, graph, machine, groups, canvasSelection, view]);
  const [nodes, setNodes] = useState<CanvasNode[]>(() => build([]));
  // 作者修改后图结构或分组变化：重建节点，保留选中。
  const built = useRef(build);
  useEffect(() => {
    if (built.current === build) return;
    built.current = build;
    setNodes(build);
  }, [build]);
  const { document: authored } = useCurrentDocument(graphPath(graph.id));
  const edges = useMemo<FlowEdge[]>(() => {
    const pairs = new Map<string, number>();
    const counts = new Map<string, number>();
    const pairKey = (edge: GraphEdge) => [edge.source!.nodeId, edge.target!.nodeId].sort().join('|');
    for (const edge of layout.edges) counts.set(pairKey(edge), (counts.get(pairKey(edge)) ?? 0) + 1);
    const system = new Set(machine ? layout.nodes.filter(laid => isSystemNode(graph, hierarchy, laid)).map(laid => laid.node.id) : []);
    return layout.edges.map(edge => {
      const id = edgeCondition(authored, edge.id, machine)?.condition ?? null;
      const found = id === null ? undefined : [...hierarchy.graphs.values()].find(item => item.owner === graph.owner && item.id === id);
      const conditions = found ? [found] : [];
      const label = conditions.map(graphName).join(' · ');
      if (machine) {
        const pair = pairKey(edge);
        const index = pairs.get(pair) ?? 0;
        pairs.set(pair, index + 1);
        return {
          id: edge.id, type: 'transition', source: edge.source!.nodeId, target: edge.target!.nodeId,
          sourceHandle: 'out:@node', targetHandle: 'in:@node',
          data: { edge, conditions, label, offset: (index - (counts.get(pair)! - 1) / 2) * 16,
            minor: system.has(edge.source!.nodeId) || system.has(edge.target!.nodeId), trigger: false },
        };
      }
      const source = layout.nodes.find(item => item.node.id === edge.source!.nodeId)!;
      const target = layout.nodes.find(item => item.node.id === edge.target!.nodeId)!;
      const output = source.outputs.find(port => port.id === edge.source!.portId);
      return {
        id: edge.id, type: 'route', source: edge.source!.nodeId, target: edge.target!.nodeId,
        sourceHandle: output ? `out:${edge.source!.portId}` : 'out:@node',
        targetHandle: target.inputs.some(port => port.id === edge.target!.portId) ? `in:${edge.target!.portId}` : 'in:@node',
        data: { edge, conditions, label, offset: 0, minor: false, trigger: output?.isTrigger ?? true },
      };
    });
  }, [layout, graph, machine, hierarchy, authored]);

  useEffect(() => {
    const records = graphRecords(route, graph, binding).filter(item => callKey(item.call) === observedCallKey);
    const shown: RouteHistory = { visited: new Set(), traveled: new Set() };
    if (route !== null) {
      for (const [id, seconds] of history.visited) if (seconds <= route.seconds) shown.visited.add(id);
      for (const [id, seconds] of history.traveled) if (seconds <= route.seconds) shown.traveled.add(id);
    }
    store.update(records, shown, observationScope);
  }, [store, graph, route, binding, observedCallKey, history, observationScope]);
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

  const connected = useMemo(() => new Set(layout.edges.flatMap(edge =>
    [`${edge.source!.nodeId}|output:${edge.source!.portId}`, `${edge.target!.nodeId}|input:${edge.target!.portId}`])), [layout]);
  const openReference = useCallback((target: NodeNavigationTarget, nodeId: string) => {
    const observation = params.pin !== null ? { runId: params.pin.runId, call: params.pin.call }
      : call === null || runId === null ? null : { runId, call };
    const origin = { owner: graph.owner, ownerName: graph.ownerName, graphId: graph.id, nodeId, observation };
    void projectActions.openReference(target, origin, panelId).catch(reason => consoleActions.error('作者', reason));
  }, [graph.owner, graph.ownerName, graph.id, call, params.pin, runId, panelId]);
  const openNode = useCallback((data: NodeData) => {
    if (data.children.length === 1) openReference(data.children[0], data.laid.node.id);
  }, [openReference]);
  const options = useMemo<ViewOptions>(() => ({
    store, observationScope,
    values: params.values, dim: params.dim, related, labels: params.labels, enter, openReference, openNode, hierarchy, graph, connected, planned,
  }), [store, observationScope, params.values, params.dim, related, params.labels, enter, openReference, openNode, hierarchy, graph, connected, planned]);
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
    if (!isGroupNode(node)) selectionActions.select({ kind: 'node', id: node.data.selectId, call, panelId });
  }, [call, editable, selectCanvas, panelId]);
  const onNodeDoubleClick = useCallback((_: unknown, node: CanvasNode) => {
    if (isGroupNode(node)) return;
    openNode(node.data);
  }, [openNode]);
  // 双击连线进入条件图；没有时新建（步骤条件默认成立），网页新建的图保存后才能进入。
  const openEdge = useCallback((edge: FlowEdge) => {
    if (edge.data!.conditions.length) { enter(edge.data!.conditions[0]); return; }
    const owner = editable && elements ? edgeCondition(currentDocument(graphPath(graph.id)), edge.id, machine) : null;
    if (!owner || !elements) return;
    if (owner.condition === null) {
      const creation = owner.step ? graphAuthoring.createStepCondition(graph.id, owner.step)
        : graphAuthoring.createCondition(graph.id, edge.id);
      void creation.then(() => consoleActions.log('info', '作者', '已新建条件图'), error => consoleActions.error('作者', error));
    }
  }, [enter, editable, elements, graph.id, machine]);
  const onEdgeDoubleClick = useCallback((_: unknown, edge: FlowEdge) => openEdge(edge), [openEdge]);
  const saved = params.viewport?.view === view ? params.viewport : null;
  const onMoveEnd = useCallback((_: unknown, viewport: Viewport) => setParams({ viewport: { ...viewport, view } }), [setParams, view]);
  // 框选结束：选中了一个节点时同步给检查器；框到多个时检查器显示第一个。
  const onSelectionEnd = useCallback(() => {
    const picked = nodes.filter((node): node is FlowNode => node.selected === true && !isGroupNode(node));
    if (picked.length) selectionActions.select({ kind: 'node', id: picked[0].data.selectId, call, panelId });
  }, [nodes, call, panelId]);
  const { fitView, screenToFlowPosition } = useReactFlow();

  // ── 作者编辑（照 NodeCanvas）：右键添加节点、拖端口连线、Delete 删除、Ctrl+C／Ctrl+V 复制粘贴、拖动写布局 ──
  const selectedEdges = useMemo(() => new Set(canvasSelection?.view === view ? canvasSelection.edges : []), [canvasSelection, view]);
  const [picker, setPicker] = useState<{ x: number; y: number; flow: { x: number; y: number }; referenceDrop?: ReferenceNodeDrop } | null>(null);
  const createNode = useNodeCreator(graph.id);
  useEffect(() => { if (!editable) setPicker(null); }, [editable]);
  const menuAt = useRef({ x: 0, y: 0 });
  const shownEdges = useMemo(() => edges.map(edge => selectedEdges.has(edge.id) ? { ...edge, selected: true } : edge), [edges, selectedEdges]);
  // 单击连线选中（Ctrl／Shift 加选，预览与作者模式都可选），同时交给检查器。
  const onEdgeClick = useCallback((event: ReactMouseEvent, edge: FlowEdge) => {
    selectionActions.select({ kind: 'edge', id: edgeSelectId(graph, edge.data!.edge), call, panelId });
    const additive = event.ctrlKey || event.shiftKey;
    const next = new Set(additive ? selectedEdges : []);
    if (!next.delete(edge.id)) next.add(edge.id);
    selectCanvas(additive ? nodes.filter(node => node.selected).map(node => node.id) : [], [...next]);
  }, [graph, call, nodes, selectCanvas, selectedEdges, panelId]);
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
  // 执行端口只接执行端口，数据端口只接数据端口；不连自己。
  const isValidConnection = useCallback((connection: Connection | FlowEdge) => {
    const trigger = (nodeId: string, handle: string | null | undefined) =>
      graph.ports.find(port => port.nodeId === nodeId && port.id === portOf(handle))?.isTrigger ?? true;
    return connection.source !== connection.target
      && trigger(connection.source, connection.sourceHandle) === trigger(connection.target, connection.targetHandle);
  }, [graph]);
  const connecting = useConnection(connection => connection.inProgress);
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
      selectCreated(await graphAuthoring.addState(graph.id, description.configuration, position));
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
  // 右键连线（线或线上的条件名）：打开或新建条件、删除条件、删除连线，排在画布菜单前面。
  const [menuEdgeId, setMenuEdgeId] = useState<string | null>(null);
  const menuEdge = menuEdgeId === null ? null : edges.find(edge => edge.id === menuEdgeId) ?? null;
  const menuOwner = menuEdge && editable ? edgeCondition(authored, menuEdge.id, machine) : null;
  // 只有转移和组合步骤的连线带条件；数据线与普通执行线只能删除。
  const conditional = !!menuEdge && (machine || menuEdge.data!.conditions.length > 0 || menuOwner !== null);
  const edgeMenu: MenuEntry[] = !menuEdge ? [] : [
    ...(!conditional ? [] : [
      {
        label: menuEdge.data!.conditions.length || menuOwner?.condition ? '打开条件' : '新建条件',
        disabled: !menuEdge.data!.conditions.length && !(menuOwner && elements),
        tip: menuEdge.data!.conditions.length || menuOwner ? undefined : blocked,
        onSelect: () => openEdge(menuEdge),
      },
      {
        label: '删除条件', disabled: !menuOwner?.condition, tip: editable ? undefined : blocked,
        onSelect: () => {
          const condition = menuOwner!.condition!;
          if (menuOwner!.step) graphAuthoring.removeStepCondition(graph.id, menuOwner!.step, condition);
          else graphAuthoring.removeCondition(graph.id, menuEdge.id, condition);
        },
      },
    ]),
    { label: '删除连线', disabled: !editable, tip: editable ? undefined : blocked, onSelect: () => graphAuthoring.disconnect(graph.id, [menuEdge.id]) },
    { kind: 'separator' },
  ];
  const menu: MenuEntry[] = [
    ...edgeMenu,
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
  // 右键拖出转移：移动超过 4px 才算拖动，松开在另一个状态上建立转移；拖过之后不弹右键菜单。
  const [link, setLink] = useState<{ source: string; at: XYPosition } | null>(null);
  const linked = useRef(false);
  const beginLink = useCallback((source: string, event: ReactPointerEvent) => {
    if (!elements) return;
    linked.current = false;
    const startX = event.clientX, startY = event.clientY;
    const move = (pointer: PointerEvent) => {
      if (!linked.current && Math.hypot(pointer.clientX - startX, pointer.clientY - startY) < 4) return;
      linked.current = true;
      setLink({ source, at: screenToFlowPosition({ x: pointer.clientX, y: pointer.clientY }) });
    };
    const up = (pointer: PointerEvent) => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      setLink(null);
      if (!linked.current) return;
      const target = document.elementFromPoint(pointer.clientX, pointer.clientY)?.closest('.react-flow__node-state')?.getAttribute('data-id');
      if (target && target !== source) graphAuthoring.connectStates(graph.id, elements, source, target);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }, [elements, graph.id, screenToFlowPosition]);
  const viewOptions = useMemo(() => ({
    ...options, editable, grabGroup, renameGroup, resizeGroup, beginLink: editable && machine ? beginLink : undefined,
  }), [options, editable, grabGroup, renameGroup, resizeGroup, machine, beginLink]);

  return (
    <ViewContext.Provider value={viewOptions}>
      <ContextMenu entries={menu}>
      <div className={styles.canvas} tabIndex={-1} onKeyDown={onKeyDown} data-author={editable || undefined} data-far={far || undefined}
        data-linking={link !== null || undefined} data-connecting={connecting || undefined}
        onDragOver={event => { if (dropTemplates(event).length > 0) { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; } }}
        onDrop={onReferenceDrop}
        onContextMenuCapture={event => {
          if (linked.current) {
            linked.current = false;
            event.preventDefault();
            event.stopPropagation();
            return;
          }
          menuAt.current = { x: event.clientX, y: event.clientY };
          setMenuEdgeId((event.target as Element).closest('[data-edge]')?.getAttribute('data-edge') ?? null);
        }}>
        {/* 照 NodeCanvas：中键拖动平移，左键在空白处拖出框选，滚轮缩放，Ctrl／Shift 加选 */}
        <ReactFlow<CanvasNode, FlowEdge> nodes={nodes} edges={shownEdges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
          onNodesChange={onNodesChange} onNodeClick={onNodeClick} onNodeDoubleClick={onNodeDoubleClick}
          onEdgeClick={onEdgeClick} onPaneClick={() => selectCanvas([], [])}
          onMoveEnd={onMoveEnd} onSelectionEnd={onSelectionEnd} zoomOnDoubleClick={false}
          onConnect={onConnect} onReconnect={onReconnect} edgesReconnectable={editable && !machine}
          isValidConnection={isValidConnection} connectionLineComponent={ConnectionLineView}
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
              bgColor={themeColor('--bg-0')} maskColor="rgb(255 255 255 / 4%)" maskStrokeColor={themeColor('--graph-edge')}
              nodeColor={node => themeColor(node.type === 'canvasGroup' ? '--group-line' : machine ? '--state' : '--node-head')}
              nodeStrokeWidth={0} />
          )}
          {link && <LinkPreview source={link.source} at={link.at} />}
        </ReactFlow>
        {picker && <NodeTypePicker graph={graph} at={picker} referenceDrop={picker.referenceDrop} onCreated={selectCreated} onClose={() => setPicker(null)} />}
      </div>
      </ContextMenu>
    </ViewContext.Provider>
  );
}

// 小地图画在 SVG 属性里，属性不解析 CSS 变量，取主题的实际值。
const themeColor = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

// 首次显示照本项目 NodeCanvas 的 FocusReadableGraph：缩放 1、以全部节点的中心居中，字号可读；看全图用“全部居中”。
const readableFocus = { minZoom: 1, maxZoom: 1 };

function useNodeState(store: RouteStore, id: string) {
  const { observationScope } = useView();
  return useSyncExternalStore(store.subscribe(`node:${id}`), () => store.node(id, observationScope));
}

// 状态机的状态：照 Animator 的圆角块，本帧所在的状态描黄并写本地秒数，走过的描金。
function StateNodeView({ data, selected: boxed }: NodeProps<FlowNode>) {
  const { laid, selectId, children } = data;
  const { store, related, beginLink, planned } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId) || boxed;
  const running = state.occurred > 0;
  const faded = related !== null && !related.has(laid.node.id);
  return (
    <Tip name={nodeName(laid.node)} content={`${laid.node.id}${children.length ? '\n双击进入状态体' : ''}${state.detail ? `\n${state.detail}` : ''}`}>
    <div className={styles.state} data-running={running || undefined}
      data-visited={(state.visited && !running) || undefined} data-selected={selected || undefined} data-faded={faded || undefined}
      data-planned={planned.has(laid.node.id) || undefined}
      onPointerDown={event => { if (event.button === 2) beginLink?.(laid.node.id, event); }}>
      <Handle id="in:@node" type="target" position={Position.Left} isConnectable={false} className={styles.hiddenHandle} />
      <Handle id="out:@node" type="source" position={Position.Right} isConnectable={false} className={styles.hiddenHandle} />
      <div className={styles.stateTitle} data-name>{nodeName(laid.node)}</div>
    </div>
    </Tip>
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
  const { store, values, dim, related, planned } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId) || boxed;
  const faded = (dim && !state.occurred) || (related !== null && !related.has(laid.node.id));
  const inputs = laid.inputs.filter(port => !port.isTrigger), outputs = laid.outputs.filter(port => !port.isTrigger);
  const triggerIns = laid.inputs.filter(port => port.isTrigger), triggerOuts = laid.outputs.filter(port => port.isTrigger);
  // 一侧只有一个执行端口时压在标题栏边框上；多个（组合节点的步骤）时各占一行写名字，从上到下即执行顺序。
  const headIns = triggerIns.length === 1 ? triggerIns : [], headOuts = triggerOuts.length === 1 ? triggerOuts : [];
  const left = [...(triggerIns.length > 1 ? triggerIns : []), ...inputs], right = [...(triggerOuts.length > 1 ? triggerOuts : []), ...outputs];
  const rows = Math.max(left.length, right.length);
  return (
    <div className={styles.node} data-selected={selected || undefined} data-faded={faded || undefined}
      data-occurred={state.occurred || undefined} data-failed={state.failed || undefined}
      data-planned={planned.has(laid.node.id) || undefined}
      data-openable={children.length > 0 || undefined}>
      <Tip name={nodeName(laid.node)}
        content={`${nodeTypeName(laid.node) ? `${nodeTypeName(laid.node)}\n` : ''}${laid.node.id}${children.length === 1 ? `\n双击打开${children[0].kind === 'timeline' ? ' Timeline' : '引用图'}` : ''}${state.detail ? `\n${state.detail}` : ''}`}>
        <div className={styles.header}>
          <b data-name>{nodeName(laid.node)}</b>
          <Handle id="in:@node" type="target" position={Position.Left} className={styles.hiddenHandle} isConnectable={false} />
          <Handle id="out:@node" type="source" position={Position.Right} className={styles.hiddenHandle} isConnectable={false} />
          {headIns.map(port => <FlowPort key={`i${port.id}`} port={port} occurred={state.occurred > 0} />)}
          {headOuts.map(port => <FlowPort key={`o${port.id}`} port={port} occurred={state.occurred > 0} />)}
        </div>
      </Tip>
      <NodeGraphLinks data={data} />
      {rows > 0 && (
        <div className={styles.body}>
          {Array.from({ length: rows }, (_, index) => (
            <div key={index} className={styles.portRow}>
              <PortCell port={left[index]} state={state} values={values} />
              <PortCell port={right[index]} state={state} values={values} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// 标题栏右上的进入按钮：引用一张图时直接打开，引用多张时列出选择。
function NodeGraphLinks({ data }: { data: NodeData }) {
  const { openReference, openNode } = useView();
  const { children, laid } = data;
  if (children.length === 0) return null;
  return <span className="nodrag nopan" onPointerDown={event => event.stopPropagation()}
    onDoubleClick={event => event.stopPropagation()}
    onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') event.stopPropagation(); }}>
    {children.length === 1
      ? <Tip content={`打开${children[0].kind === 'timeline' ? ' Timeline' : '引用图'}：${children[0].ownerName} · ${graphName(children[0])}`}>
        <button type="button" className={styles.openButton} aria-label={`打开${children[0].kind === 'timeline' ? ' Timeline' : '引用图'}：${graphName(children[0])}`}
          onClick={event => { event.stopPropagation(); if (event.detail < 2) openNode(data); }}>
          {children[0].kind === 'timeline' ? <TimelineIcon width={12} height={12} /> : <SubgraphIcon width={12} height={12} />}
        </button>
      </Tip>
      : <MenuButton label={`选择引用内容（${children.length}）`} iconOnly className={styles.openButton} entries={children.map(target => ({
        label: `${target.ownerName} · ${graphName(target)}`, onSelect: () => openReference(target, laid.node.id),
      }))}><SubgraphIcon width={12} height={12} /></MenuButton>}
  </span>;
}

// 执行端口：压在标题栏两侧边框上的三角，连线接在三角中心；与数据端口同一规则，连了线实心，本帧经过变黄。
function FlowPort({ port, occurred }: { port: GraphPort; occurred: boolean }) {
  const input = port.direction === 'input';
  const { editable, connected } = useView();
  return (
    <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
      position={input ? Position.Left : Position.Right} isConnectable={!!editable} className={styles.flowPort}
      data-filled={connected.has(`${port.nodeId}|${port.direction}:${port.id}`) || undefined} data-occurred={occurred || undefined}>
      <svg viewBox="0 0 10 12"><path d="M1.5 1.5 8.5 6l-7 4.5z" /></svg>
    </Handle>
  );
}

function PortCell({ port, state, values }: {
  port: GraphPort | undefined;
  state: ReturnType<RouteStore['node']>;
  values: boolean;
}) {
  if (!port) return <span className={styles.portEmpty} />;
  const input = port.direction === 'input';
  if (port.isTrigger) return (
    <span className={input ? styles.portIn : styles.portOut}>
      <FlowPort port={port} occurred={state.occurred > 0} />
      <span className={styles.portName} data-name>{port.displayName}</span>
    </span>
  );
  return <ValuePortCell port={port} state={state} values={values} />;
}

function ValuePortCell({ port, state, values }: { port: GraphPort; state: ReturnType<RouteStore['node']>; values: boolean }) {
  const { editable, connected, observationScope } = useView();
  const [choice, setChoice] = useState<{ scope: string; key: string } | null>(null);
  const samples = state.ports[`${port.direction}:${port.id}`] ?? [];
  const selected = choice?.scope === observationScope ? choice.key : null;
  const sample = selected === null ? samples.length === 1 ? samples[0] : undefined
    : samples.find(item => portSampleKey(item) === selected);
  const input = port.direction === 'input';
  const color = portColor(sample?.valueType);
  // 照 Shader Graph：连了线实心；没连线的输入在旁边写当前值。
  const linked = connected.has(`${port.nodeId}|${port.direction}:${port.id}`);
  const inline = input && !linked;
  return (
    <Tip name={port.displayName} content={sample
      ? `${port.id}\n${sample.valueType} · ${sample.value}\n${portSampleLabel(sample)}\nSourceAvailable=${sample.sourceAvailable} · ${sample.reason}\n` +
        (sample.loopScopes === null ? sample.loopScopeAvailability?.reason ?? '循环范围未记录' : sample.loopScopes.map(loopScopeLabel).join('\n')) +
        `\n${sample.activationAvailability?.reason ?? ''}`
      : `${port.id}\n${samples.length > 1 ? '本帧有多个样本，请选择具体记录' : 'Unavailable：当前没有对应端口观察'}`}>
      <span className={input ? styles.portIn : styles.portOut} style={{ ['--pc' as string]: color }}>
        <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
          position={input ? Position.Left : Position.Right} isConnectable={!!editable}
          className={styles.valuePort} data-filled={linked || undefined} />
        <span className={styles.portName} data-name>{port.displayName}</span>
        {samples.length > 1 && (values || inline) && <span className={`nodrag nopan ${styles.sampleChoice}`} onClick={event => event.stopPropagation()}>
          <Select label={`${port.displayName}的实际样本`} value={sample === undefined ? null : portSampleKey(sample)}
            placeholder={`${samples.length} 次观察`} options={samples.map(item => ({ value: portSampleKey(item),
              label: portSampleLabel(item) + (item.loopScopes === null ? ` · ${item.loopScopeAvailability?.reason ?? '循环来源未记录'}`
                : item.loopScopes.length === 0 ? ' · 无循环范围' : ` · ${item.loopScopes.map(loopScopeLabel).join(' → ')}`) }))}
            onChange={key => setChoice({ scope: observationScope, key })} />
        </span>}
        {sample && (values || inline) && <span className={styles.portValue} data-inline={inline || undefined}
          data-unavailable={!sample.sourceAvailable || undefined}>{sample.value}</span>}
      </span>
    </Tip>
  );
}

// 端口颜色：执行端口用执行色，数据端口按采样值类型（与端口圆圈一致）。
function usePortColor(nodeId: string, handleId: string | null | undefined) {
  const { store, graph } = useView();
  const input = handleId?.startsWith('in:') ?? false;
  const portId = handleId?.slice(handleId.indexOf(':') + 1) ?? '';
  const samples = useNodeState(store, nodeId).ports[`${input ? 'input' : 'output'}:${portId}`] ?? [];
  const types = new Set(samples.map(sample => sample.valueType));
  const trigger = graph.ports.find(port => port.nodeId === nodeId && port.id === portId)?.isTrigger ?? true;
  return trigger ? 'var(--port-exec)' : portColor(types.size === 1 ? samples[0]?.valueType : undefined);
}

// 拖线：与出发端口同色，端点随鼠标；吸附到能接的端口时贴上去，停在接不上的端口时变红。
function ConnectionLineView({ fromNode, fromHandle, fromX, fromY, toX, toY, fromPosition, toPosition, connectionStatus, toHandle }:
  ConnectionLineComponentProps<CanvasNode>) {
  const color = usePortColor(fromNode.id, fromHandle.id);
  const [path] = getBezierPath({ sourceX: fromX, sourceY: fromY, sourcePosition: fromPosition, targetX: toX, targetY: toY, targetPosition: toPosition });
  return (
    <g className={styles.connectionLine} style={{ ['--ec' as string]: color }} data-status={connectionStatus ?? undefined}>
      <path d={path} />
      {!toHandle && <circle cx={toX} cy={toY} r={4} />}
    </g>
  );
}

// 流图连线：执行线用执行色，数据线与源端口同色（Shader Graph）。
function RouteEdgeView({ id, source, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data, selected }: EdgeProps<FlowEdge>) {
  const { store, related, labels, enter, observationScope } = useView();
  const state = useSyncExternalStore(store.subscribe(`edge:${id}`), () => store.edge(id, observationScope));
  const color = usePortColor(source, data!.trigger ? null : `out:${data!.edge.source!.portId}`);
  const [path, labelX, labelY] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
  const status = state.passed ? 'passed' : state.rejected ? 'rejected' : 'idle';
  const faded = related !== null && !related.has(id);
  // 组合节点步骤的条件挂在从该步骤出发的线上：线中点写条件图名，双击线或名字进入条件图。
  const condition = data!.conditions[0];
  return (
    <>
      <Tip content={`${data!.edge.id} · ${data!.edge.kind}${state.occurrences ? ` · 本帧 ${state.occurrences} 次\n${state.detail}` : ''}`}>
        <g data-edge={id}>
          <path d={path} className={styles.edgeHit} />
          <path d={path} className={styles.edge} style={{ ['--ec' as string]: color }} data-status={status} data-faded={faded || undefined}
            data-selected={state.selected || undefined} data-picked={selected || undefined} />
        </g>
      </Tip>
      {condition && labels !== 'none' && (
        <EdgeLabelRenderer>
          <Tip name={data!.label} content="双击进入条件图">
            <div className={styles.edgeLabel} data-condition data-faded={faded || undefined} data-name data-edge={id}
              style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
              onDoubleClick={() => enter(condition)}>
              {data!.label}
            </div>
          </Tip>
        </EdgeLabelRenderer>
      )}
    </>
  );
}

const stateBox = (node: InternalNode) => ({
  x: node.internals.positionAbsolute.x, y: node.internals.positionAbsolute.y,
  w: node.measured.width ?? 80, h: node.measured.height ?? 26,
});
type StateBox = ReturnType<typeof stateBox>;

// 从 (cx, cy) 沿 (dx, dy) 方向射出，落在状态边框外 2px 处。
function borderPoint(box: StateBox, cx: number, cy: number, dx: number, dy: number): XYPosition {
  const scale = Math.min(Math.abs((box.w / 2 + 2) / (dx || 1e-6)), Math.abs((box.h / 2 + 2) / (dy || 1e-6)));
  return { x: cx + dx * scale, y: cy + dy * scale };
}

// 画在 mid 处、指向 start→end 方向的三角箭头。
function midArrow(start: XYPosition, end: XYPosition, mid: XYPosition) {
  const span = Math.hypot(end.x - start.x, end.y - start.y) || 1;
  const ux = (end.x - start.x) / span, uy = (end.y - start.y) / span;
  const tipX = mid.x + ux * 5, tipY = mid.y + uy * 5;
  return `M${tipX} ${tipY} L${tipX - ux * 10 - uy * 4.5} ${tipY - uy * 10 + ux * 4.5} L${tipX - ux * 10 + uy * 4.5} ${tipY - uy * 10 - ux * 4.5}Z`;
}

// 右键拖出转移时跟着鼠标的线，样子同提亮的转移。
function LinkPreview({ source, at }: { source: string; at: XYPosition }) {
  const node = useInternalNode(source);
  if (!node) return null;
  const box = stateBox(node);
  const cx = box.x + box.w / 2, cy = box.y + box.h / 2;
  const start = borderPoint(box, cx, cy, at.x - cx, at.y - cy);
  return (
    <ViewportPortal>
      <svg className={styles.linkPreview}>
        <path d={`M${start.x} ${start.y} L${at.x} ${at.y}`} />
        <path d={midArrow(start, at, { x: (start.x + at.x) / 2, y: (start.y + at.y) / 2 })} className={styles.arrow} />
      </svg>
    </ViewportPortal>
  );
}

// 转移：从源状态边框到目标状态边框的曲线，照 Animator 把箭头画在线的中点，多条转移汇到同一状态时箭头不堆在一起；
// 标签取条件图名称，放在靠源状态的三分之一处，不盖住箭头。
function TransitionEdgeView({ id, source, target, data, selected }: EdgeProps<FlowEdge>) {
  const { store, related, labels, enter, observationScope } = useView();
  const state = useSyncExternalStore(store.subscribe(`edge:${id}`), () => store.edge(id, observationScope));
  const from = useInternalNode(source), to = useInternalNode(target);
  const [hover, setHover] = useState(false);
  if (!from || !to) return null;
  const a = stateBox(from), b = stateBox(to);
  const ax = a.x + a.w / 2, ay = a.y + a.h / 2, bx = b.x + b.w / 2, by = b.y + b.h / 2;
  // 错开同一对状态之间的多条转移：沿垂直方向平移。
  const length = Math.hypot(bx - ax, by - ay) || 1;
  const nx = -(by - ay) / length * data!.offset, ny = (bx - ax) / length * data!.offset;
  const start = borderPoint(a, ax + nx, ay + ny, bx - ax, by - ay);
  const end = borderPoint(b, bx + nx, by + ny, ax - bx, ay - by);
  // 二次曲线 t=0.5 处正好是 (mx, my)，切线方向与起点指向终点一致。
  const mx = (start.x + end.x) / 2 + nx * 0.6, my = (start.y + end.y) / 2 + ny * 0.6;
  const cx = mx * 2 - (start.x + end.x) / 2, cy = my * 2 - (start.y + end.y) / 2;
  const path = `M${start.x} ${start.y} Q${cx} ${cy} ${end.x} ${end.y}`;
  const arrow = midArrow(start, end, { x: mx, y: my });
  const at = (t: number) => ({
    x: (1 - t) ** 2 * start.x + 2 * t * (1 - t) * cx + t ** 2 * end.x,
    y: (1 - t) ** 2 * start.y + 2 * t * (1 - t) * cy + t ** 2 * end.y,
  });
  const label = at(0.3);
  const status = state.passed ? 'live' : state.traveled ? 'route' : 'idle';
  const highlighted = hover || (related !== null && related.has(id));
  const faded = related !== null && !related.has(id) && status === 'idle';
  const showLabel = labels !== 'none' && data!.label && (labels === 'all' || highlighted || status !== 'idle');
  return (
    <g className={styles.transition} data-edge={id} data-status={status} data-hi={highlighted || undefined} data-faded={faded || undefined}
      data-picked={selected || undefined} data-minor={data!.minor || undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}>
      <BaseEdge path={path} interactionWidth={12} />
      <path d={arrow} className={styles.arrow} />
      {showLabel && (
        <EdgeLabelRenderer>
          <Tip name={data!.label} content="双击进入条件图">
            <div className={styles.edgeLabel} data-status={status} data-hi={highlighted || undefined} data-name data-edge={id}
              style={{ transform: `translate(-50%, -50%) translate(${label.x}px, ${label.y}px)` }}
              onDoubleClick={() => { if (data!.conditions[0]) enter(data!.conditions[0]); }}>
              {data!.label}
            </div>
          </Tip>
        </EdgeLabelRenderer>
      )}
    </g>
  );
}
