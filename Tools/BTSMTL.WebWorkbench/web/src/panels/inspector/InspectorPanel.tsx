import { useMemo, type ReactNode } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Availability, Field, Frame, GraphDefinition, HostRecord } from '../../api/types';
import { openPanel } from '../../shell/layout';
import { usePanelParams } from '../../shell/panel';
import {
  childGraphs, edgeChildGraphs, findGraphNode, graphHierarchy, graphKey, graphName, graphRecords, isNodeOccurrence,
  isPortSample, nodeName, playedTimeline, portColor,
} from '../../state/graphModel';
import { stageNames } from '../../state/session';
import { getState, selectionActions, useWorkbench, type Selection } from '../../state/store';
import {
  clipKind, findTimelineClip, findTimelineTrack, nearestFrame, playSegments, trackColors, trackKind,
} from '../../state/timelineModel';
import { transport } from '../../state/transport';
import { BarLabel, BarSeparator, Empty, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, ObjectField } from '../../ui/controls';
import styles from './inspector.module.css';

// 检查器（D7）：按 selection 显示所选对象在当前帧的记录；不选中时显示运行摘要。
// 锁定的实例固定显示锁定对象；身份、修订与哈希只放底部身份折叠。

interface Params {
  locked: Selection | null;
}

const kindNames: Record<Selection['kind'], string> = { node: '节点', edge: '连线', clip: '片段', track: '轨道', actor: '角色' };

export function InspectorPanel() {
  const [params, setParams] = usePanelParams<Params>({ locked: null });
  const { selection, runId } = useWorkbench(useShallow(state => ({ selection: state.selection, runId: state.runId })));
  const shown = params.locked ?? selection;
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" pressed={!!params.locked} disabled={!params.locked && !selection}
          tip={params.locked ? '解除锁定，跟随选中对象' : selection ? '锁定当前对象' : '没有选中对象'}
          onClick={() => setParams({ locked: params.locked ? null : selection })}>锁定</Button>
        <BarSeparator />
        <BarLabel>{shown ? kindNames[shown.kind] : '运行'}</BarLabel>
      </PanelBar>
      <div className={styles.scroll}>
        {!runId ? <Empty text="选择一个运行" /> : <Content shown={shown} locked={!!params.locked} />}
      </div>
    </div>
  );
}

function Content({ shown, locked }: { shown: Selection | null; locked: boolean }) {
  const missing = <Empty text={locked ? '锁定对象不在当前运行中' : '所选对象不在当前运行中'} />;
  if (!shown) return <RunSummary />;
  switch (shown.kind) {
    case 'node': return <NodeInspector id={shown.id} missing={missing} />;
    case 'clip': return shown.id.startsWith('input/') ? <InputItemInspector id={shown.id} missing={missing} />
      : <ClipInspector id={shown.id} missing={missing} />;
    case 'track': return shown.id.startsWith('input/') ? <InputTrackInspector id={shown.id} missing={missing} />
      : <TrackInspector id={shown.id} missing={missing} />;
    case 'actor': return <ActorInspector id={shown.id} missing={missing} />;
    case 'edge': return missing;
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
      <PlayerAlignment />
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

// 画面面板的逐帧对照明细。
function PlayerAlignment() {
  const { live, alignment, targetFrame, actualFrame, lastCommand } = useWorkbench(useShallow(state => ({
    live: state.live, alignment: state.alignment, targetFrame: state.targetFrame, actualFrame: state.actualFrame,
    lastCommand: state.lastCommand,
  })));
  if (!live) return null;
  return (
    <Fold title="画面对照" kind="var(--track-cam)" defaultOpen>
      <Value label="播放器进程" mono={false} value={live.state} />
      {live.player && <Values fields={live.player.fields} />}
      {alignment && (
        <>
          <Value label="结论" mono={false} value={alignment.state} />
          <Value label="已对照帧" value={String(alignment.comparedFrames)} />
          {alignment.firstMismatchFrame !== null && <Value label="首个不一致" value={String(alignment.firstMismatchFrame)} />}
          <Value label="播放器最新帧" value={String(alignment.lastPlayerFrame)} />
          {alignment.changeKind && <Value label="需要的更新" mono={false} value={alignment.changeKind} />}
          {alignment.reason && <Value label="说明" mono={false} value={alignment.reason} />}
        </>
      )}
      {targetFrame !== null && <Value label="目标帧 / 实际帧" value={`${targetFrame} / ${actualFrame ?? '—'}`} />}
      {lastCommand && <Value label="最后命令" value={`${lastCommand.type} · ${lastCommand.stage}${lastCommand.error ? ` · ${lastCommand.error}` : ''}`} />}
    </Fold>
  );
}

// ── 节点 ──

function NodeInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { graphs, frame } = useWorkbench(useShallow(state => ({ graphs: state.graphs, frame: state.frame })));
  const found = useMemo(() => findGraphNode(graphs, id), [graphs, id]);
  if (!found) return missing;
  const { graph, node } = found;
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

const childLabel = (graph: GraphDefinition) => graph.edges.some(edge => edge.kind === 'Transition') ? '子状态机' : '子图';

// ── Timeline 片段与轨道 ──

function ClipInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { timelines, routeFrames, seconds, frame, graphs } = useWorkbench(useShallow(state => ({
    timelines: state.timelines, routeFrames: state.routeFrames, seconds: state.seconds, frame: state.frame, graphs: state.graphs,
  })));
  const found = useMemo(() => findTimelineClip(timelines, id), [timelines, id]);
  const segments = useMemo(() => found ? playSegments(routeFrames).get(found.timeline.documentId) ?? [] : [], [found, routeFrames]);
  if (!found) return missing;
  const { timeline, track, clip } = found;
  const kind = trackColors[clipKind(clip)];
  const clipGraphId = clip.graphId?.slice(clip.graphId.indexOf(':') + 1);
  const clipGraph = clipGraphId ? graphs?.find(graph => graph.id === clipGraphId) ?? null : null;
  const instances = segments.map(segment => ({
    segment, start: segment.origin + clip.startSeconds, end: Math.min(segment.origin + clip.endSeconds, segment.end),
  })).filter(item => item.start <= item.segment.end);
  return (
    <>
      <Head kind={kind} name={clip.displayName ?? clip.contract} sub={`${timeline.displayName} · ${track.displayName}`} />
      <Fold title="片段" kind={kind} defaultOpen>
        <Link label="Timeline" text={timeline.displayName} kind="var(--track-anim)" tip="在 Timeline 面板进入"
          onOpen={() => openTimeline(timeline.documentId)} />
        {clipGraph
          ? <Link label="节点图" text={graphName(clipGraph)} kind="var(--node-flow)" tip="在节点图面板打开" onOpen={() => openGraph(clipGraph)} />
          : <Value label="节点图" mono={false} value={clip.graphId ? '本次运行没有这张图' : '无'} />}
        <Value label="合同" value={clip.contract} />
        <Value label="起点" value={clip.startSeconds.toFixed(3)} />
        <Value label="终点" value={clip.endSeconds.toFixed(3)} />
        <Value label="时长" value={(clip.endSeconds - clip.startSeconds).toFixed(3)} />
      </Fold>
      <Fold title="本次运行" kind="var(--value)" defaultOpen>
        {instances.length ? (
          <div className={styles.list}>
            {instances.map(item => {
              const active = seconds !== null && item.start <= seconds && seconds <= item.end;
              return (
                <div key={item.segment.index} className={`${styles.listRow} ${styles.listLink}`} role="button" tabIndex={0}
                  title="跳到这次播放的片段起点"
                  onClick={() => transport.seek(nearestFrame(getState().routeFrames, item.start))}
                  onKeyDown={event => { if (event.key === 'Enter') transport.seek(nearestFrame(getState().routeFrames, item.start)); }}>
                  <span className={styles.listText}>第 {item.segment.index + 1} 次播放{active ? ' · 游标所在' : ''}</span>
                  <span className={styles.listValue} data-sampled={active || undefined}>{item.start.toFixed(3)} — {item.end.toFixed(3)} s</span>
                </div>
              );
            })}
          </div>
        ) : <div className={styles.note}>本次运行没有播放到这个片段</div>}
      </Fold>
      <HostRecords records={locatedRecords(frame, (documentId, elementId) => documentId === timeline.documentId && elementId === clip.id)} />
      <Identity extra={[
        { displayName: '片段', value: clip.id }, { displayName: 'Timeline', value: timeline.documentId },
        ...(clip.graphId ? [{ displayName: '节点图', value: clip.graphId }] : []),
        { displayName: 'Timeline 修订', value: timeline.contentRevision },
      ]} />
    </>
  );
}

function TrackInspector({ id, missing }: { id: string; missing: ReactNode }) {
  const { timelines, frame } = useWorkbench(useShallow(state => ({ timelines: state.timelines, frame: state.frame })));
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
  const clip = found?.track.clips.find(item => item.id === itemId);
  const marker = found?.track.markers.find(item => item.id === itemId);
  if (!found || (!clip && !marker)) return missing;
  const writes = (clip ?? marker)!.writes;
  const kind = 'var(--track-in)';
  return (
    <>
      <Head kind={kind} name={[...new Set(writes.map(write => write.keyId))].join(' / ') || '输入'} sub={`${found.root.displayName} · ${found.track.displayName}`} />
      <Fold title={clip ? '按住段' : '输入标记'} kind={kind} defaultOpen>
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
