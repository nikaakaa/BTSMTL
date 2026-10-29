import {
  Background, BackgroundVariant, getBezierPath, Handle, MiniMap, Position, ReactFlow, ReactFlowProvider,
  applyNodeChanges, useReactFlow, type Edge, type EdgeProps, type Node, type NodeChange, type NodeProps, type Viewport,
} from '@xyflow/react';
import '@xyflow/react/dist/base.css';
import {
  createContext, memo, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { GraphDefinition, GraphEdge, GraphPort } from '../../api/types';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import {
  belongsTo, callKey, graphKey, graphName, graphRecords, layoutGraph, nodeName, nodeSelectId, nodeWidth, portColor,
  type LaidNode,
} from '../../state/graphModel';
import { getState, selectionActions, useWorkbench } from '../../state/store';
import { BarSeparator, Empty, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { RouteStore } from './routeStore';
import styles from './graph.module.css';

// 节点图面板（D4）：React Flow 显示图与端口；路线状态只更新状态变了的节点与连线（D9）。

interface Params {
  graph: string | null;
  call: string | null;
  // 视图位置按所看的图与图实例保存。
  viewport: (Viewport & { view: string }) | null;
  values: boolean;
  dim: boolean;
  relations: boolean;
  minimap: boolean;
}
const defaults: Params = { graph: null, call: null, viewport: null, values: true, dim: false, relations: false, minimap: true };

interface ViewOptions {
  store: RouteStore;
  values: boolean;
  dim: boolean;
  related: Set<string> | null;
}
const ViewContext = createContext<ViewOptions | null>(null);
const useView = () => useContext(ViewContext)!;

interface NodeData extends Record<string, unknown> {
  laid: LaidNode;
  selectId: string;
}
type FlowNode = Node<NodeData, 'graph'>;
interface EdgeData extends Record<string, unknown> {
  edge: GraphEdge;
}
type FlowEdge = Edge<EdgeData, 'route'>;

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

function GraphView({ graphs }: { graphs: GraphDefinition[] }) {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const graphOptions = useMemo(() => graphs.map(graph => ({ value: graphKey(graph), label: `${graph.programId} · ${graphName(graph)}` })), [graphs]);

  // 未选图时取本帧第一张有路线记录的图。
  const firstActive = useWorkbench(state => {
    if (params.graph) return null;
    const route = state.frame?.route;
    if (!route) return null;
    const items = [...route.nodes, ...route.edges];
    const graph = graphs.find(graph => items.some(item => item.graphId === graph.id && belongsTo(item.call, graph)));
    return graph ? graphKey(graph) : null;
  });
  const graph = graphs.find(item => graphKey(item) === (params.graph ?? firstActive)) ?? graphs[0];
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

  return (
    // 视图状态随所看的图与图实例重建，保存的视图位置才能生效。
    <div className={styles.root}>
      <ReactFlowProvider key={`${key}|${path}`}>
      <PanelBar>
        <Select label="节点图" value={key} options={graphOptions}
          onChange={value => setParams({ graph: value, call: null, viewport: null })} />
        <Select label="实际调用" value={call} options={callOptions} disabled={callOptions.length < 2}
          tip={callOptions.length ? undefined : '此图没有节点'} onChange={value => setParams({ call: value })} />
        <BarSeparator />
        <ViewButtons params={params} setParams={setParams} />
      </PanelBar>
      {!graph.nodes.length ? <ReportEmpty text="此图未保留作者节点来源" />
        : path === null ? <ReportEmpty text="此调用未保留可显示的节点来源" />
          : <GraphCanvas graph={graph} path={path} call={calls.includes(call!) ? call : null}
            params={params} setParams={setParams} />}
      </ReactFlowProvider>
    </div>
  );
}

function ViewButtons({ params, setParams }: { params: Params; setParams(patch: Partial<Params>): void }) {
  const flow = useReactFlow();
  return (
    <>
      <Button variant="ghost" tip="缩放到 100%" onClick={() => void flow.zoomTo(1)}>Zoom</Button>
      <Button variant="ghost" pressed={params.values} tip="显示本帧采样值" onClick={() => setParams({ values: !params.values })}>Values</Button>
      <Button variant="ghost" pressed={params.dim} tip="压暗本帧没走到的节点" onClick={() => setParams({ dim: !params.dim })}>Dim</Button>
      <Button variant="ghost" pressed={params.relations} tip="只突出所选节点的连线与相邻节点"
        onClick={() => setParams({ relations: !params.relations })}>Relations</Button>
      <Button variant="ghost" tip="适配视图" onClick={() => void flow.fitView({ padding: 0.12 })}>Overview</Button>
      <Button variant="ghost" pressed={params.minimap} tip="小地图" onClick={() => setParams({ minimap: !params.minimap })}>Map</Button>
    </>
  );
}

const nodeTypes = { graph: memo(GraphNodeView) };
const edgeTypes = { route: memo(RouteEdgeView) };

function GraphCanvas({ graph, path, call, params, setParams }: {
  graph: GraphDefinition;
  path: string;
  call: string | null;
  params: Params;
  setParams(patch: Partial<Params>): void;
}) {
  const layout = useMemo(() => layoutGraph(graph, path), [graph, path]);
  const store = useMemo(() => new RouteStore(), []);
  const [nodes, setNodes] = useState<FlowNode[]>(() => layout.nodes.map(laid => ({
    id: laid.node.id, type: 'graph', position: { x: laid.x, y: laid.y }, data: { laid, selectId: nodeSelectId(graph, laid.node) },
    width: nodeWidth,
  })));
  const edges = useMemo<FlowEdge[]>(() => layout.edges.map(edge => {
    const source = layout.nodes.find(item => item.node.id === edge.source!.nodeId)!;
    const target = layout.nodes.find(item => item.node.id === edge.target!.nodeId)!;
    return {
      id: edge.id, type: 'route', source: edge.source!.nodeId, target: edge.target!.nodeId,
      sourceHandle: source.outputs.some(port => port.id === edge.source!.portId) ? `out:${edge.source!.portId}` : 'out:@node',
      targetHandle: target.inputs.some(port => port.id === edge.target!.portId) ? `in:${edge.target!.portId}` : 'in:@node',
      data: { edge },
    };
  }), [layout]);

  // 帧变化时只更新路线状态，节点数组不重建。
  useEffect(() => {
    const apply = () => {
      const records = graphRecords(getState().frame, graph).filter(item => !call || callKey(item.call) === call);
      store.update(layout.nodes, layout.edges, records);
    };
    apply();
    return useWorkbench.subscribe((state, previous) => { if (state.frame !== previous.frame) apply(); });
  }, [store, graph, layout, call]);
  useFrameReport(`${graphKey(graph)}|${path}|${call}`);

  const selectedNode = useWorkbench(state => state.selection?.kind === 'node' ? state.selection.id : null);
  const related = useMemo(() => {
    if (!params.relations || !selectedNode) return null;
    const own = layout.nodes.find(item => nodeSelectId(graph, item.node) === selectedNode);
    if (!own) return null;
    const result = new Set([own.node.id]);
    for (const edge of layout.edges) {
      if (edge.source!.nodeId === own.node.id) { result.add(edge.target!.nodeId); result.add(edge.id); }
      if (edge.target!.nodeId === own.node.id) { result.add(edge.source!.nodeId); result.add(edge.id); }
    }
    return result;
  }, [params.relations, selectedNode, layout, graph]);

  const options = useMemo<ViewOptions>(() => ({ store, values: params.values, dim: params.dim, related }),
    [store, params.values, params.dim, related]);
  const onNodesChange = useCallback((changes: NodeChange<FlowNode>[]) => setNodes(current => applyNodeChanges(changes, current)), []);
  const onNodeClick = useCallback((_: unknown, node: FlowNode) => selectionActions.select({ kind: 'node', id: node.data.selectId }), []);
  const view = `${graphKey(graph)}|${path}`;
  const saved = params.viewport?.view === view ? params.viewport : null;
  const onMoveEnd = useCallback((_: unknown, viewport: Viewport) => setParams({ viewport: { ...viewport, view } }), [setParams, view]);

  return (
    <ViewContext.Provider value={options}>
      <div className={styles.canvas}>
        <ReactFlow<FlowNode, FlowEdge> nodes={nodes} edges={edges} nodeTypes={nodeTypes} edgeTypes={edgeTypes}
          onNodesChange={onNodesChange} onNodeClick={onNodeClick} onMoveEnd={onMoveEnd}
          defaultViewport={saved ?? undefined} fitView={!saved} fitViewOptions={{ padding: 0.12 }}
          minZoom={0.1} maxZoom={2} nodesConnectable={false} edgesFocusable={false} elementsSelectable
          deleteKeyCode={null} selectionKeyCode={null} multiSelectionKeyCode={null} proOptions={{ hideAttribution: true }}
          colorMode="dark">
          <Background id="minor" variant={BackgroundVariant.Lines} gap={12} patternClassName={styles.gridMinor} />
          <Background id="major" variant={BackgroundVariant.Lines} gap={120} patternClassName={styles.gridMajor} />
          {params.minimap && (
            <MiniMap className={styles.minimap} pannable zoomable bgColor="var(--bg-1)" maskColor="rgb(0 0 0 / 45%)"
              nodeColor="var(--node-header)" nodeStrokeColor="var(--node-line)" />
          )}
        </ReactFlow>
      </div>
    </ViewContext.Provider>
  );
}

function useNodeState(store: RouteStore, id: string) {
  return useSyncExternalStore(store.subscribe(`node:${id}`), () => store.node(id));
}

function GraphNodeView({ data }: NodeProps<FlowNode>) {
  const { laid, selectId } = data;
  const { store, values, dim, related } = useView();
  const state = useNodeState(store, laid.node.id);
  const selected = useWorkbench(current => current.selection?.kind === 'node' && current.selection.id === selectId);
  const name = nodeName(laid.node);
  const split = name.lastIndexOf('.');
  const category = split > 0 ? name.slice(0, split) : '';
  const title = split > 0 ? name.slice(split + 1) : name;
  const faded = (dim && !state.occurred) || (related !== null && !related.has(laid.node.id));
  const rows = Math.max(laid.inputs.length, laid.outputs.length);
  return (
    <div className={styles.node} data-selected={selected || undefined} data-faded={faded || undefined}
      data-occurred={state.occurred || undefined} data-failed={state.failed || undefined}
      title={`${name}\n${laid.node.id}${state.detail ? `\n${state.detail}` : ''}`}>
      <div className={styles.header}>
        <i className={styles.glyph} style={{ background: laid.entry ? 'var(--node-event)' : 'var(--node-flow)' }} />
        <div className={styles.titles}>
          <small>{category}</small>
          <b>{title}</b>
        </div>
        <Handle id="in:@node" type="target" position={Position.Left} className={styles.hiddenHandle} isConnectable={false} />
        <Handle id="out:@node" type="source" position={Position.Right} className={styles.hiddenHandle} isConnectable={false} />
      </div>
      <div className={styles.body}>
        {Array.from({ length: rows }, (_, index) => (
          <div key={index} className={styles.portRow}>
            <PortCell port={laid.inputs[index]} state={state} values={values} occurred={state.occurred > 0} />
            <PortCell port={laid.outputs[index]} state={state} values={values} occurred={state.occurred > 0} />
          </div>
        ))}
      </div>
    </div>
  );
}

function PortCell({ port, state, values, occurred }: {
  port: GraphPort | undefined;
  state: ReturnType<RouteStore['node']>;
  values: boolean;
  occurred: boolean;
}) {
  if (!port) return <span className={styles.portEmpty} />;
  const input = port.direction === 'input';
  const sample = state.ports[`${port.direction}:${port.id}`];
  const color = port.isTrigger ? 'var(--port-exec)' : portColor(sample?.valueType);
  const handle = (
    <Handle id={`${input ? 'in' : 'out'}:${port.id}`} type={input ? 'target' : 'source'}
      position={input ? Position.Left : Position.Right} isConnectable={false}
      className={port.isTrigger ? styles.execPort : styles.valuePort}
      data-off={port.isTrigger && !occurred ? true : undefined} data-filled={!port.isTrigger && sample ? true : undefined}
      style={{ ['--pc' as string]: color }} />
  );
  return (
    <span className={input ? styles.portIn : styles.portOut} title={sample ? `${port.id}\n${sample.valueType} · ${sample.value}\n${sample.sequence}` : port.id}>
      {handle}
      <span className={styles.portName}>{port.displayName}</span>
      {values && sample && (input
        ? <span className={styles.inputValue}>{sample.value}</span>
        : <span className={styles.outputValue}>{sample.value}</span>)}
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
