import { createContext, useContext, useMemo, type ReactNode } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Availability, ExecutionCall, Field, Frame, GraphDefinition, HostRecord, RouteFrame } from '../../api/types';
import { openPanel } from '../../shell/layout';
import { usePanelParams } from '../../shell/panel';
import {
  findGraphNode, graphKey, graphName, graphRecords, isNodeOccurrence,
  isPortSample, nodeGraphs, nodeName, nodeOccurrenceLabel, portColor, compareSequence, callKey, singleCall, matchesDocumentCall, type DocumentObservationBinding,
  type GraphNavigationTarget,
} from '../../state/graphModel';
import {
  applyTimelineEdits, authoringActions, authoringDocument, edgeStep, findEditedGraph, graphAuthoring, graphPath, ownsAuthoringGraph, useAuthoring, useAuthorTimelines, useCurrentDocument, useInputSchemes, useSkillElements, useDocumentGraphHierarchy, useDocumentObservationBinding,
} from '../../state/authoring';
import { projectActions } from '../../state/project';
import { useAnimationLibrary } from '../../state/resources';
import type { JsonObject } from '../../state/skillJson';
import { consoleActions, selectionActions, useWorkbench, type GraphNavigationOrigin, type Selection } from '../../state/store';
import { clipGraph, clipKind, findTimelineClip, findTimelineMarker, findTimelineTrack, trackKind } from '../../state/timelineModel';
import { transport } from '../../state/transport';
import { useInputObservation } from '../../state/inputModel';
import { Empty, Fold, IdentityFold } from '../../ui/blocks';
import { Button, FieldLabel, NumberInput, ObjectField, Tip } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { TrackIcon } from '../timeline/icons';
import { ClipFields, InputClipFields, InputPlanFields, NodeFields, StepFields, TrackFields, TransitionFields } from './FieldEditors';
import styles from './inspector.module.css';

// 检查器（D7，照 Unity Inspector）：对象头写所选对象的名字与所在位置，右上角是锁；
// 每个对象一栏自身的序列化字段，引用（节点图、条件图等）作为同栏的对象字段；运行记录只在有运行时附在后面。
// 不选中时显示技能本身。锁定的实例固定显示锁定对象；身份、修订与哈希只放底部身份折叠。

interface Params {
  locked: Selection | null;
}

// 对象头右上角的锁：toggle 为 null 时没有可锁定的对象。
const LockContext = createContext<{ locked: boolean; toggle: (() => void) | null }>({ locked: false, toggle: null });

export function InspectorPanel() {
  const [params, setParams] = usePanelParams<Params>({ locked: null });
  const selection = useWorkbench(state => state.selection);
  const target = useAuthoring(state => state.target);
  const shown = params.locked ?? selection;
  const unlock = () => setParams({ locked: null });
  const lock = {
    locked: !!params.locked,
    toggle: params.locked ? unlock : selection ? () => setParams({ locked: selection }) : null,
  };
  // 动画资源不属于技能，没有打开技能时同样可检查。
  const content = shown?.kind === 'resource' ? <ResourceInspector key={shown.id} identity={shown.id} />
    : !target ? <Empty text="选择要编辑的内容" />
      : shown ? <Content key={`${target.kind}/${target.name}:${shown.kind}:${shown.id}`} shown={shown} locked={!!params.locked} unlock={unlock} />
        : <SkillSummary />;
  return (
    <div className={styles.root}>
      <LockContext.Provider value={lock}>
        <div className={styles.scroll}>
          {content}
        </div>
      </LockContext.Provider>
    </div>
  );
}

function Content({ shown, locked, unlock }: { shown: Selection; locked: boolean; unlock(): void }) {
  const missing = (
    <div className={styles.missing}>
      {shown.kind === 'actor' ? '档案中没有所选角色' : locked ? '锁定对象不属于当前技能' : '所选对象不属于当前技能'}
      {locked && <Button variant="ghost" onClick={unlock}>解除锁定</Button>}
    </div>
  );
  switch (shown.kind) {
    case 'node': return <NodeInspector id={shown.id} call={shown.call} missing={missing} />;
    case 'clip': return shown.id.startsWith('input/') ? <InputItemInspector id={shown.id} missing={missing} />
      : <ClipInspector id={shown.id} call={shown.call} missing={missing} />;
    case 'track': return shown.id.startsWith('curve/') ? <CurveInspector id={shown.id} missing={missing} />
      : shown.id.startsWith('input/') ? <InputTrackInspector id={shown.id} missing={missing} />
      : <TrackInspector id={shown.id} call={shown.call} missing={missing} />;
    case 'actor': return <ActorInspector id={shown.id} missing={missing} />;
    case 'edge': return <EdgeInspector id={shown.id} missing={missing} />;
    case 'resource': return null;
  }
}

// ── 通用部件 ──

// 对象头：有真实矢量图标的对象（轨道、片段、动画资源）在左侧显示类型图标。
function Head({ icon, name, sub }: { icon?: ReactNode; name: ReactNode; sub?: ReactNode }) {
  const lock = useContext(LockContext);
  return (
    <div className={styles.head}>
      {icon && <span className={styles.icon}>{icon}</span>}
      <span className={`${styles.box} ${styles.name}`}>{name}</span>
      <span className={styles.subline}>{sub}</span>
      <Button variant="ghost" className={styles.lock} pressed={lock.locked} disabled={!lock.toggle} aria-label="锁定检查器"
        tip={lock.locked ? '解除锁定，跟随选中对象' : lock.toggle ? '锁定：固定显示这个对象' : '没有选中对象'}
        onClick={lock.toggle ?? undefined}>
        <i className={styles.padlock} data-locked={lock.locked || undefined} />
      </Button>
    </div>
  );
}

function Value({ label, value, mono = true }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className={styles.field}>
      <FieldLabel label={label} />
      <Tip name={typeof value === 'string' ? value : undefined}>
        <span className={`${styles.box} ${mono ? styles.mono : ''}`} data-name>{value}</span>
      </Tip>
    </div>
  );
}

// 引用字段：照 Unity 对象引用框，点击在节点图或 Timeline 面板打开所引用的对象。
function Link({ label, text, kind, tip, onOpen }: { label: string; text: ReactNode; kind: string; tip: string; onOpen(): void }) {
  return <div className={styles.field}><FieldLabel label={label} /><ObjectField kind={kind} text={text} tip={tip} onOpen={onOpen} /></div>;
}

function openGraph(graph: Pick<GraphDefinition, 'id'>, origin: GraphNavigationOrigin | null = null) {
  const target = authoringDocument(useAuthoring.getState()).documentGraphs!.find(item => item.id === graph.id)!;
  void projectActions.openGraph(target, undefined, origin).then(() => { openPanel('graph'); }, reason => consoleActions.error('作者', reason));
}

function Values({ fields }: { fields: Field[] }) {
  return <>{fields.map((field, index) => <Value key={index} label={field.displayName} value={field.value} />)}</>;
}

function Raw({ value }: { value: unknown }) {
  return <pre className={styles.raw}>{typeof value === 'string' ? value : JSON.stringify(value, null, 2)}</pre>;
}

function HostRecords({ records }: { records: HostRecord[] }) {
  if (!records.length) return null;
  return (
    <Fold title="宿主记录" defaultOpen>
      {records.map((record, index) => (
        <Fold key={index} title={record.displayName}>
          <Values fields={record.fields} />
          <Fold title="原始记录"><Raw value={record.rawJson} /></Fold>
        </Fold>
      ))}
    </Fold>
  );
}

function locatedRecords(frame: Frame | null, binding: DocumentObservationBinding, call: ExecutionCall | null,
  match: (documentId: string, elementId: string) => boolean): HostRecord[] {
  if (!frame || call === null) return [];
  return frame.sections.flatMap(section => section.records)
    .filter(record => record.locations.some(location => location.call !== null && matchesDocumentCall(location.call, binding) &&
      callKey(location.call) === callKey(call) && match(location.documentId, location.elementId)));
}

const gap = (item: Availability) => !item.available || !item.complete;

// 身份折叠：运行身份、内容修订、SDK 帧、能力缺项、协议版本，以及所示对象自身的身份。
function Identity({ extra = [] }: { extra?: Field[] }) {
  const state = useWorkbench(useShallow(current => ({
    summary: current.summary, hostFrame: current.hostFrame, frame: current.frame, routeProtocol: current.routeProtocol,
    inputProtocol: current.inputTimeline?.protocol ?? null, routeAvailability: current.routeAvailability,
    graphAvailability: current.graphAvailability, metadataAvailability: current.metadataAvailability,
  })));
  const fields: Field[] = [...extra];
  if (state.summary) {
    fields.push({ displayName: '运行', value: state.summary.runId }, { displayName: '内容修订', value: state.summary.contentRevision });
    const execution = state.summary.execution;
    fields.push({ displayName: '执行类型', value: execution.kind ?? '不可用' }, { displayName: '执行身份', value: execution.identity ?? '不可用' });
    if (execution.playerBuildId !== null) fields.push({ displayName: 'Player 构建', value: execution.playerBuildId });
    if (execution.editorAssemblyMvid !== null) fields.push({ displayName: 'Editor 程序集', value: execution.editorAssemblyMvid });
    if (state.hostFrame !== null) fields.push({ displayName: state.summary.frameDisplayName, value: state.hostFrame });
  }
  if (state.frame?.consistency) fields.push({ displayName: state.frame.consistency.displayName, value: state.frame.consistency.value });
  if (state.routeProtocol) fields.push({ displayName: '路线协议', value: state.routeProtocol });
  if (state.inputProtocol) fields.push({ displayName: '输入协议', value: state.inputProtocol });
  for (const item of [...(state.summary?.availability ?? []), ...(state.frame?.availability ?? []),
    ...state.routeAvailability, ...state.graphAvailability, ...state.metadataAvailability, ...(state.frame?.route?.availability ?? [])]) {
    if (!gap(item)) continue;
    const displayName = `缺项 ${item.channel}`, value = item.reason || (item.available ? '不完整' : '不可用');
    if (!fields.some(field => field.displayName === displayName && field.value === value)) fields.push({ displayName, value });
  }
  return <IdentityFold fields={fields} />;
}

// ── 技能 ──

// 打开技能、没有选中对象时（照 Unity 选中资产本身）：技能名与所含 Timeline 数，下面是预览输入方案（作者模式可编辑）。
function SkillSummary() {
  const { skill, plans, editable, noProgram } = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    return { skill: state.target?.name ?? null, editable: state.author,
      noProgram: state.target?.kind === 'skill' && document.documentGraphs !== null && document.programId === null && document.documentError === null,
      plans: [...new Set([...Object.keys(document.files), ...Object.keys(document.edited)])]
        .filter(path => path.startsWith('inputs/') && !document.removed.includes(path)).sort().join('\n') };
  }));
  const timelines = useAuthorTimelines().length;
  const paths = plans ? plans.split('\n') : [];
  return (
    <>
      <Head name={skill ?? '技能'} sub={`技能 · ${timelines} 个 Timeline`} />
      {noProgram && <div className={styles.note}>当前技能没有执行图</div>}
      {paths.map((path, index) => (
        <Fold key={path} title={paths.length > 1 ? `预览输入 ${index + 1}` : '预览输入'} defaultOpen>
          <InputPlanFields path={path} editable={editable} />
        </Fold>
      ))}
      <Identity extra={paths.map(path => ({ displayName: '预览输入文件', value: path }))} />
    </>
  );
}

// ── 节点 ──

// 作者模式下按叠加了未保存修改的图查找，新建的状态与转移也能检查。
function useAuthorGraphs() {
  return useAuthoring(useShallow(state => ({
    on: state.author && state.target?.kind === 'skill',
    edited: authoringDocument(state).edited, ports: authoringDocument(state).addedPorts,
  })));
}

function NodeInspector({ id, call, missing }: { id: string; call: ExecutionCall | undefined; missing: ReactNode }) {
  const binding = useDocumentObservationBinding();
  const { graphs, frame, runId } = useWorkbench(useShallow(state => ({ graphs: state.graphs, frame: state.frame, runId: state.runId })));
  const author = useAuthorGraphs();
  const found = useMemo(() => {
    if (!author.on) return findGraphNode(graphs, id);
    const located = findEditedGraph(graphs, id, author.edited, author.ports);
    const node = located?.graph.nodes.find(item => item.id === located.id && item.invocationPath === located.path);
    return located && node ? { graph: located.graph, node } : null;
  }, [graphs, id, author]);
  const editable = useAuthoring(state => found !== null && state.author && ownsAuthoringGraph(state, found.graph.id));
  if (!found) return missing;
  const { graph, node } = found;
  const candidates = graphRecords(frame?.route, graph, binding);
  const observedCall = call ?? singleCall(candidates);
  const records = candidates.filter(item => observedCall !== null && callKey(item.call) === callKey(observedCall) &&
    'nodeId' in item && item.nodeId === node.id);
  const events = records.filter(isNodeOccurrence).sort(compareSequence);
  const samples = records.filter(isPortSample);
  const ports = graph.ports.filter(port => port.nodeId === node.id && port.invocationPath === node.invocationPath);
  const references = (
    <>
      {editable && <StateBody graph={graph} nodeId={node.id} />}
      <NodeLinks graph={graph} nodeId={node.id} invocationPath={node.invocationPath} />
    </>
  );
  return (
    <>
      <Head name={nodeName(node)} sub={graphName(graph)} />
      {editable ? <NodeFields graphId={graph.id} nodeId={node.id}>{references}</NodeFields>
        : <Fold title="节点" defaultOpen>{references}</Fold>}
      {runId && <Fold title="本帧" defaultOpen>
        <Value label="状态" mono={false} value={observedCall === null ? '尚未选择实际调用' : events.some(item => item.result === 'Failure') ? '失败' : events.length ? '已发生' : '未发生'} />
        {observedCall && <Value label="角色" value={observedCall.actorId} />}
        {events.length ? (
          <div className={styles.list}>
            {events.map(item => (
              <div key={item.sequence} className={styles.listRow}>
                <span className={styles.listText}>{nodeOccurrenceLabel(item)}</span>
                <span className={styles.listValue} data-failed={item.result === 'Failure' || undefined}>{item.result}</span>
              </div>
            ))}
          </div>
        ) : null}
      </Fold>}
      {runId && ports.length > 0 && <Fold title="端口取值" defaultOpen>
        <div className={styles.list}>
          {ports.map(port => {
            const sample = samples.filter(item => item.portId === port.id && item.direction === port.direction).at(-1);
            return (
              <Tip key={`${port.direction}:${port.id}`} content={sample && `${sample.valueType} · ${sample.sequence}`}>
              <div className={styles.listRow}>
                {port.isTrigger ? <i className={styles.exec} /> : <i className={styles.dot} style={{ ['--pc' as string]: portColor(sample?.valueType) }} />}
                <span className={styles.listText}>{port.displayName}{port.direction === 'input' ? '' : ' →'}</span>
                <span className={styles.listValue} data-sampled={sample ? true : undefined}>{sample?.value ?? ''}</span>
              </div>
              </Tip>
            );
          })}
        </div>
      </Fold>}
      <HostRecords records={locatedRecords(frame, binding, observedCall, (documentId, elementId) => documentId === graph.id && elementId === node.id)} />
      {records.length > 0 && <Fold title="原始记录"><Raw value={records} /></Fold>}
      <Identity extra={[
        { displayName: '节点', value: node.id }, { displayName: '图', value: graph.id },
        { displayName: '调用路径', value: node.invocationPath }, { displayName: '程序', value: graph.programId },
        { displayName: '程序修订', value: graph.programRevision },
      ]} />
    </>
  );
}

function NodeLinks({ graph, nodeId, invocationPath }: { graph: GraphDefinition; nodeId: string; invocationPath: string }) {
  const hierarchy = useDocumentGraphHierarchy();
  const children = nodeGraphs(hierarchy, graph, nodeId);
  const source = hierarchy.graphs.get(graphKey(graph))!;
  const names = new Map(graph.nodes.map(node => [node.id, nodeName(node)]));
  const transitions = graph.edges.filter(edge => edge.kind === 'Transition' && edge.source?.nodeId === nodeId &&
    edge.invocationPath === invocationPath);
  return (
    <>
      {children.map(child => (
        <Link key={child.id} label={childLabel(child)} text={graphName(child)} kind="var(--node-flow)" tip="在节点图面板打开"
          onOpen={() => openGraph(child, { owner: source.owner, ownerName: source.ownerName, graphId: source.id, nodeId })} />
      ))}
      {transitions.map(edge => {
        const target = edge.target ? names.get(edge.target.nodeId) ?? '—' : '收招';
        return <Value key={edge.id} label={`→ ${target}`} mono={false} value="条件图声明尚未提供" />;
      })}
    </>
  );
}

// 专属图（状态体、转移条件）：已在展开结果里的可进入；网页新建的保存后才有展开结果。
function OwnedGraph({ label, id, onCreate, onRemove, createText }: {
  label: string; id: string | null; onCreate: (() => Promise<unknown>) | null; onRemove(id: string): void; createText: string;
}) {
  const graphs = useWorkbench(state => state.graphs);
  const target = id ? graphs?.find(item => item.id === id) ?? null : null;
  const error = useAuthoring(state => authoringDocument(state).documentError);
  return (
    <div className={styles.field}>
      <FieldLabel label={label} />
      <div className={styles.actions}>
        {id === null ? <Button variant="ghost" disabled={!onCreate} onClick={() => {
          void onCreate?.().catch(error => consoleActions.error('作者', error));
        }}>{createText}</Button> : (
          <>
            {target ? <ObjectField kind="var(--node-flow)" text={graphName(target)} tip="在节点图面板打开" onOpen={() => openGraph(target)} />
              : <span className={styles.note}>{error ?? '正在读取作者图'}</span>}
            <Button variant="ghost" tip="移除；不再被引用的图保存时删除" onClick={() => onRemove(id)}>移除</Button>
          </>
        )}
      </div>
    </div>
  );
}

function StateBody({ graph, nodeId }: { graph: GraphDefinition; nodeId: string }) {
  const { document } = useCurrentDocument(graphPath(graph.id));
  const elements = useSkillElements(true);
  const node = ((document as JsonObject | null)?.allNodes as JsonObject[] | undefined)?.find(item => item.UID === nodeId);
  // 只有普通状态带状态体；入口、任意状态与出口是系统锚点。
  if (node?.type !== 'BtsmtlSkillNativeState') return null;
  return (
    <OwnedGraph label="状态体" id={(node.m_Body as string | null) ?? null} createText="新建状态体"
      onCreate={elements ? () => graphAuthoring.createStateBody(graph.id, nodeId) : null}
      onRemove={body => graphAuthoring.removeStateBody(graph.id, nodeId, body)} />
  );
}

// ── 连线：状态机转移与组合步骤的连线都带条件图；步骤的条件与调度存在步骤上，从这里编辑 ──

function EdgeInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const graphs = useWorkbench(state => state.graphs);
  const author = useAuthorGraphs();
  const found = useMemo(() => {
    const located = findEditedGraph(graphs, id, author.on ? author.edited : {}, author.ports);
    const edge = located?.graph.edges.find(item => item.id === located.id && item.invocationPath === located.path);
    return located && edge ? { graph: located.graph, edge } : null;
  }, [graphs, id, author]);
  const editable = useAuthoring(state => found !== null && state.author && ownsAuthoringGraph(state, found.graph.id));
  const elements = useSkillElements(editable);
  const { document } = useCurrentDocument(editable ? graphPath(found!.graph.id) : null);
  if (!found) return missing;
  const { graph, edge } = found;
  const transition = edge.kind === 'Transition';
  const names = new Map(graph.nodes.map(node => [node.id, nodeName(node)]));
  const port = edge.source && !transition ? graph.ports.find(item => item.nodeId === edge.source!.nodeId && item.id === edge.source!.portId &&
    item.direction === 'output' && item.invocationPath === edge.invocationPath)?.displayName : undefined;
  const name = `${edge.source ? names.get(edge.source.nodeId) ?? '—' : '—'}${port ? ` · ${port}` : ''} → ${edge.target ? names.get(edge.target.nodeId) ?? '—' : '收招'}`;
  const connection = (((document as JsonObject | null)?.allNodes as JsonObject[] | undefined) ?? [])
    .flatMap(node => node.outConnections as JsonObject[]).find(item => item.UID === edge.id);
  const step = editable && !transition ? edgeStep(document, edge.id) : null;
  const condition = transition ? ((connection?.Payload as JsonObject | undefined)?.m_Condition as string | null | undefined) ?? null
    : (step?.step.m_Condition as string | null | undefined) ?? null;
  const owned = editable && (transition || step);
  const conditionRows = owned ? (
    <OwnedGraph label="条件图" id={condition} createText="新建条件"
      onCreate={!elements ? null : step ? () => graphAuthoring.createStepCondition(graph.id, step)
        : () => graphAuthoring.createCondition(graph.id, edge.id)}
      onRemove={item => step ? graphAuthoring.removeStepCondition(graph.id, step, item) : graphAuthoring.removeCondition(graph.id, edge.id, item)} />
  ) : edge.kind === 'Value' ? null : <Value label="条件图" mono={false} value="条件图声明尚未提供" />;
  return (
    <>
      <Head name={name} sub={graphName(graph)} />
      {transition && editable && elements ? <TransitionFields graphId={graph.id} connectionId={edge.id} shape={elements.transition.shape}>{conditionRows}</TransitionFields>
        : step ? <StepFields graphId={graph.id} step={step}>{conditionRows}</StepFields>
          : <Fold title={transition ? '转移' : '连线'} defaultOpen>{conditionRows}</Fold>}
      <Identity extra={[
        { displayName: transition ? '转移' : '连线', value: edge.id }, { displayName: '图', value: graph.id }, { displayName: '调用路径', value: edge.invocationPath },
        { displayName: '程序', value: graph.programId },
      ]} />
    </>
  );
}

const childLabel = (graph: GraphNavigationTarget) => graph.role === 'StateMachine' ? '状态机' : '图引用';

// ── Timeline 片段与轨道 ──

function ClipInspector({ id, call, missing }: { id: string; call: ExecutionCall | undefined; missing: ReactNode }) {
  const binding = useDocumentObservationBinding();
  const schemes = useInputSchemes();
  const { timelines: shownTimelines, routeFrames, frame, graphs, runId } = useWorkbench(useShallow(state => ({
    timelines: state.timelines, routeFrames: state.routeFrames, frame: state.frame, graphs: state.graphs,
    runId: state.runId,
  })));
  // 作者模式下未保存的片段时间同样显示在检查器里。
  const { edited, removed, editable } = useAuthoring(useShallow(state => ({
    edited: authoringDocument(state).edited, removed: authoringDocument(state).removed, editable: state.author && state.target !== null,
  })));
  const timelines = useMemo(() => applyTimelineEdits(shownTimelines, edited, removed), [shownTimelines, edited, removed]);
  const found = useMemo(() => findTimelineClip(timelines, id), [timelines, id]);
  const { document: inputGraph } = useCurrentDocument(found?.clip.graphId ? graphPath(found.clip.graphId) : null);
  const instances = useMemo(() => {
    const result = new Map<string, { call: ExecutionCall; first: RouteFrame; last: RouteFrame; count: number }>();
    if (!found) return [];
    for (const recorded of routeFrames) {
      const calls = new Map<string, ExecutionCall>();
      for (const location of recorded.active)
        if (location.call !== null && matchesDocumentCall(location.call, binding) &&
          location.documentId === found.timeline.documentId && location.elementId === found.clip.id &&
          (call === undefined || callKey(location.call) === callKey(call)))
          calls.set(callKey(location.call), location.call);
      for (const [key, actualCall] of calls) {
        const previous = result.get(key);
        if (previous) { previous.last = recorded; previous.count++; }
        else result.set(key, { call: actualCall, first: recorded, last: recorded, count: 1 });
      }
    }
    return [...result.values()];
  }, [found, routeFrames, binding, call]);
  const marker = useMemo(() => found ? null : findTimelineMarker(timelines, id), [found, timelines, id]);
  if (!found) return marker ? <MarkerInspector found={marker} editable={editable} /> : missing;
  const { timeline, track, clip } = found;
  const input = schemes.some(scheme => scheme.documentId === timeline.documentId);
  const graph = clipGraph(graphs, clip.graphId);
  const observedCall = call ?? (instances.length === 1 ? instances[0].call : null);
  const graphRow = input || !clip.graphId ? null : graph
    ? <Link label="节点图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
    : <Value label="节点图" mono={false} value="当前技能没有这张图" />;
  return (
    <>
      <Head icon={<TrackIcon kind={input ? 'in' : clipKind(clip)} />} sub={`${timeline.displayName} · ${track.displayName}`}
        name={input ? (inputGraph as JsonObject | null)?.name as string ?? track.displayName
          : clip.displayName ?? (graph ? graphName(graph) : track.displayName)} />
      {editable ? <ClipFields documentId={timeline.documentId} clipId={clip.id} timeOnly={input}>{graphRow}</ClipFields> : (
        <Fold title="片段" defaultOpen>
          {graphRow}
          <Value label="起点（秒）" value={clip.startSeconds.toFixed(3)} />
          <Value label="终点（秒）" value={clip.endSeconds.toFixed(3)} />
        </Fold>
      )}
      {editable && input && clip.graphId && <InputClipFields documentId={timeline.documentId} graphId={clip.graphId} />}
      {input && <NativeInputInspector documentId={timeline.documentId} />}
      {runId && !input && <Fold title="已读取的活动记录" defaultOpen>
        {instances.length ? (
          <div className={styles.list}>
            {instances.map(item => {
              const active = frame?.route?.active.some(location => location.call !== null &&
                callKey(location.call) === callKey(item.call) && location.documentId === timeline.documentId && location.elementId === clip.id);
              return (
                <Tip key={callKey(item.call)} content="跳到已读取的首条活动记录">
                <div className={`${styles.listRow} ${styles.listLink}`} role="button" tabIndex={0}
                  onClick={() => void transport.seekTime(item.first.seconds)}
                  onKeyDown={event => { if (event.key === 'Enter') void transport.seekTime(item.first.seconds); }}>
                  <span className={styles.listText}>{item.call.actorId} · {item.count} 帧记录{active ? ' · 当前帧活动' : ''}</span>
                  <span className={styles.listValue} data-sampled={active || undefined}>{item.first.seconds.toFixed(3)} — {item.last.seconds.toFixed(3)} s</span>
                </div>
                </Tip>
              );
            })}
          </div>
        ) : <div className={styles.note}>{frame === null ? '正在读取当前帧'
          : frame.route === null ? '档案播放记录尚未提供' : binding.dirty ? '当前内容未保存，旧档案叠加已停用'
            : '已读取记录中没有匹配来源修订与调用的活动记录'}</div>}
      </Fold>}
      {!input && <HostRecords records={locatedRecords(frame, binding, observedCall, (documentId, elementId) => documentId === timeline.documentId && elementId === clip.id)} />}
      <Identity extra={[
        { displayName: '片段', value: clip.id }, { displayName: '合同', value: clip.contract }, { displayName: 'Timeline', value: timeline.documentId },
        ...(clip.graphId ? [{ displayName: '节点图', value: clip.graphId }] : []),
        { displayName: 'Timeline 来源修订', value: timeline.contentRevision ?? '未保存或尚未读取来源修订' },
      ]} />
    </>
  );
}

// 标记：时刻与触发图；作者模式可改时刻。输入标记与输入片段一样直接读触发图文件，编辑写入的按键与值。
function MarkerInspector({ found, editable }: { found: NonNullable<ReturnType<typeof findTimelineMarker>>; editable: boolean }) {
  const { timeline, track, marker } = found;
  const graph = useWorkbench(state => clipGraph(state.graphs, marker.graphId));
  const input = useInputSchemes().some(scheme => scheme.documentId === timeline.documentId);
  const tickRate = useAuthoring(state => authoringDocument(state).tickRate);
  const step = tickRate ? 1 / tickRate : 1 / 60;
  return (
    <>
      <Head icon={<TrackIcon kind={input ? 'in' : trackKind(track)} />} name={`${track.displayName} 标记`} sub={timeline.displayName} />
      <Fold title="标记" defaultOpen>
        {input ? null : graph
          ? <Link label="触发图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
          : <Value label="触发图" mono={false} value="未保存，保存后可进入" />}
        {editable ? (
          <div className={styles.field}>
            <label>时刻（秒）</label>
            <NumberInput wide label="时刻" value={marker.seconds} min={0} step={step}
              onChange={next => { if (next !== null) authoringActions.setMarkerTime(timeline.documentId, marker.id, Math.round(next / step) * step); }} />
          </div>
        ) : <Value label="时刻（秒）" value={marker.seconds.toFixed(3)} />}
      </Fold>
      {editable && input && marker.graphId && <InputClipFields documentId={timeline.documentId} graphId={marker.graphId} />}
      <Identity extra={[{ displayName: '标记', value: marker.id }, { displayName: 'Timeline', value: timeline.documentId }]} />
    </>
  );
}

function TrackInspector({ id, call, missing }: { id: string; call: ExecutionCall | undefined; missing: ReactNode }) {
  const binding = useDocumentObservationBinding();
  const schemes = useInputSchemes();
  const { timelines: shownTimelines, frame } = useWorkbench(useShallow(state => ({ timelines: state.timelines, frame: state.frame })));
  const { edited, removed, editable } = useAuthoring(useShallow(state => ({
    edited: authoringDocument(state).edited, removed: authoringDocument(state).removed, editable: state.author && state.target !== null,
  })));
  const timelines = useMemo(() => applyTimelineEdits(shownTimelines, edited, removed), [shownTimelines, edited, removed]);
  const found = useMemo(() => findTimelineTrack(timelines, id), [timelines, id]);
  if (!found) return missing;
  const { timeline, track } = found;
  const input = schemes.some(scheme => scheme.documentId === timeline.documentId);
  return (
    <>
      <Head icon={<TrackIcon kind={input ? 'in' : trackKind(track)} />} name={track.displayName} sub={timeline.displayName} />
      {editable ? <TrackFields documentId={timeline.documentId} trackId={track.id} /> : (
        <Fold title="轨道" defaultOpen><Value label="静音" mono={false} value={track.muted ? '是' : '否'} /></Fold>
      )}
      {input && <NativeInputInspector documentId={timeline.documentId} />}
      {!input && <HostRecords records={locatedRecords(frame, binding, call ?? null, (documentId, elementId) => documentId === timeline.documentId && elementId === track.id)} />}
      <Identity extra={[{ displayName: '轨道', value: track.id }, { displayName: 'Timeline', value: timeline.documentId }]} />
    </>
  );
}

// ── 动画资源 ──

// 照 Unity 选中动画资产：资源清单记录的素材信息只读显示，资源与产物哈希放身份折叠。
function ResourceInspector({ identity }: { identity: string }) {
  const libraryState = useAnimationLibrary(true);
  if (libraryState.kind === 'loading') return <Empty text="读取中" />;
  if (libraryState.kind === 'unavailable') return <div className={styles.missing}>{libraryState.reason}</div>;
  const library = libraryState.library;
  const clip = library.clips.find(item => item.resource.identity === identity);
  if (!clip) return <div className={styles.missing}>动画库不包含所选资源，请重新导出动画库；原身份已保留</div>;
  return (
    <>
      <Head icon={<TrackIcon kind="anim" />} name={clip.name} sub={`动画 · ${clip.category}`} />
      <Fold title="动画" defaultOpen>
        <Value label="Unity 来源" value={clip.unityAsset} />
        <Value label="素材时长（秒）" value={clip.duration.toFixed(3)} />
        <Value label="glb 时长（秒）" value={clip.exportedDuration.toFixed(3)} />
        <Value label="源帧率" value={String(clip.frameRate)} />
        <Value label="来源说明" mono={false} value={clip.provenance.description} />
      </Fold>
      <IdentityFold fields={[
        { displayName: '资源身份', value: clip.resource.identity }, { displayName: 'GUID', value: clip.resource.guid },
        { displayName: 'Local file ID', value: clip.resource.localFileId },
        { displayName: '来源资产 SHA-256', value: clip.resource.contentRevision },
        { displayName: '来源 Blend SHA-256', value: library.sourceSha256 }, { displayName: 'glb SHA-256', value: library.sha256 },
      ]} />
    </>
  );
}

// ── 预览输入 ──

function CurveInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const [, sourceRun, ...path] = id.split('/');
  const curveId = path.join('/');
  const { runId, curves, frame, hostFrame } = useWorkbench(useShallow(state => ({
    runId: state.runId, curves: state.archiveCurves, frame: state.archiveFrame, hostFrame: state.hostFrame,
  })));
  if (runId !== sourceRun) return <Empty text="此曲线属于另一档案" />;
  const curve = curves?.find(item => item.id === curveId);
  if (!curve) return missing;
  const record = frame?.hostFrame === hostFrame ? frame.curves[curveId] : undefined;
  const unavailable = record?.kind === 'sampled' ? record.availability.find(item =>
    (item.channel === curve.channel || item.channel === 'data-curves') && !item.available)?.reason : null;
  return <>
    <Head name={curve.displayName} />
    <Fold title="曲线" defaultOpen>
      <Value label="通道 / 时钟" value={`${curve.channel} / ${curve.clock}`} />
      <Value label="坐标 / 单位" value={`${curve.coordinates} / ${curve.unit}`} />
      <Value label="分量" value={curve.component} />
      <Value label="编码" value={curve.encoding} />
    </Fold>
    <Fold title="当前帧样本" defaultOpen>
      {record === undefined ? <div className={styles.note}>正在读取曲线</div>
        : record.kind === 'failed' ? <ErrorLine text={record.reason} entryId={record.entryId} />
          : unavailable ? <div className={styles.note}>{unavailable}</div>
            : record.samples.length === 0 ? <div className={styles.note}>此帧没有样本</div>
              : record.samples.map((sample, index) => <Fold key={index} title={`${sample.source.clock.positionDisplayName} ${sample.source.clock.position.hostFrame}`} defaultOpen>
                <Value label="显示值" value={String(sample.value)} />
                <Value label="原始值" value={sample.rawValue} />
                {sample.presentationSequence !== null && <Value label="表现序号" value={sample.presentationSequence} />}
                {sample.presentationTick !== null && <Value label="表现 Tick" value={String(sample.presentationTick)} />}
                <Identity extra={[
                  ...sample.source.clock.fields,
                  ...(sample.source.sceneRevision === null ? [] : [{ displayName: '场景修订', value: sample.source.sceneRevision }]),
                  { displayName: '输入修订', value: sample.source.inputRevision ?? '无固定修订' },
                ]} />
              </Fold>)}
    </Fold>
    <Identity extra={[
      { displayName: '档案', value: sourceRun }, { displayName: '曲线', value: curve.id },
      { displayName: '角色', value: curve.actorId }, { displayName: '目标', value: curve.targetId },
      { displayName: '档案坐标约定', value: curve.frameCoordinates },
    ]} />
  </>;
}

function NativeInputInspector({ documentId }: { documentId: string }) {
  const observation = useInputObservation(documentId);
  const runId = useWorkbench(state => state.runId);
  if (!runId) return null;
  if (observation.kind !== 'ready') return <Fold title="实际采用输入" defaultOpen>
    <div className={styles.note}>{observation.reason}</div>
  </Fold>;
  const { input } = observation;
  return <Fold title="实际采用输入" defaultOpen>
    <Value label="SDK 帧" value={observation.frame.hostFrame} />
    <Value label="已收到" value={input.received ? '是' : '否'} />
    {input.received && <>
      {input.values.map(value => <Value key={value.inputId} label={value.inputId}
        value={JSON.stringify(value.runtimeValue === null ? value.value : value.runtimeValue)} />)}
      {input.requests.map(request => <Value key={`${request.requestId}/${request.sequence}`} label={request.requestId}
        value={`序号 ${request.sequence} · Tick ${request.sourceTick}—${request.expireTick} · 优先级 ${request.priority}`} />)}
      <Identity extra={[
        { displayName: '输入序号', value: input.inputSequence }, { displayName: '来源 Tick', value: input.sourceTick },
        { displayName: '输入修订', value: observation.plan.planRevision },
        { displayName: '数值编码', value: observation.valueEncoding ?? '未声明' },
      ]} />
    </>}
  </Fold>;
}

function useInputTrack(trackId: string) {
  const roots = useWorkbench(state => state.inputTimeline?.roots);
  return useMemo(() => {
    for (const root of roots ?? []) {
      const track = root.tracks.find(item => item.id === trackId);
      if (track) return { root, track };
    }
    return null;
  }, [roots, trackId]);
}

function InputItemInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const [, trackId, itemId] = id.split('/');
  const found = useInputTrack(trackId);
  const graphs = useWorkbench(state => state.graphs);
  const clip = found?.track.clips.find(item => item.id === itemId);
  const marker = found?.track.markers.find(item => item.id === itemId);
  if (!found || (!clip && !marker)) return missing;
  const writes = (clip ?? marker)!.writes;
  const graph = clipGraph(graphs, (clip ?? marker)!.graphId);
  return (
    <>
      <Head icon={<TrackIcon kind="in" />} name={[...new Set(writes.map(write => write.keyId))].join(' / ') || '输入'}
        sub={`${found.root.displayName} · ${found.track.displayName}`} />
      <Fold title={clip ? '按住段' : '输入标记'} defaultOpen>
        {graph
          ? <Link label="节点图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
          : <Value label="节点图" mono={false} value="当前技能没有这张图" />}
        {clip ? (
          <>
            <Value label="起点（秒）" value={clip.startSeconds.toFixed(3)} />
            <Value label="终点（秒）" value={clip.endSeconds.toFixed(3)} />
          </>
        ) : <Value label="时刻（秒）" value={marker!.seconds.toFixed(3)} />}
      </Fold>
      <Fold title="写入" defaultOpen>
        <div className={styles.list}>
          {writes.map((write, index) => {
            const text = `${write.keyId} · ${write.fields.map(field => `${field.displayName}=${field.value}`).join(' · ')}`;
            return (
              <Tip key={index} name={text}>
                <div className={styles.listRow}>
                  <span className={styles.listText} data-name>{text}</span>
                  <span className={styles.listValue}>{write.seconds.toFixed(3)} s</span>
                </div>
              </Tip>
            );
          })}
        </div>
      </Fold>
      <Identity extra={[
        { displayName: '输入项', value: itemId }, { displayName: '图', value: (clip ?? marker)!.graphId },
        { displayName: '输入程序', value: `${found.root.programId} · ${found.root.programRevision}` },
        { displayName: '输入方案', value: `${found.root.planId} · ${found.root.planRevision}` },
      ]} />
    </>
  );
}

function InputTrackInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const found = useInputTrack(id.split('/')[1]);
  if (!found) return missing;
  return (
    <>
      <Head icon={<TrackIcon kind="in" />} name={found.track.displayName} sub={found.root.displayName} />
      <Fold title="输入轨" defaultOpen>
        <Value label="角色" value={found.root.actorId} />
        <Value label="方案零点（秒）" value={found.root.timeOriginSeconds.toFixed(3)} />
      </Fold>
      <Identity extra={[{ displayName: '输入轨', value: found.track.id }]} />
    </>
  );
}

// ── 角色 ──

function ActorInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const actor = useWorkbench(state => state.frame?.actors.find(item => item.id === id));
  if (!actor) return missing;
  return (
    <>
      <Head name={actor.displayName} />
      <Fold title="变换" defaultOpen>
        <Value label="位置" value={actor.root.position.map(value => value.toFixed(3)).join(', ')} />
        <Value label="旋转" value={actor.root.rotation.map(value => value.toFixed(3)).join(', ')} />
      </Fold>
      <Fold title="本帧" defaultOpen><Values fields={actor.fields} /></Fold>
      <Fold title="原始记录"><Raw value={actor.rawJson} /></Fold>
      <Identity extra={[{ displayName: '角色', value: actor.id }]} />
    </>
  );
}
