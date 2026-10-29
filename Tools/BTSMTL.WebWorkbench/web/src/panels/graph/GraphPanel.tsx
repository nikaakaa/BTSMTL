import {
  Background, BackgroundVariant, BaseEdge, EdgeLabelRenderer, getBezierPath, Handle, MiniMap, Position, ReactFlow,
  ReactFlowProvider, SelectionMode, applyNodeChanges, useInternalNode, useReactFlow, type Edge, type EdgeProps, type Node, type NodeChange,
  type Connection, type NodeProps, type Viewport,
} from '@xyflow/react';
import '@xyflow/react/dist/base.css';
import {
  createContext, memo, useCallback, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore,
  type KeyboardEvent as ReactKeyboardEvent, type MouseEvent as ReactMouseEvent,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type NodeTypeInfo } from '../../api/authoring';
import type { GraphDefinition, GraphEdge, GraphPort } from '../../api/types';
import { applyGraphEdits, graphAuthoring, useAuthoring } from '../../state/authoring';
import { openPanel } from '../../shell/layout';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import {
  belongsTo, callKey, childGraphs, edgeChildGraphs, graphCrumbs, graphHierarchy, graphKey, graphName, graphRecords,
  isStateMachine, layoutGraph, nodeName, nodeSelectId, nodeTimeline, playedTimeline, portColor, type GraphHierarchy, type LaidNode,
} from '../../state/graphModel';
import { consoleActions, getState, selectionActions, useWorkbench } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, ContextMenu, MenuButton, Select, TextInput, type MenuEntry } from '../../ui/controls';
import { RouteStore, type RouteHistory } from './routeStore';
import styles from './graph.module.css';

// 节点图面板（D4）：照 NodeCanvas Canvas。状态机画成状态块与转移箭头，流图画成带端口的节点。
// 双击节点进入子图（调用的状态机、状态体），双击转移进入条件图，双击播放 Timeline 的节点跳到 Timeline；
// 顶部面包屑逐级返回。路线状态只更新状态变了的节点与连线（D9）。

interface Params {
  graph: string | null;
  call: string | null;
  // 视图位置按所看的图与图实例保存。
  viewport: (Viewport & { view: string }) | null;
  values: boolean;
  dim: boolean;
  relations: boolean;
  minimap: boolean;
  labels: 'brief' | 'all' | 'none';
}
const defaults: Params = {
  graph: null, call: null, viewport: null, values: true, dim: false, relations: false, minimap: true, labels: 'brief',
};

interface ViewOptions {
  store: RouteStore;
  values: boolean;
  dim: boolean;
  related: Set<string> | null;
  labels: Params['labels'];
  enter(graph: GraphDefinition): void;
  hierarchy: GraphHierarchy;
  graph: GraphDefinition;
  // 作者模式下端口可拖出连线。
  editable?: boolean;
}
const ViewContext = createContext<ViewOptions | null>(null);
const useView = () => useContext(ViewContext)!;

interface NodeData extends Record<string, unknown> {
  laid: LaidNode;
  index: number;
  selectId: string;
  system: boolean;
  timeline: string | null;
  children: GraphDefinition[];
}
type FlowNode = Node<NodeData, 'flow' | 'state'>;
interface EdgeData extends Record<string, unknown> {
  edge: GraphEdge;
  label: string;
  conditions: GraphDefinition[];
  // 同一对状态之间多条转移时的错开量。
  offset: number;
}
type FlowEdge = Edge<EdgeData, 'route' | 'transition'>;

export function GraphPanel() {
  const { runId, graphs, availability } = useWorkbench(useShallow(state => ({
    runId: state.runId, graphs: state.graphs, availability: state.graphAvailability,
  })));
  if (!runId) return <ReportEmpty text="选择一个运行" />;
  if (!graphs) return <ReportEmpty text="读取中" />;
  if (!graphs.length) return <ReportEmpty text={availability.find(item => item.channel === 'graph-definitions')?.reason || '本次运行没有节点图'} />;
  return <GraphView graphs={graphs} />;
}

// 当前帧绘制后报告就绪；空状态也算已按当前帧绘制。
function useFrameReport(dependency: unknown) {
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  useEffect(() => {
    if (!visible) return;
    let handle = 0;
    const report = () => {
      panelWindow.cancelAnimationFrame(handle);
      handle = panelWindow.requestAnimationFrame(() => {
        const state = getState();
        const key = frameKey(state.runId, state.frameIndex);
        if (key) reportRendered(panelId, key);
      });
    };
    report();
    const unsubscribe = useWorkbench.subscribe((state, previous) => {
      if (state.frameKey !== previous.frameKey || state.frameIndex !== previous.frameIndex) report();
    });
    return () => { unsubscribe(); panelWindow.cancelAnimationFrame(handle); };
  }, [visible, panelWindow, panelId, dependency]);
  useEffect(() => () => clearRendered(panelId), [panelId]);
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

const programOf = (graph: GraphDefinition) => JSON.stringify([graph.actorId, graph.programId, graph.programRevision]);

function GraphView({ graphs }: { graphs: GraphDefinition[] }) {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const hierarchy = useMemo(() => graphHierarchy(graphs), [graphs]);
  const programs = useMemo(() => {
    const result = new Map<string, { label: string; root: GraphDefinition }>();
    for (const graph of graphs) {
      const key = programOf(graph);
      const crumbs = graphCrumbs(hierarchy, graph);
      const root = crumbs[0]?.graph ?? graph;
      if (!result.has(key) || root === graph) result.set(key, { label: `${graph.actorId} · ${graph.programId}`, root });
    }
    return result;
  }, [graphs, hierarchy]);

  // 未选图时跟随本帧：优先有路线记录的状态机，其次第一张有记录的图。
  const followed = useWorkbench(state => {
    if (params.graph) return null;
    const route = state.frame?.route;
    if (!route) return null;
    const items = [...route.nodes, ...route.edges];
    const active = graphs.filter(graph => items.some(item => item.graphId === graph.id && belongsTo(item.call, graph)));
    const graph = active.find(isStateMachine) ?? active[0];
    return graph ? graphKey(graph) : null;
  });
  const graph = graphs.find(item => graphKey(item) === (params.graph ?? followed)) ?? programs.values().next().value!.root;
  const key = graphKey(graph);

  // 本帧的实际调用；没有记录时按图实例列出。
  const calls = useWorkbench(useShallow(state => {
    const result: string[] = [];
    for (const item of graphRecords(state.frame, graph)) {
      const id = callKey(item.call);
      if (!result.includes(id)) result.push(id);
    }
    return result;
  }));
  const paths = useMemo(() => [...new Set(graph.nodes.map(node => node.invocationPath))], [graph]);
  const callOptions = calls.length
    ? calls.map((id, index) => ({ value: id, label: `调用 ${index + 1}` }))
    : paths.map((path, index) => ({ value: path, label: `图实例 ${index + 1}` }));
  const call = callOptions.some(option => option.value === params.call) ? params.call! : callOptions[0]?.value ?? null;
  const path = call === null ? null : calls.includes(call) ? (JSON.parse(call) as string[])[3] : call;

  const enter = useCallback((target: GraphDefinition) => setParams({ graph: graphKey(target), call: null, viewport: null }), [setParams]);

  // 其它面板请求显示某张图（检查器跳转、Timeline 片段）。
  const request = useWorkbench(state => state.graphRequest);
  const handled = useRef(request?.sequence ?? 0);
  useEffect(() => {
    if (!request || request.sequence === handled.current) return;
    handled.current = request.sequence;
    if (graphs.some(item => graphKey(item) === request.key)) setParams({ graph: request.key, call: null, viewport: null });
  }, [request, graphs, setParams]);
  const crumbs = useMemo(() => graphCrumbs(hierarchy, graph), [hierarchy, graph]);
  const programOptions = [...programs].map(([value, item]) => ({ value, label: item.label }));

  const settings: MenuEntry[] = [
    { kind: 'check', label: '跟随当前帧', checked: params.graph === null, onChange: on => setParams({ graph: on ? null : key, call: null }) },
    { kind: 'separator' },
    { kind: 'check', label: '连线标签：简写', checked: params.labels === 'brief', onChange: () => setParams({ labels: 'brief' }) },
    { kind: 'check', label: '连线标签：全文', checked: params.labels === 'all', onChange: () => setParams({ labels: 'all' }) },
    { kind: 'check', label: '连线标签：隐藏', checked: params.labels === 'none', onChange: () => setParams({ labels: 'none' }) },
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
        <PanelBar>
          <Select label="程序" value={programOf(graph)} options={programOptions}
            onChange={value => enter(programs.get(value)!.root)} />
          <span className={styles.crumbs}>
            {crumbs.map((crumb, index) => (
              <span key={index} className={styles.crumb}>
                {index > 0 && <i className={styles.crumbArrow} />}
                {crumb.graph && crumb.graph !== graph
                  ? <button className={styles.crumbLink} onClick={() => enter(crumb.graph!)}>{crumb.label}</button>
                  : <span data-current={crumb.graph === graph || undefined}>{crumb.label}</span>}
              </span>
            ))}
          </span>
          <Select label="实际调用" value={call} options={callOptions} disabled={callOptions.length < 2}
            tip={callOptions.length ? undefined : '此图没有节点'} onChange={value => setParams({ call: value })} />
          <FitButton />
          <MenuButton label="显示设置" caret entries={settings}>显示设置</MenuButton>
        </PanelBar>
        <div className={styles.title}>
          {graphName(graph)}
          <SelectedSummary graph={graph} />
        </div>
        {!graph.nodes.length ? <ReportEmpty text="此图未保留作者节点来源" />
          : path === null ? <ReportEmpty text="此调用未保留可显示的节点来源" />
            : <AuthoredCanvas graph={graph} path={path} call={calls.includes(call!) ? call : null}
              hierarchy={hierarchy} enter={enter} params={params} setParams={setParams} />}
      </ReactFlowProvider>
    </div>
  );
}

// 添加节点的类型选择：照 NodeCanvas 的右键节点浏览器，搜索宿主按实际 C# 类型发现的全部可创建类型。
let typeCatalog: { skill: string; items: Promise<NodeTypeInfo[]> } | null = null;
function loadTypes(skill: string) {
  if (typeCatalog?.skill !== skill) {
    const items = (async () => {
      const result: NodeTypeInfo[] = [];
      for (let offset = 0; ; offset += 128) {
        const page = await authoringApi.types(skill, offset);
        if (!page.availability.available) throw new Error(page.availability.reason ?? '类型查询不可用');
        result.push(...page.items);
        if (!page.hasMore) return result;
      }
    })();
    typeCatalog = { skill, items };
    items.catch(() => { typeCatalog = null; });
  }
  return typeCatalog.items;
}

function NodeTypePicker({ graph, at, onClose }: {
  graph: GraphDefinition; at: { x: number; y: number; flow: { x: number; y: number } }; onClose(): void;
}) {
  const skill = useAuthoring(state => state.skill)!;
  const [types, setTypes] = useState<NodeTypeInfo[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    loadTypes(skill).then(setTypes, (reason: unknown) => setError(reason instanceof Error ? reason.message : String(reason)));
  }, [skill]);
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const shown = (types ?? []).filter(item => words.every(word =>
    `${item.type} ${item.kind ?? ''} ${item.family} ${item.timelineDomains ?? ''}`.toLowerCase().includes(word)));
  const add = async (item: NodeTypeInfo) => {
    setBusy(true);
    try {
      const description = await authoringApi.describeType(skill, item.type, graph.id);
      if (!description.availability.available) throw new Error(description.availability.reason ?? `${item.type} 不可创建`);
      if (!description.ports.availability.available)
        consoleActions.log('warn', '作者', `${item.type} 的端口暂不可用：${description.ports.availability.reason ?? ''}`);
      const ports = (description.ports.items ?? []).map(entry => ({
        id: entry.port.ID, displayName: entry.port.name, direction: entry.direction, isTrigger: entry.port.type === 'Flow',
      }));
      graphAuthoring.addNode(graph.id, item.type, description.configuration, ports, at.flow);
      onClose();
    } catch (reason) {
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
        <button className={styles.pickerClose} onClick={onClose} aria-label="关闭">×</button>
      </div>
      <div className={styles.pickerList}>
        {error ? <div className={styles.pickerNote}>{error}</div>
          : !types ? <div className={styles.pickerNote}>读取节点类型中</div>
            : !shown.length ? <div className={styles.pickerNote}>没有匹配的类型</div>
              : shown.map(item => (
                <button key={item.type} className={styles.pickerItem} disabled={busy} title={item.clrType}
                  onClick={() => void add(item)}>
                  <span>{item.type}</span>
                  <em>{[item.kind, item.timelineDomains].filter(Boolean).join(' · ')}</em>
                </button>
              ))}
      </div>
    </div>
  );
}

function FitButton() {
  const flow = useReactFlow();
  return <Button variant="ghost" tip="适配视图" onClick={() => void flow.fitView({ padding: 0.12 })}>全部居中</Button>;
}

function SelectedSummary({ graph }: { graph: GraphDefinition }) {
  const text = useWorkbench(state => {
    if (state.selection?.kind !== 'node') return '';
    const node = graph.nodes.find(item => nodeSelectId(graph, item) === state.selection!.id);
    if (!node) return '';
    const out = graph.edges.filter(edge => edge.source?.nodeId === node.id).length;
    const into = graph.edges.filter(edge => edge.target?.nodeId === node.id).length;
    return `选中 ${nodeName(node)} · ${out} 出 / ${into} 入`;
  });
  return <em>{text}</em>;
}

const nodeTypes = { flow: memo(FlowNodeView), state: memo(StateNodeView) };
const edgeTypes = { route: memo(RouteEdgeView), transition: memo(TransitionEdgeView) };

// 状态机里没有入边且没有子图、也不播放 Timeline 的节点（入口、出口、任意状态）画成灰色。
function isSystemNode(graph: GraphDefinition, hierarchy: GraphHierarchy, laid: LaidNode) {
  return !childGraphs(hierarchy, graph, laid.node.id).length && !nodeTimeline(hierarchy, graph, laid.node.id);
}

// 作者模式：当前技能的流图叠加网页中未保存的修改后显示，并可编辑。状态机编辑另做（共享文档 9.5）。
function AuthoredCanvas(props: Omit<GraphCanvasProps, 'editable'>) {
  const { graph, path } = props;
  const author = useAuthoring(useShallow(state => ({
    on: state.author && !!state.skill && state.programId === graph.programId, edited: state.edited, ports: state.addedPorts,
  })));
  const editable = author.on && !isStateMachine(graph);
  const shown = useMemo(() => editable ? applyGraphEdits(graph, path, author.edited, author.ports) : graph,
    [editable, graph, path, author.edited, author.ports]);
  return <GraphCanvas {...props} graph={shown} editable={editable} />;
}

interface GraphCanvasProps {
  graph: GraphDefinition;
  path: string;
  call: string | null;
  hierarchy: GraphHierarchy;
  enter(graph: GraphDefinition): void;
  params: Params;
  setParams(patch: Partial<Params>): void;
  editable: boolean;
}

function GraphCanvas({ graph, path, call, hierarchy, enter, params, setParams, editable }: GraphCanvasProps) {
  const machine = isStateMachine(graph);
  const layout = useMemo(() => layoutGraph(graph, path), [graph, path]);
  const store = useMemo(() => new RouteStore(), []);
  // 粘贴出的新节点在下次重建时选中。
  const pendingSelect = useRef<ReadonlySet<string> | null>(null);
  const build = useCallback((previous: FlowNode[]) => {
    const selected = pendingSelect.current ?? new Set(previous.filter(node => node.selected).map(node => node.id));
    pendingSelect.current = null;
    return layout.nodes.map((laid, index): FlowNode => {
      const children = childGraphs(hierarchy, graph, laid.node.id);
      const timeline = playedTimeline(hierarchy, graph, laid.node.id);
      return {
        id: laid.node.id, type: machine ? 'state' : 'flow', position: { x: laid.x, y: laid.y }, selected: selected.has(laid.node.id),
        data: {
          laid, index, selectId: nodeSelectId(graph, laid.node), system: machine && isSystemNode(graph, hierarchy, laid),
          timeline, children,
        },
      };
    });
  }, [layout, hierarchy, graph, machine]);
  const [nodes, setNodes] = useState<FlowNode[]>(() => build([]));
  // 作者修改后图结构变化：重建节点，保留选中。
  const built = useRef(layout);
  useEffect(() => {
    if (built.current === layout) return;
    built.current = layout;
    setNodes(build);
  }, [layout, build]);
  const edges = useMemo<FlowEdge[]>(() => {
    const pairs = new Map<string, number>();
    const counts = new Map<string, number>();
    const pairKey = (edge: GraphEdge) => [edge.source!.nodeId, edge.target!.nodeId].sort().join('|');
    for (const edge of layout.edges) counts.set(pairKey(edge), (counts.get(pairKey(edge)) ?? 0) + 1);
    return layout.edges.map(edge => {
      const conditions = edgeChildGraphs(hierarchy, graph, edge.id);
      if (machine) {
        const pair = pairKey(edge);
        const index = pairs.get(pair) ?? 0;
        pairs.set(pair, index + 1);
        return {
          id: edge.id, type: 'transition', source: edge.source!.nodeId, target: edge.target!.nodeId,
          sourceHandle: 'out:@node', targetHandle: 'in:@node',
          data: { edge, conditions, label: conditions.map(graphName).join(' · '), offset: (index - (counts.get(pair)! - 1) / 2) * 16 },
        };
      }
      const source = layout.nodes.find(item => item.node.id === edge.source!.nodeId)!;
      const target = layout.nodes.find(item => item.node.id === edge.target!.nodeId)!;
      return {
        id: edge.id, type: 'route', source: edge.source!.nodeId, target: edge.target!.nodeId,
        sourceHandle: source.outputs.some(port => port.id === edge.source!.portId) ? `out:${edge.source!.portId}` : 'out:@node',
        targetHandle: target.inputs.some(port => port.id === edge.target!.portId) ? `in:${edge.target!.portId}` : 'in:@node',
        data: { edge, conditions, label: conditions.map(graphName).join(' · '), offset: 0 },
      };
    });
  }, [layout, hierarchy, graph, machine]);

  // 帧变化时只更新路线状态，节点数组不重建；状态机另按到本帧为止的路线标出走过的状态与转移。
  useEffect(() => {
    const apply = () => {
      const state = getState();
      const records = graphRecords(state.frame, graph).filter(item => !call || callKey(item.call) === call);
      const history: RouteHistory = { visited: new Set(), traveled: new Set() };
      if (machine) {
        const prefix = `${graph.id}/`;
        for (let index = 0; index <= state.frameIndex && index < state.routeFrames.length; index++) {
          const frame = state.routeFrames[index];
          for (const key of frame.enters) if (key.startsWith(prefix)) history.visited.add(key.slice(prefix.length));
          for (const key of frame.passed) if (key.startsWith(prefix)) history.traveled.add(key.slice(prefix.length));
        }
      }
      store.update(layout.nodes, layout.edges, records, history);
    };
    apply();
    return useWorkbench.subscribe((state, previous) => {
      if (state.frame !== previous.frame || state.frameIndex !== previous.frameIndex) apply();
    });
  }, [store, graph, layout, call, machine]);
  useFrameReport(`${graphKey(graph)}|${path}|${call}`);

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

  const options = useMemo<ViewOptions>(() => ({
    store, values: params.values, dim: params.dim, related, labels: params.labels, enter, hierarchy, graph,
  }), [store, params.values, params.dim, related, params.labels, enter, hierarchy, graph]);
  const onNodesChange = useCallback((changes: NodeChange<FlowNode>[]) => setNodes(current => applyNodeChanges(changes, current)), []);
  const onNodeClick = useCallback((_: unknown, node: FlowNode) => selectionActions.select({ kind: 'node', id: node.data.selectId }), []);
  const onNodeDoubleClick = useCallback((_: unknown, node: FlowNode) => {
    if (node.data.children.length) enter(node.data.children[0]);
    else if (node.data.timeline) showTimeline(node.data.timeline);
  }, [enter]);
  const onEdgeDoubleClick = useCallback((_: unknown, edge: FlowEdge) => {
    if (edge.data!.conditions.length) enter(edge.data!.conditions[0]);
  }, [enter]);
  const view = `${graphKey(graph)}|${path}`;
  const saved = params.viewport?.view === view ? params.viewport : null;
  const onMoveEnd = useCallback((_: unknown, viewport: Viewport) => setParams({ viewport: { ...viewport, view } }), [setParams, view]);
  // 框选结束：选中了一个节点时同步给检查器；框到多个时检查器显示第一个。
  const onSelectionEnd = useCallback(() => {
    const picked = nodes.filter(node => node.selected);
    if (picked.length) selectionActions.select({ kind: 'node', id: picked[0].data.selectId });
  }, [nodes]);
  const { fitView, screenToFlowPosition } = useReactFlow();

  // ── 作者编辑（照 NodeCanvas）：右键添加节点、拖端口连线、Delete 删除、Ctrl+C／Ctrl+V 复制粘贴、拖动写布局 ──
  const [selectedEdges, setSelectedEdges] = useState<ReadonlySet<string>>(() => new Set());
  const [picker, setPicker] = useState<{ x: number; y: number; flow: { x: number; y: number } } | null>(null);
  const menuAt = useRef({ x: 0, y: 0 });
  const shownEdges = useMemo(() => edges.map(edge => selectedEdges.has(edge.id) ? { ...edge, selected: true } : edge), [edges, selectedEdges]);
  const onEdgeClick = useCallback((event: ReactMouseEvent, edge: FlowEdge) => {
    if (!editable) return;
    setSelectedEdges(current => {
      const next = new Set(event.ctrlKey || event.shiftKey ? current : []);
      if (!next.delete(edge.id)) next.add(edge.id);
      return next;
    });
  }, [editable]);
  const portOf = (handle: string | null | undefined) => handle?.slice(handle.indexOf(':') + 1) ?? '';
  const onConnect = useCallback((connection: Connection) => {
    if (!connection.source || !connection.target) return;
    graphAuthoring.connect(graph.id, { nodeId: connection.source, portId: portOf(connection.sourceHandle) },
      { nodeId: connection.target, portId: portOf(connection.targetHandle) });
  }, [graph.id]);
  const onNodeDragStop = useCallback((_: unknown, __: FlowNode, dragged: FlowNode[]) => {
    if (editable) graphAuthoring.move(graph.id, Object.fromEntries(dragged.map(node => [node.id, node.position])));
  }, [editable, graph.id]);
  const portsOf = useCallback((ids: string[]) => Object.fromEntries(ids.map(id => [id, graph.ports
    .filter(port => port.nodeId === id && port.invocationPath === path)
    .map(port => ({ id: port.id, displayName: port.displayName, direction: port.direction as 'input' | 'output', isTrigger: port.isTrigger }))])), [graph, path]);
  const onKeyDown = (event: ReactKeyboardEvent) => {
    if (!editable || (event.target as HTMLElement).closest('input, textarea')) return;
    const picked = nodes.filter(node => node.selected);
    if (event.key === 'Delete') {
      if (picked.length) graphAuthoring.deleteNodes(graph.id, picked.map(node => node.id));
      if (selectedEdges.size) graphAuthoring.disconnect(graph.id, [...selectedEdges]);
      setSelectedEdges(new Set());
    } else if ((event.ctrlKey || event.metaKey) && event.key === 'c' && picked.length) {
      graphAuthoring.copy(graph.id, picked.map(node => node.id), Object.fromEntries(picked.map(node => [node.id, node.position])),
        portsOf(picked.map(node => node.id)));
    } else if ((event.ctrlKey || event.metaKey) && event.key === 'v') {
      const pasted = graphAuthoring.paste(graph.id, { x: 40, y: 40 });
      if (pasted.length) pendingSelect.current = new Set(pasted);
    } else return;
    event.preventDefault();
  };
  const blocked = machine ? '状态机的状态与转移编辑另做（共享文档 9.5）' : '进入 Timeline 编辑行的“作者”模式后可编辑当前技能的流图';
  const menu: MenuEntry[] = [
    editable
      ? { label: '添加节点…', onSelect: () => setPicker({ ...menuAt.current, flow: screenToFlowPosition(menuAt.current) }) }
      : { label: '添加节点…', onSelect: () => {}, disabled: true, tip: blocked },
    { label: '粘贴', disabled: !editable, tip: editable ? undefined : blocked, onSelect: () => graphAuthoring.paste(graph.id, { x: 40, y: 40 }) },
    { kind: 'separator' },
    { label: '全部居中', onSelect: () => void fitView({ padding: 0.12, maxZoom: 1, duration: 200 }) },
    { label: '全选', onSelect: () => setNodes(current => current.map(node => ({ ...node, selected: true }))) },
  ];
  const viewOptions = useMemo(() => ({ ...options, editable }), [options, editable]);

  return (
    <ViewContext.Provider value={viewOptions}>
      <ContextMenu entries={menu}>
      <div className={styles.canvas} tabIndex={-1} onKeyDown={onKeyDown} data-author={editable || undefined}
        onContextMenuCapture={event => { menuAt.current = { x: event.clientX, y: event.clientY }; }}>
        {/* 照 NodeCanvas：中键拖动平移，左键在空白处拖出框选，滚轮缩放，Ctrl／Shift 加选 */}
        <ReactFlow<FlowNode, FlowEdge> nodes={nodes} edges={shownEdges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
          onNodesChange={onNodesChange} onNodeClick={onNodeClick} onNodeDoubleClick={onNodeDoubleClick}
          onEdgeDoubleClick={onEdgeDoubleClick} onEdgeClick={onEdgeClick} onPaneClick={() => setSelectedEdges(new Set())}
          onMoveEnd={onMoveEnd} onSelectionEnd={onSelectionEnd} zoomOnDoubleClick={false}
          onConnect={onConnect} onNodeDragStop={onNodeDragStop} nodesDraggable
          panOnDrag={[1]} selectionOnDrag selectionMode={SelectionMode.Partial}
          defaultViewport={saved ?? undefined} fitView={!saved} fitViewOptions={{ padding: 0.12, maxZoom: 1 }}
          minZoom={0.1} maxZoom={2} nodesConnectable={editable} edgesFocusable={false} elementsSelectable
          deleteKeyCode={null} selectionKeyCode={null} multiSelectionKeyCode={['Control', 'Shift']} proOptions={{ hideAttribution: true }}
          colorMode="dark">
          <Background id="minor" variant={BackgroundVariant.Lines} gap={20} patternClassName={styles.gridMinor} />
          <Background id="major" variant={BackgroundVariant.Lines} gap={200} patternClassName={styles.gridMajor} />
          {params.minimap && (
            <MiniMap className={styles.minimap} style={{ width: 204, height: 104, marginBottom: 24 }} pannable zoomable
              bgColor="#181818" maskColor="rgb(255 255 255 / 4%)"
              maskStrokeColor="#9a9a9a" nodeColor={node => (node.data as NodeData).system ? '#8a8a8a' : machine ? '#f0634f' : '#5a5a5a'}
              nodeStrokeWidth={0} />
          )}
        </ReactFlow>
        <div className={styles.hint}>
          {editable ? '作者模式 · 右键添加节点 · 从端口拖出连线 · Delete 删除 · Ctrl+C／Ctrl+V 复制粘贴 · 中键拖动平移 · 左键框选'
            : `${machine ? '双击状态进入状态体 · 双击转移进入条件' : '双击节点进入子图或跳到 Timeline'} · 中键拖动平移 · 左键框选`}
        </div>
        {picker && <NodeTypePicker graph={graph} at={picker} onClose={() => setPicker(null)} />}
      </div>
      </ContextMenu>
    </ViewContext.Provider>
  );
}

// 跳到该 Timeline 第一次播放的帧并显示 Timeline 面板。
function showTimeline(documentId: string) {
  selectionActions.showTimeline(documentId);
  openPanel('timeline');
}

function useNodeState(store: RouteStore, id: string) {
  return useSyncExternalStore(store.subscribe(`node:${id}`), () => store.node(id));
}

// 状态机的状态：NodeCanvas 状态块，正在运行的加黄框，走过的加金色外框。
function StateNodeView({ data, selected: boxed }: NodeProps<FlowNode>) {
  const { laid, index, selectId, system, timeline, children } = data;
  const { store, related } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId) || boxed;
  const running = state.occurred > 0;
  const local = useWorkbench(current => {
    if (!running || !timeline) return null;
    return current.routeFrames[current.frameIndex]?.playbacks.find(item => item.documentId === timeline)?.cursorSeconds ?? null;
  });
  const timelineName = useWorkbench(current => timeline ? current.timelines.find(item => item.documentId === timeline)?.displayName ?? null : null);
  const faded = related !== null && !related.has(laid.node.id);
  return (
    <div className={styles.state} data-system={system || undefined} data-running={running || undefined}
      data-visited={(state.visited && !running) || undefined} data-selected={selected || undefined} data-faded={faded || undefined}
      title={`${nodeName(laid.node)}\n${laid.node.id}${children.length ? '\n双击进入状态体' : ''}${state.detail ? `\n${state.detail}` : ''}`}>
      <span className={styles.stateId}>#{index}</span>
      <Handle id="in:@node" type="target" position={Position.Left} className={styles.hiddenHandle} isConnectable={false} />
      <Handle id="out:@node" type="source" position={Position.Right} className={styles.hiddenHandle} isConnectable={false} />
      <div className={styles.stateTitle}>{running && <i className={styles.clock} />}{nodeName(laid.node)}</div>
      {running && local !== null && <div className={styles.stateBody}>本地 {local.toFixed(2)} s</div>}
      {timelineName && <span className={styles.stateCaption}>{timelineName}</span>}
    </div>
  );
}

// 流图节点：NodeCanvas 节点，标题下按行列出输入与输出端口。
function FlowNodeView({ data, selected: boxed }: NodeProps<FlowNode>) {
  const { laid, selectId, children, timeline } = data;
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
      title={`${nodeName(laid.node)}\n${laid.node.id}${children.length ? '\n双击进入子图' : timeline ? '\n双击跳到 Timeline' : ''}${state.detail ? `\n${state.detail}` : ''}`}>
      <div className={styles.header}>
        {triggerIn && <i className={styles.flowIn} />}
        <b>{nodeName(laid.node)}</b>
        {triggerOut && <i className={styles.flowOut} />}
        {(children.length > 0 || timeline) && <i className={styles.subgraphMark} />}
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

// 转移：从源状态边框指向目标状态边框的曲线箭头，标签取条件图名称。
function TransitionEdgeView({ id, source, target, data }: EdgeProps<FlowEdge>) {
  const { store, related, labels } = useView();
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
  const showLabel = labels !== 'none' && data!.label && (labels === 'all' || highlighted || status !== 'idle');
  return (
    <g className={styles.transition} data-status={status} data-hi={highlighted || undefined} data-faded={faded || undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}>
      <BaseEdge path={path} interactionWidth={12} />
      <path d={arrow} className={styles.arrow} />
      {showLabel && (
        <EdgeLabelRenderer>
          <div className={styles.edgeLabel} data-status={status} data-hi={highlighted || undefined}
            style={{ transform: `translate(-50%, -50%) translate(${mx}px, ${my}px)` }}
            title={`${data!.label}\n双击进入条件图`}>
            {data!.label}
          </div>
        </EdgeLabelRenderer>
      )}
    </g>
  );
}
