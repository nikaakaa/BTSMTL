import { useMemo, type ReactNode } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Availability, Field, Frame, GraphDefinition, HostRecord } from '../../api/types';
import { openPanel } from '../../shell/layout';
import { usePanelParams } from '../../shell/panel';
import {
  childGraphs, edgeChildGraphs, findGraphNode, graphHierarchy, graphKey, graphName, graphRecords, isNodeOccurrence,
  isPortSample, nodeName, playedTimeline, portColor,
} from '../../state/graphModel';
import {
  applyTimelineEdits, authoringActions, findEditedGraph, graphAuthoring, graphPath, useAuthoring, useCurrentDocument, useSkillElements,
} from '../../state/authoring';
import type { JsonObject } from '../../state/skillJson';
import { stageNames } from '../../state/session';
import { getState, selectionActions, useWorkbench, type Selection } from '../../state/store';
import {
  clipGraph, clipKind, findTimelineClip, findTimelineMarker, findTimelineTrack, playSegments, trackColors, trackKind,
} from '../../state/timelineModel';
import { transport } from '../../state/transport';
import { BarLabel, BarSeparator, Empty, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, ObjectField } from '../../ui/controls';
import { ClipFields, InputPlanFields, NodeFields, TrackFields, TransitionFields } from './FieldEditors';
import styles from './inspector.module.css';

// 检查器（D7）：按 selection 显示所选对象在当前帧的记录；不选中时显示运行摘要。
// 锁定的实例固定显示锁定对象；身份、修订与哈希只放底部身份折叠。

interface Params {
  locked: Selection | null;
}

const kindNames: Record<Selection['kind'], string> = { node: '节点', edge: '连线', clip: '片段', track: '轨道', actor: '角色' };

export function InspectorPanel() {
  const [params, setParams] = usePanelParams<Params>({ locked: null });
  const selection = useWorkbench(state => state.selection);
  const skill = useAuthoring(state => state.skill);
  const shown = params.locked ?? selection;
  const content = !skill ? <Empty text="选择一个技能" />
      : shown ? <Content key={`${skill}:${shown.kind}:${shown.id}`} shown={shown} locked={!!params.locked} /> : <SkillSummary />;
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" pressed={!!params.locked} disabled={!params.locked && !selection}
          tip={params.locked ? '解除锁定，跟随选中对象' : selection ? '锁定当前对象' : '没有选中对象'}
          onClick={() => setParams({ locked: params.locked ? null : selection })}>锁定</Button>
        <BarSeparator />
        <BarLabel>{shown ? kindNames[shown.kind] : '技能'}</BarLabel>
      </PanelBar>
      <div className={styles.scroll}>
        {content}
      </div>
    </div>
  );
}

function Content({ shown, locked }: { shown: Selection | null; locked: boolean }) {
  const missing = <Empty text={shown?.kind === 'actor' ? '档案中没有所选角色' : locked ? '锁定对象不属于当前技能' : '所选对象不属于当前技能'} />;
  if (!shown) return <RunSummary />;
  switch (shown.kind) {
    case 'node': return <NodeInspector id={shown.id} missing={missing} />;
    case 'clip': return shown.id.startsWith('input/') ? <InputItemInspector id={shown.id} missing={missing} />
      : <ClipInspector id={shown.id} missing={missing} />;
    case 'track': return shown.id.startsWith('input/') ? <InputTrackInspector id={shown.id} missing={missing} />
      : <TrackInspector id={shown.id} missing={missing} />;
    case 'actor': return <ActorInspector id={shown.id} missing={missing} />;
    case 'edge': return <EdgeInspector id={shown.id} missing={missing} />;
  }
}

// ── 通用部件 ──

function Head({ kind, name, sub }: { kind: string; name: ReactNode; sub?: ReactNode }) {
  return (
    <div className={styles.head}>
      <span className={styles.big} style={{ ['--kind' as string]: kind }} />
      <span className={`${styles.box} ${styles.name}`}>{name}</span>
      <span className={styles.subline}>{sub}</span>
    </div>
  );
}

function Value({ label, value, mono = true }: { label: string; value: ReactNode; mono?: boolean }) {
  return (
    <div className={styles.field}>
      <label title={label}>{label}</label>
      <span className={`${styles.box} ${mono ? styles.mono : ''}`} title={typeof value === 'string' ? value : undefined}>{value}</span>
    </div>
  );
}

// 引用字段：照 Unity 对象引用框，点击在节点图或 Timeline 面板打开所引用的对象。
function Link({ label, text, kind, tip, onOpen }: { label: string; text: ReactNode; kind: string; tip: string; onOpen(): void }) {
  return <div className={styles.field}><label title={label}>{label}</label><ObjectField kind={kind} text={text} tip={tip} onOpen={onOpen} /></div>;
}

function openGraph(graph: GraphDefinition) {
  selectionActions.showGraph(graphKey(graph));
  openPanel('graph');
}

function openTimeline(documentId: string) {
  selectionActions.showTimeline(documentId);
  openPanel('timeline');
}

function useTimelineName(documentId: string | null) {
  return useWorkbench(state => documentId ? state.timelines.find(item => item.documentId === documentId)?.displayName ?? documentId.slice(0, 8) : null);
}

// 哈希类身份值（十六进制长串）只放身份折叠。
const isIdentityValue = (field: Field) => /^[0-9a-f]{16,}$/i.test(field.value);

function Values({ fields }: { fields: Field[] }) {
  return <>{fields.map((field, index) => <Value key={index} label={field.displayName} value={field.value} />)}</>;
}

function Raw({ value }: { value: unknown }) {
  return <pre className={styles.raw}>{typeof value === 'string' ? value : JSON.stringify(value, null, 2)}</pre>;
}

function HostRecords({ records }: { records: HostRecord[] }) {
  if (!records.length) return null;
  return (
    <Fold title="宿主记录" kind="var(--node-cmp)" defaultOpen>
      {records.map((record, index) => (
        <Fold key={index} title={record.displayName}>
          <Values fields={record.fields} />
          <Fold title="原始记录"><Raw value={record.rawJson} /></Fold>
        </Fold>
      ))}
    </Fold>
  );
}

function locatedRecords(frame: Frame | null, match: (documentId: string, elementId: string) => boolean): HostRecord[] {
  if (!frame) return [];
  return frame.sections.flatMap(section => section.records)
    .filter(record => record.locations.some(location => match(location.documentId, location.elementId)));
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
    if (state.hostFrame !== null) fields.push({ displayName: state.summary.frameDisplayName || 'SDK 帧', value: state.hostFrame });
  }
  if (state.frame?.consistency) fields.push({ displayName: state.frame.consistency.displayName, value: state.frame.consistency.value });
  if (state.routeProtocol) fields.push({ displayName: '路线协议', value: state.routeProtocol });
  if (state.inputProtocol) fields.push({ displayName: '输入协议', value: state.inputProtocol });
  for (const item of [...state.routeAvailability, ...state.graphAvailability, ...state.metadataAvailability, ...(state.frame?.route?.availability ?? [])])
    if (gap(item)) fields.push({ displayName: `缺项 ${item.channel}`, value: item.reason || (item.available ? '不完整' : '不可用') });
  return <IdentityFold fields={fields} />;
}

// ── 运行摘要 ──

function RunSummary() {
  const { summary, receipt, frame, failure } = useWorkbench(useShallow(state => ({
    summary: state.summary, receipt: state.receipt, frame: state.frame, failure: state.failure,
  })));
  if (!summary) return <><Empty text={failure?.reason ?? (receipt ? stageNames[receipt.stage] : '读取中')} /><Identity /></>;
  return (
    <>
      <Head kind="var(--accent)" name="运行" sub={receipt ? `${stageNames[receipt.stage]} · ${new Date(receipt.acceptedAt).toLocaleString()}` : null} />
      <Fold title="运行" kind="var(--accent)" defaultOpen>
        <Value label="确认帧数" value={String(summary.frameCount)} />
        <Value label="起止帧" value={`${summary.firstFrame} — ${summary.lastFrame}`} />
        <Values fields={summary.fields.filter(field => !isIdentityValue(field) && field.value !== summary.runId)} />
      </Fold>
      {frame && (
        <Fold title="本帧" kind="var(--value)" defaultOpen>
          <Value label="秒数" value={frame.seconds.toFixed(3)} />
          <Values fields={frame.fields} />
        </Fold>
      )}
      <PlayerStatus />
      {frame?.actors.map(actor => (
        <Fold key={actor.id} title={actor.displayName} kind="var(--track-anim)">
          <Value label="根位置" value={actor.root.position.map(value => value.toFixed(3)).join(', ')} />
          <Values fields={actor.fields} />
        </Fold>
      ))}
      {frame?.sections.map((section, index) => (
        <Fold key={index} title={section.displayName} kind="var(--node-flow)">
          {section.records.length ? section.records.map((record, recordIndex) => (
            <Fold key={recordIndex} title={record.displayName}><Values fields={record.fields} /></Fold>
          )) : <div className={styles.note}>{section.emptyMessage}</div>}
        </Fold>
      ))}
      <Identity extra={summary.fields.filter(field => isIdentityValue(field) && field.value !== summary.runId)} />
    </>
  );
}

// 打开技能、没有选中对象时：技能的预览输入方案（作者模式可编辑）。
function SkillSummary() {
  const { skill, plans, editable } = useAuthoring(useShallow(state => ({
    skill: state.skill, editable: state.author,
    plans: [...new Set([...Object.keys(state.files), ...Object.keys(state.edited)])]
      .filter(path => path.startsWith('inputs/') && !state.removed.includes(path)).sort().join('\n'),
  })));
  return (
    <>
      <Head kind="var(--accent)" name={skill ?? '技能'} sub="在节点图或 Timeline 中选中对象查看与编辑" />
      {plans ? plans.split('\n').map(path => (
        <Fold key={path} title={`预览输入 · ${path.slice('inputs/'.length, -'.json'.length).slice(0, 8)}`} kind="var(--track-in)" defaultOpen>
          <InputPlanFields path={path} editable={editable} />
        </Fold>
      )) : <div className={styles.note}>此技能没有预览输入方案</div>}
    </>
  );
}

function PlayerStatus() {
  const { live, targetFrame, actualFrame, lastCommand } = useWorkbench(useShallow(state => ({
    live: state.live, targetFrame: state.targetFrame, actualFrame: state.actualFrame,
    lastCommand: state.lastCommand,
  })));
  if (!live) return null;
  return (
    <Fold title="播放器" kind="var(--track-cam)" defaultOpen>
      <Value label="播放器进程" mono={false} value={live.state} />
      {live.player && <Values fields={live.player.fields} />}
      {targetFrame !== null && <Value label="目标帧 / 实际帧" value={`${targetFrame} / ${actualFrame ?? '—'}`} />}
      {lastCommand && <Value label="最后命令" value={`${lastCommand.type} · ${lastCommand.stage}${lastCommand.error ? ` · ${lastCommand.error}` : ''}`} />}
    </Fold>
  );
}

// ── 节点 ──

// 作者模式下按叠加了未保存修改的图查找，新建的状态与转移也能检查。
function useAuthorGraphs() {
  return useAuthoring(useShallow(state => ({
    program: state.author && state.skill ? state.programId : null, edited: state.edited, ports: state.addedPorts,
  })));
}

function NodeInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { graphs, frame } = useWorkbench(useShallow(state => ({ graphs: state.graphs, frame: state.frame })));
  const author = useAuthorGraphs();
  const found = useMemo(() => {
    if (author.program === null) return findGraphNode(graphs, id);
    const located = findEditedGraph(graphs, id, author.edited, author.ports);
    const node = located?.graph.nodes.find(item => item.id === located.id && item.invocationPath === located.path);
    return located && node ? { graph: located.graph, node } : null;
  }, [graphs, id, author]);
  if (!found) return missing;
  const { graph, node } = found;
  const editable = author.program !== null && author.program === graph.programId;
  const records = graphRecords(frame, graph).filter(item => 'nodeId' in item && item.nodeId === node.id &&
    item.call.invocationPath === node.invocationPath);
  const events = records.filter(isNodeOccurrence).sort((left, right) => Number(left.sequence) - Number(right.sequence));
  const samples = records.filter(isPortSample);
  const ports = graph.ports.filter(port => port.nodeId === node.id && port.invocationPath === node.invocationPath);
  const entry = !graph.edges.some(edge => edge.target?.nodeId === node.id && edge.invocationPath === node.invocationPath);
  const kind = entry ? 'var(--node-event)' : 'var(--node-flow)';
  return (
    <>
      <Head kind={kind} name={nodeName(node)} sub={`${graph.programId} · ${graphName(graph)}`} />
      {editable && <NodeFields graphId={graph.id} nodeId={node.id} />}
      {editable && <StateBody graph={graph} nodeId={node.id} />}
      <NodeLinks graph={graph} nodeId={node.id} invocationPath={node.invocationPath} />
      <Fold title="本帧" kind={kind} defaultOpen>
        <Value label="状态" mono={false} value={events.some(item => item.result === 'Failure') ? '失败' : events.length ? '已发生' : '未发生'} />
        {events.length ? (
          <div className={styles.list}>
            {events.map(item => (
              <div key={item.sequence} className={styles.listRow}>
                <span className={styles.listText}>{item.kind}</span>
                <span className={styles.listValue} data-failed={item.result === 'Failure' || undefined}>{item.result}</span>
              </div>
            ))}
          </div>
        ) : null}
      </Fold>
      <Fold title="端口" kind="var(--port-float)" defaultOpen>
        {ports.length ? (
          <div className={styles.list}>
            {ports.map(port => {
              const sample = samples.filter(item => item.portId === port.id && item.direction === port.direction).at(-1);
              return (
                <div key={`${port.direction}:${port.id}`} className={styles.listRow} title={sample ? `${sample.valueType} · ${sample.sequence}` : undefined}>
                  {port.isTrigger ? <i className={styles.exec} /> : <i className={styles.dot} style={{ ['--pc' as string]: portColor(sample?.valueType) }} />}
                  <span className={styles.listText}>{port.displayName}{port.direction === 'input' ? '' : ' →'}</span>
                  <span className={styles.listValue} data-sampled={sample ? true : undefined}>{sample?.value ?? ''}</span>
                </div>
              );
            })}
          </div>
        ) : <div className={styles.note}>没有端口</div>}
      </Fold>
      <HostRecords records={locatedRecords(frame, (_, elementId) => elementId === node.id)} />
      {records.length > 0 && <Fold title="原始记录" kind="var(--node-flow)"><Raw value={records} /></Fold>}
      <Identity extra={[
        { displayName: '节点', value: node.id }, { displayName: '图', value: graph.id },
        { displayName: '调用路径', value: node.invocationPath }, { displayName: '程序修订', value: graph.programRevision },
      ]} />
    </>
  );
}

// 节点的引用：所在图、子图（状态体或调用的状态机）、播放的 Timeline、出去的转移及其条件图。
function NodeLinks({ graph, nodeId, invocationPath }: { graph: GraphDefinition; nodeId: string; invocationPath: string }) {
  const graphs = useWorkbench(state => state.graphs);
  const hierarchy = useMemo(() => graphHierarchy(graphs ?? []), [graphs]);
  const children = childGraphs(hierarchy, graph, nodeId);
  const timeline = playedTimeline(hierarchy, graph, nodeId);
  const timelineName = useTimelineName(timeline);
  const names = new Map(graph.nodes.map(node => [node.id, nodeName(node)]));
  const transitions = graph.edges.filter(edge => edge.kind === 'Transition' && edge.source?.nodeId === nodeId &&
    edge.invocationPath === invocationPath);
  return (
    <Fold title="引用" kind="var(--node-cmp)" defaultOpen>
      <Link label="所在图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
      {children.map(child => (
        <Link key={child.id} label={childLabel(child)} text={graphName(child)} kind="var(--node-flow)" tip="在节点图面板打开"
          onOpen={() => openGraph(child)} />
      ))}
      {timeline && <Link label="Timeline" text={timelineName} kind="var(--track-anim)" tip="在 Timeline 面板进入"
        onOpen={() => openTimeline(timeline)} />}
      {transitions.map(edge => {
        const conditions = edgeChildGraphs(hierarchy, graph, edge.id);
        const target = edge.target ? names.get(edge.target.nodeId) ?? '—' : '收招';
        return conditions.length
          ? conditions.map(condition => (
            <Link key={`${edge.id}/${condition.id}`} label={`→ ${target}`} text={graphName(condition)} kind="var(--port-bool)"
              tip="转移条件图，在节点图面板打开" onOpen={() => openGraph(condition)} />
          ))
          : <Value key={edge.id} label={`→ ${target}`} mono={false} value="无条件" />;
      })}
    </Fold>
  );
}

// 专属图（状态体、转移条件）：已在展开结果里的可进入；网页新建的保存后才有展开结果。
function OwnedGraph({ label, owner, id, onCreate, onRemove, createText }: {
  label: string; owner: GraphDefinition; id: string | null; onCreate: (() => void) | null; onRemove(id: string): void; createText: string;
}) {
  const graphs = useWorkbench(state => state.graphs);
  const target = id ? graphs?.find(item => item.id === id && item.programId === owner.programId) ?? null : null;
  return (
    <div className={styles.field}>
      <label title={label}>{label}</label>
      <div className={styles.actions}>
        {id === null ? <Button variant="ghost" disabled={!onCreate} onClick={() => onCreate?.()}>{createText}</Button> : (
          <>
            {target ? <ObjectField kind="var(--node-flow)" text={graphName(target)} tip="在节点图面板打开" onOpen={() => openGraph(target)} />
              : <span className={styles.note}>未保存，保存后可进入</span>}
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
    <Fold title="状态体" kind="var(--node-flow)" defaultOpen>
      <OwnedGraph label="状态体" owner={graph} id={(node.m_Body as string | null) ?? null} createText="新建状态体"
        onCreate={elements ? () => graphAuthoring.createStateBody(graph.id, elements, nodeId) : null}
        onRemove={body => graphAuthoring.removeStateBody(graph.id, nodeId, body)} />
    </Fold>
  );
}

// ── 状态机转移 ──

function EdgeInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const graphs = useWorkbench(state => state.graphs);
  const author = useAuthorGraphs();
  const found = useMemo(() => {
    const located = findEditedGraph(graphs, id, author.program === null ? {} : author.edited, author.ports);
    const edge = located?.graph.edges.find(item => item.id === located.id && item.invocationPath === located.path);
    return located && edge ? { graph: located.graph, edge } : null;
  }, [graphs, id, author]);
  const hierarchy = useMemo(() => graphHierarchy(graphs ?? []), [graphs]);
  const editable = !!found && author.program === found.graph.programId;
  const elements = useSkillElements(editable);
  const { document } = useCurrentDocument(editable ? graphPath(found!.graph.id) : null);
  if (!found) return missing;
  const { graph, edge } = found;
  const names = new Map(graph.nodes.map(node => [node.id, nodeName(node)]));
  const name = `${edge.source ? names.get(edge.source.nodeId) ?? '—' : '—'} → ${edge.target ? names.get(edge.target.nodeId) ?? '—' : '收招'}`;
  const connection = (((document as JsonObject | null)?.allNodes as JsonObject[] | undefined) ?? [])
    .flatMap(node => node.outConnections as JsonObject[]).find(item => item.UID === edge.id);
  const condition = ((connection?.Payload as JsonObject | undefined)?.m_Condition as string | null | undefined) ?? null;
  return (
    <>
      <Head kind="var(--port-bool)" name={name} sub={`${graph.programId} · ${graphName(graph)}`} />
      {editable && elements && <TransitionFields graphId={graph.id} connectionId={edge.id} shape={elements.transition.shape} />}
      <Fold title="条件" kind="var(--port-bool)" defaultOpen>
        {editable ? (
          <OwnedGraph label="条件图" owner={graph} id={condition} createText="新建条件"
            onCreate={elements ? () => graphAuthoring.createCondition(graph.id, elements, edge.id) : null}
            onRemove={item => graphAuthoring.removeCondition(graph.id, edge.id, item)} />
        ) : edgeChildGraphs(hierarchy, graph, edge.id).map(item => (
          <Link key={item.id} label="条件图" text={graphName(item)} kind="var(--port-bool)" tip="在节点图面板打开" onOpen={() => openGraph(item)} />
        ))}
        {!editable && !edgeChildGraphs(hierarchy, graph, edge.id).length && <Value label="条件图" mono={false} value="无条件" />}
        <Link label="所在图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
      </Fold>
      {editable && (
        <div className={styles.actions}>
          <Button variant="ghost" tip="删除这条转移；不再被引用的条件图保存时删除"
            onClick={() => { graphAuthoring.disconnect(graph.id, [edge.id]); selectionActions.select(null); }}>删除转移</Button>
        </div>
      )}
      <Identity extra={[
        { displayName: '转移', value: edge.id }, { displayName: '图', value: graph.id }, { displayName: '调用路径', value: edge.invocationPath },
      ]} />
    </>
  );
}

const childLabel = (graph: GraphDefinition) => graph.edges.some(edge => edge.kind === 'Transition') ? '子状态机' : '子图';

// ── Timeline 片段与轨道 ──

function ClipInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { timelines: shownTimelines, routeFrames, seconds, frame, graphs, runId } = useWorkbench(useShallow(state => ({
    timelines: state.timelines, routeFrames: state.routeFrames, seconds: state.seconds, frame: state.frame, graphs: state.graphs,
    runId: state.runId,
  })));
  // 作者模式下未保存的片段时间同样显示在检查器里。
  const { edited, editable } = useAuthoring(useShallow(state => ({ edited: state.edited, editable: state.author && !!state.skill })));
  const timelines = useMemo(() => applyTimelineEdits(shownTimelines, edited), [shownTimelines, edited]);
  const found = useMemo(() => findTimelineClip(timelines, id), [timelines, id]);
  const segments = useMemo(() => found ? playSegments(routeFrames).get(found.timeline.documentId) ?? [] : [], [found, routeFrames]);
  const marker = useMemo(() => found ? null : findTimelineMarker(timelines, id), [found, timelines, id]);
  if (!found) return marker ? <MarkerInspector found={marker} editable={editable} /> : missing;
  const { timeline, track, clip } = found;
  const kind = trackColors[clipKind(clip)];
  const graph = clipGraph(graphs, clip.graphId);
  const instances = segments.map(segment => ({
    segment, start: segment.origin + clip.startSeconds, end: Math.min(segment.origin + clip.endSeconds, segment.end),
  })).filter(item => item.start <= item.segment.end);
  return (
    <>
      <Head kind={kind} name={clip.displayName ?? clip.contract} sub={`${timeline.displayName} · ${track.displayName}`} />
      <Fold title="片段" kind={kind} defaultOpen>
        <Link label="Timeline" text={timeline.displayName} kind="var(--track-anim)" tip="在 Timeline 面板进入"
          onOpen={() => openTimeline(timeline.documentId)} />
        {graph
          ? <Link label="节点图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
          : <Value label="节点图" mono={false} value={clip.graphId ? '本次运行没有这张图' : '无'} />}
        <Value label="合同" value={clip.contract} />
        {!editable && <Value label="起点" value={clip.startSeconds.toFixed(3)} />}
        {!editable && <Value label="终点" value={clip.endSeconds.toFixed(3)} />}
        <Value label="时长" value={(clip.endSeconds - clip.startSeconds).toFixed(3)} />
      </Fold>
      {editable && <ClipFields documentId={timeline.documentId} clipId={clip.id} />}
      {editable && (
        <div className={styles.actions}>
          <Button variant="ghost" tip="删除片段；不再被引用的片段图保存时删除"
            onClick={() => { authoringActions.deleteClips(timeline.documentId, [clip.id]); selectionActions.select(null); }}>删除片段</Button>
        </div>
      )}
      {runId && <Fold title="本次运行" kind="var(--value)" defaultOpen>
        {instances.length ? (
          <div className={styles.list}>
            {instances.map(item => {
              const active = seconds !== null && item.start <= seconds && seconds <= item.end;
              return (
                <div key={item.segment.index} className={`${styles.listRow} ${styles.listLink}`} role="button" tabIndex={0}
                  title="跳到这次播放的片段起点"
                  onClick={() => void transport.seekTime(item.start)}
                  onKeyDown={event => { if (event.key === 'Enter') void transport.seekTime(item.start); }}>
                  <span className={styles.listText}>第 {item.segment.index + 1} 次播放{active ? ' · 游标所在' : ''}</span>
                  <span className={styles.listValue} data-sampled={active || undefined}>{item.start.toFixed(3)} — {item.end.toFixed(3)} s</span>
                </div>
              );
            })}
          </div>
        ) : <div className={styles.note}>{frame === null ? '正在读取当前帧'
          : frame.route === null ? '档案播放记录尚未提供' : '本次运行没有播放到这个片段'}</div>}
      </Fold>}
      <HostRecords records={locatedRecords(frame, (documentId, elementId) => documentId === timeline.documentId && elementId === clip.id)} />
      <Identity extra={[
        { displayName: '片段', value: clip.id }, { displayName: 'Timeline', value: timeline.documentId },
        ...(clip.graphId ? [{ displayName: '节点图', value: clip.graphId }] : []),
        { displayName: 'Timeline 来源修订', value: timeline.contentRevision ?? '未保存或尚未读取来源修订' },
      ]} />
    </>
  );
}

// 标记：时刻与触发图；作者模式可改时刻、删除。
function MarkerInspector({ found, editable }: { found: NonNullable<ReturnType<typeof findTimelineMarker>>; editable: boolean }) {
  const { timeline, track, marker } = found;
  const graph = useWorkbench(state => clipGraph(state.graphs, marker.graphId));
  const tickRate = useAuthoring(state => state.tickRate);
  const step = tickRate ? 1 / tickRate : 1 / 60;
  const kind = 'var(--track-act)';
  return (
    <>
      <Head kind={kind} name={`${track.displayName} 标记`} sub={timeline.displayName} />
      <Fold title="标记" kind={kind} defaultOpen>
        <Link label="Timeline" text={timeline.displayName} kind="var(--track-anim)" tip="在 Timeline 面板进入" onOpen={() => openTimeline(timeline.documentId)} />
        {graph
          ? <Link label="触发图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
          : <Value label="触发图" mono={false} value="未保存，保存后可进入" />}
        {editable ? (
          <div className={styles.field}>
            <label>时刻（秒）</label>
            <NumberInput wide label="时刻" value={marker.seconds} min={0} step={step}
              onChange={next => { if (next !== null) authoringActions.setMarkerTime(timeline.documentId, marker.id, Math.round(next / step) * step); }} />
          </div>
        ) : <Value label="时刻" value={`${marker.seconds.toFixed(3)} s`} />}
      </Fold>
      {editable && (
        <div className={styles.actions}>
          <Button variant="ghost" tip="删除标记；不再被引用的触发图保存时删除"
            onClick={() => { authoringActions.deleteClips(timeline.documentId, [marker.id]); selectionActions.select(null); }}>删除标记</Button>
        </div>
      )}
      <Identity extra={[{ displayName: '标记', value: marker.id }, { displayName: 'Timeline', value: timeline.documentId }]} />
    </>
  );
}

function TrackInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { timelines: shownTimelines, frame } = useWorkbench(useShallow(state => ({ timelines: state.timelines, frame: state.frame })));
  const { edited, editable } = useAuthoring(useShallow(state => ({ edited: state.edited, editable: state.author && !!state.skill })));
  const timelines = useMemo(() => applyTimelineEdits(shownTimelines, edited), [shownTimelines, edited]);
  const found = useMemo(() => findTimelineTrack(timelines, id), [timelines, id]);
  if (!found) return missing;
  const { timeline, track } = found;
  const kind = trackColors[trackKind(track)];
  return (
    <>
      <Head kind={kind} name={track.displayName} sub={timeline.displayName} />
      <Fold title="轨道" kind={kind} defaultOpen>
        <Value label="静音" mono={false} value={track.muted ? '是' : '否'} />
        <Value label="片段数" value={String(track.clips.length)} />
        <Value label="标记数" value={String(track.markers.length)} />
      </Fold>
      {editable && <TrackFields documentId={timeline.documentId} trackId={track.id} />}
      {editable && (
        <div className={styles.actions}>
          <Button variant="ghost" tip="删除轨道及其片段与标记；不再被引用的图保存时删除"
            onClick={() => { authoringActions.deleteTracks(timeline.documentId, [track.id]); selectionActions.select(null); }}>删除轨道</Button>
        </div>
      )}
      <Fold title="片段" kind={kind} defaultOpen>
        <div className={styles.list}>
          {track.clips.map(clip => (
            <div key={clip.id} className={styles.listRow}>
              <span className={styles.listText}>{clip.displayName ?? clip.contract}</span>
              <span className={styles.listValue}>{clip.startSeconds.toFixed(3)} — {clip.endSeconds.toFixed(3)}</span>
            </div>
          ))}
        </div>
      </Fold>
      <HostRecords records={locatedRecords(frame, (documentId, elementId) => documentId === timeline.documentId && elementId === track.id)} />
      <Identity extra={[{ displayName: '轨道', value: track.id }, { displayName: 'Timeline', value: timeline.documentId }]} />
    </>
  );
}

// ── 预览输入 ──

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
  const seconds = useWorkbench(state => state.seconds);
  const graphs = useWorkbench(state => state.graphs);
  const clip = found?.track.clips.find(item => item.id === itemId);
  const marker = found?.track.markers.find(item => item.id === itemId);
  if (!found || (!clip && !marker)) return missing;
  const writes = (clip ?? marker)!.writes;
  const kind = 'var(--track-in)';
  const graph = clipGraph(graphs, (clip ?? marker)!.graphId);
  const at = clip ? clip.startSeconds : marker!.seconds;
  return (
    <>
      <Head kind={kind} name={[...new Set(writes.map(write => write.keyId))].join(' / ') || '输入'} sub={`${found.root.displayName} · ${found.track.displayName}`} />
      <Fold title={clip ? '按住段' : '输入标记'} kind={kind} defaultOpen>
        {graph
          ? <Link label="节点图" text={graphName(graph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(graph)} />
          : <Value label="节点图" mono={false} value="本次运行没有这张图" />}
        <Link label="跳到" text={`${at.toFixed(3)} s`} kind={kind} tip="游标跳到这里"
          onOpen={() => void transport.seekTime(at)} />
        {clip ? (
          <>
            <Value label="开始" value={`${clip.startSeconds.toFixed(3)} s`} />
            <Value label="结束" value={`${clip.endSeconds.toFixed(3)} s`} />
            <Value label="游标所在" mono={false} value={seconds !== null && clip.startSeconds <= seconds && seconds <= clip.endSeconds ? '是' : '否'} />
          </>
        ) : <Value label="时刻" value={`${marker!.seconds.toFixed(3)} s`} />}
      </Fold>
      <Fold title="写入" kind="var(--port-enum)" defaultOpen>
        <div className={styles.list}>
          {writes.map((write, index) => (
            <div key={index} className={styles.listRow} title={write.fields.map(field => `${field.displayName}=${field.value}`).join('\n')}>
              <span className={styles.listText}>{write.keyId} · {write.fields.map(field => `${field.displayName}=${field.value}`).join(' · ')}</span>
              <span className={styles.listValue}>{write.seconds.toFixed(3)} s</span>
            </div>
          ))}
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
  const kind = 'var(--track-in)';
  return (
    <>
      <Head kind={kind} name={found.track.displayName} sub={found.root.displayName} />
      <Fold title="输入轨" kind={kind} defaultOpen>
        <Value label="按住段" value={String(found.track.clips.length)} />
        <Value label="标记" value={String(found.track.markers.length)} />
        <Value label="角色" value={found.root.actorId} />
        <Value label="方案零点" value={`${found.root.timeOriginSeconds.toFixed(3)} s`} />
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
      <Head kind="var(--track-anim)" name={actor.displayName} />
      <Fold title="变换" kind="var(--track-anim)" defaultOpen>
        <Value label="位置" value={actor.root.position.map(value => value.toFixed(3)).join(', ')} />
        <Value label="旋转" value={actor.root.rotation.map(value => value.toFixed(3)).join(', ')} />
      </Fold>
      <Fold title="本帧" kind="var(--value)" defaultOpen><Values fields={actor.fields} /></Fold>
      <Fold title="原始记录" kind="var(--node-flow)"><Raw value={actor.rawJson} /></Fold>
      <Identity extra={[{ displayName: '角色', value: actor.id }]} />
    </>
  );
}
