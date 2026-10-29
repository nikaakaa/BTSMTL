import {
  Background, BackgroundVariant, BaseEdge, EdgeLabelRenderer, getBezierPath, Handle, MiniMap, Position, ReactFlow,
  ReactFlowProvider, applyNodeChanges, useInternalNode, useReactFlow, type Edge, type EdgeProps, type Node, type NodeChange,
  type NodeProps, type Viewport,
} from '@xyflow/react';
import '@xyflow/react/dist/base.css';
import {
  createContext, memo, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { GraphDefinition, GraphEdge, GraphPort } from '../../api/types';
import { openPanel } from '../../shell/layout';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import {
  belongsTo, callKey, childGraphs, edgeChildGraphs, graphCrumbs, graphHierarchy, graphKey, graphName, graphRecords,
  isStateMachine, layoutGraph, nodeName, nodeSelectId, nodeTimeline, nodeWidth, portColor, type GraphHierarchy, type LaidNode,
} from '../../state/graphModel';
import { getState, selectionActions, useWorkbench } from '../../state/store';
import { playSegments } from '../../state/timelineModel';
import { transport } from '../../state/transport';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, Select, type MenuEntry } from '../../ui/controls';
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
            : <GraphCanvas graph={graph} path={path} call={calls.includes(call!) ? call : null}
              hierarchy={hierarchy} enter={enter} params={params} setParams={setParams} />}
      </ReactFlowProvider>
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

function GraphCanvas({ graph, path, call, hierarchy, enter, params, setParams }: {
  graph: GraphDefinition;
  path: string;
  call: string | null;
  hierarchy: GraphHierarchy;
  enter(graph: GraphDefinition): void;
  params: Params;
  setParams(patch: Partial<Params>): void;
}) {
  const machine = isStateMachine(graph);
  const layout = useMemo(() => layoutGraph(graph, path), [graph, path]);
  const store = useMemo(() => new RouteStore(), []);
  const [nodes, setNodes] = useState<FlowNode[]>(() => layout.nodes.map((laid, index) => {
    const children = childGraphs(hierarchy, graph, laid.node.id);
    // 状态本身不播放 Timeline，取状态体里播放的那条。
    const timeline = nodeTimeline(hierarchy, graph, laid.node.id) ?? children.flatMap(child =>
      child.nodes.map(node => nodeTimeline(hierarchy, child, node.id))).find(item => item) ?? null;
    return {
      id: laid.node.id, type: machine ? 'state' : 'flow', position: { x: laid.x, y: laid.y },
      data: {
        laid, index, selectId: nodeSelectId(graph, laid.node), system: machine && isSystemNode(graph, hierarchy, laid),
        timeline, children,
      },
      ...(machine ? {} : { width: nodeWidth }),
    };
  }));
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

  return (
    <ViewContext.Provider value={options}>
      <div className={styles.canvas}>
        <ReactFlow<FlowNode, FlowEdge> nodes={nodes} edges={edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
          onNodesChange={onNodesChange} onNodeClick={onNodeClick} onNodeDoubleClick={onNodeDoubleClick}
          onEdgeDoubleClick={onEdgeDoubleClick} onMoveEnd={onMoveEnd} zoomOnDoubleClick={false}
          defaultViewport={saved ?? undefined} fitView={!saved} fitViewOptions={{ padding: 0.12, maxZoom: 1 }}
          minZoom={0.1} maxZoom={2} nodesConnectable={false} edgesFocusable={false} elementsSelectable
          deleteKeyCode={null} selectionKeyCode={null} multiSelectionKeyCode={null} proOptions={{ hideAttribution: true }}
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
        <div className={styles.hint}>{machine ? '双击状态进入状态体 · 双击转移进入条件' : '双击节点进入子图或跳到 Timeline'}</div>
      </div>
    </ViewContext.Provider>
  );
}

// 跳到该 Timeline 第一次播放的帧并显示 Timeline 面板。
function showTimeline(documentId: string) {
  const segment = playSegments(getState().routeFrames).get(documentId)?.[0];
  if (segment) transport.seek(segment.firstIndex);
  openPanel('timeline');
}

function useNodeState(store: RouteStore, id: string) {
  return useSyncExternalStore(store.subscribe(`node:${id}`), () => store.node(id));
}

// 状态机的状态：NodeCanvas 状态块，正在运行的加黄框，走过的加金色外框。
function StateNodeView({ data }: NodeProps<FlowNode>) {
  const { laid, index, selectId, system, timeline, children } = data;
  const { store, related } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId);
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
function FlowNodeView({ data }: NodeProps<FlowNode>) {
  const { laid, selectId, children, timeline } = data;
  const { store, values, dim, related } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId);
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
  return <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
    position={input ? Position.Left : Position.Right} isConnectable={false} className={input ? styles.triggerIn : styles.triggerOut} />;
}

function PortCell({ port, state, values }: {
  port: GraphPort | undefined;
  state: ReturnType<RouteStore['node']>;
  values: boolean;
}) {
  if (!port) return <span className={styles.portEmpty} />;
  const input = port.direction === 'input';
  const sample = state.ports[`${port.direction}:${port.id}`];
  const color = portColor(sample?.valueType);
  return (
    <span className={input ? styles.portIn : styles.portOut} title={sample ? `${port.id}\n${sample.valueType} · ${sample.value}\n${sample.sequence}` : port.id}
      style={{ ['--pc' as string]: color }}>
      <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
        position={input ? Position.Left : Position.Right} isConnectable={false}
        className={styles.valuePort} data-filled={sample ? true : undefined} />
      <span className={styles.portName}>{port.displayName}</span>
      {values && sample && <span className={styles.portValue}>{sample.value}</span>}
    </span>
  );
}

function RouteEdgeView({ id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data }: EdgeProps<FlowEdge>) {
  const { store, related } = useView();
  const state = useSyncExternalStore(store.subscribe(`edge:${id}`), () => store.edge(id));
  const [path] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition });
  const status = state.passed ? 'passed' : state.rejected ? 'rejected' : 'idle';
  const faded = related !== null && !related.has(id);
  return (
    <path d={path} className={styles.edge} data-status={status} data-faded={faded || undefined}
      data-selected={state.selected || undefined}>
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
