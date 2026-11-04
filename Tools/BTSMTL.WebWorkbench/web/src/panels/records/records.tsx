import { useEffect, useMemo, useState } from 'react';
import type { Availability, ExecutionCall, Frame, LoopScope, RouteFrame } from '../../api/types';
import { callKey, callLabel, loopScopeKey, loopScopeLabel, nodeOccurrenceLabel, portSampleKey, portSampleLabel } from '../../state/graphModel';
import { queryService } from '../../state/query';
import { consoleActions } from '../../state/store';
import { Empty, Facts, Fold } from '../../ui/blocks';
import { Select } from '../../ui/controls';
import { VirtualList } from '../../ui/VirtualList';
import styles from './records.module.css';

// 帧记录、记录查询、运行比较共用的记录显示。

export function JsonLines({ text }: { text: string }) {
  const [result, setResult] = useState<{ text: string } & (
    { kind: 'ready'; lines: string[] } | { kind: 'unavailable'; reason: string }) | null>(null);
  useEffect(() => {
    let current = true;
    void queryService.formatJson(text).then(lines => {
      if (current) setResult(lines === null
        ? { text, kind: 'unavailable', reason: '原读取已取消，记录尚未格式化' } : { text, kind: 'ready', lines });
    }, error => {
      if (!current) return;
      consoleActions.error('records', error);
      setResult({ text, kind: 'unavailable', reason: '记录格式化失败，详见控制台' });
    });
    return () => { current = false; };
  }, [text]);
  if (result === null || result.text !== text) return <Empty text="格式化中" />;
  if (result.kind === 'unavailable') return <Empty text={result.reason} />;
  const { lines } = result;
  return (
    <VirtualList className={styles.json} count={lines.length} rowHeight={17} label="JSON"
      render={index => <code className={styles.jsonLine}>{lines[index]}</code>} />
  );
}

export type ObservationRecord = RouteFrame['nodes'][number] | RouteFrame['edges'][number] | RouteFrame['ports'][number];

export const observationRecordKey = (record: ObservationRecord) => JSON.stringify([
  record.call === null ? null : callKey(record.call), record.graphId,
  'portId' in record ? ['port', record.nodeId, record.direction, record.portId, portSampleKey(record)]
    : 'edgeId' in record ? ['edge', record.edgeId, record.sequence, record.sourceNodeId, record.generation,
      record.loopScope === null ? null : loopScopeKey(record.loopScope)]
      : ['node', record.nodeId, record.sequence, record.kind, record.generation,
        record.loopScope === null ? null : loopScopeKey(record.loopScope)],
]);

const observationScopes = (record: ObservationRecord): LoopScope[] =>
  'portId' in record ? record.loopScopes ?? [] : record.loopScope === null ? [] : [record.loopScope];

const observationRecordLabel = (record: ObservationRecord) => ('portId' in record
  ? `${record.nodeId} · ${record.direction}:${record.portId} · ${portSampleLabel(record)}`
  : 'edgeId' in record ? `${record.edgeId} · 记录 ${record.sequence} · 激活 ${record.generation ?? '未提供'} · Selected=${record.selected} · Passed=${record.passed}`
    : `${record.nodeId} · 记录 ${record.sequence} · ${nodeOccurrenceLabel(record)}`) +
  (record.call === null ? ' · 调用来源未记录' : ` · ${callLabel(record.call)}`) +
  observationScopes(record).map(scope => ` · ${loopScopeLabel(scope)}`).join('');

export function ObservationAvailability({ value, label }: { value: Availability | null; label: string }) {
  return <div className={styles.note} role="status">{label}：{value === null ? 'Unavailable · 来源未记录此项可用性'
    : `${value.channel} · Available=${value.available} · Complete=${value.complete}${value.reason ? ` · ${value.reason}` : ''}`}</div>;
}

function CallDetails({ call }: { call: ExecutionCall | null }) {
  if (call === null) return <div className={styles.note}>Unavailable：此项没有记录调用身份</div>;
  return <Fold title="实际调用身份">
    <Facts fields={[
      { displayName: '角色', value: call.actorId ?? '未关联角色' },
      { displayName: '对象句柄', value: call.objectHandle ?? '未记录' },
      { displayName: '对象槽', value: call.objectSlot === null ? '未记录' : String(call.objectSlot) },
      { displayName: '对象种类', value: call.objectKind ?? '未记录' },
      { displayName: '定义索引', value: call.definitionIndex === null ? '未记录' : String(call.definitionIndex) },
      { displayName: '程序', value: call.programId }, { displayName: '程序修订', value: call.programRevision },
      { displayName: '程序索引', value: call.programIndex === null ? '未记录' : String(call.programIndex) },
      { displayName: '动作', value: call.actionId }, { displayName: '执行代次', value: call.executionGeneration },
      { displayName: '调用路径', value: call.invocationPath }, { displayName: '调用代次', value: call.invocationGeneration },
      { displayName: '父调用路径', value: call.parentInvocationPath ?? '未记录' },
      { displayName: '父调用代次', value: call.parentInvocationGeneration ?? '未记录' },
      { displayName: '播放', value: call.playbackId },
    ]} />
    <ObservationAvailability label="对象来源" value={call.objectAvailability} />
  </Fold>;
}

export function ObservationDetails({ record }: { record: ObservationRecord }) {
  const scopes = observationScopes(record);
  return <>
    <Facts fields={[
      { displayName: '记录序号', value: record.sequence }, { displayName: '图', value: record.graphId },
      { displayName: '激活代次', value: record.generation ?? '未提供' },
      ...('portId' in record ? [
        { displayName: '节点', value: record.nodeId }, { displayName: '端口', value: `${record.direction}:${record.portId}` },
        { displayName: '观察序号', value: record.observationSequence ?? '未记录' },
        { displayName: '类型', value: record.valueType }, { displayName: '值', value: record.value },
        { displayName: 'SourceAvailable', value: String(record.sourceAvailable) }, { displayName: 'Reason', value: record.reason },
      ] : 'edgeId' in record ? [
        { displayName: '连线', value: record.edgeId }, { displayName: '来源节点', value: record.sourceNodeId ?? '未记录' },
        { displayName: 'Selected', value: String(record.selected) }, { displayName: 'Passed', value: String(record.passed) },
      ] : [
        { displayName: '节点', value: record.nodeId }, { displayName: '发生', value: record.kind },
        { displayName: '结果', value: record.result ?? '未记录' },
        { displayName: '记录迭代号', value: record.loopIteration === null ? '未记录' : String(record.loopIteration) },
        { displayName: 'StopCause', value: record.stopCause === null ? '未记录' : String(record.stopCause) },
        { displayName: 'StopMode', value: record.stopMode ?? '未记录' },
      ]),
    ]} />
    <ObservationAvailability label="激活来源" value={record.activationAvailability} />
    <ObservationAvailability label="循环来源" value={record.loopScopeAvailability} />
    {scopes.length > 0 && <Fold title={'portId' in record ? '循环范围（外 → 内）' : '记录的直接循环范围'} defaultOpen>
      <ol className={styles.scopes}>{scopes.map(scope => <li key={loopScopeKey(scope)}>
        <div>{loopScopeLabel(scope)}</div>
        <div className={styles.note}>{callLabel(scope.call)}</div>
        <ObservationAvailability label="循环激活来源" value={scope.activationAvailability} />
      </li>)}</ol>
    </Fold>}
    <CallDetails call={record.call} />
    {'portId' in record && <Fold title="原始端口值"><div className={styles.rawBox}><JsonLines text={record.rawJson} /></div></Fold>}
  </>;
}

export function ObservationRecords({ records }: { records: readonly ObservationRecord[] }) {
  const [scope, setScope] = useState<string | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const scopes = useMemo(() => {
    const result = new Map<string, LoopScope>();
    for (const record of records) for (const item of observationScopes(record)) result.set(loopScopeKey(item), item);
    return [...result].map(([value, item]) => ({ value, label: loopScopeLabel(item) }));
  }, [records]);
  const visible = useMemo(() => scope === null ? records
    : records.filter(record => observationScopes(record).some(item => loopScopeKey(item) === scope)), [records, scope]);
  const item = selected === null ? visible.length === 1 ? visible[0] : undefined
    : visible.find(record => observationRecordKey(record) === selected);
  return <div className={styles.observations}>
    {scopes.length > 0 && <Select label="循环范围" value={scope ?? 'all'}
      options={[{ value: 'all', label: '全部已记录范围' }, ...scopes]}
      onChange={value => { setScope(value === 'all' ? null : value); setSelected(null); }} />}
    <div className={styles.note}>{visible.length} 条实际记录 · 循环自身事件按其记录的父范围显示</div>
    {visible.length === 0 ? <Empty text={scope === null ? '当前来源没有对应记录' : '当前来源没有所选循环范围的记录'} /> : <>
      <VirtualList className={styles.observationList} count={visible.length} rowHeight={24} label="实际发生与端口样本"
        render={index => {
          const record = visible[index], key = observationRecordKey(record);
          return <button className={styles.item} aria-selected={item === record} title={observationRecordLabel(record)}
            onClick={() => setSelected(key)}>{observationRecordLabel(record)}</button>;
        }} />
      <div className={styles.observationDetail}>{item === undefined
        ? <Empty text={selected === null ? '选择一条实际记录查看详情' : '原记录已不在当前来源中，选择身份已保留'} />
        : <ObservationDetails record={item} />}</div>
    </>}
  </div>;
}

export function FrameSections({ frame }: { frame: Frame }) {
  return (
    <>
      {frame.source === null || frame.source.content === null
        ? <div className={styles.note} role="status">Unavailable：原帧未记录正式采用的内容来源</div>
        : <div className={styles.note}>实际内容：{frame.source.content.kind} · {frame.source.content.revision}</div>}
      {frame.availability.filter(item => !item.available || !item.complete).map(item =>
        <ObservationAvailability key={item.channel} label="帧通道" value={item} />)}
      {frame.route !== null && <Fold title="节点、分支与端口观察">
        {frame.route.availability.map(item => <ObservationAvailability key={item.channel} label="观察通道" value={item} />)}
        <ObservationRecords records={[...frame.route.nodes, ...frame.route.edges, ...frame.route.ports]} />
      </Fold>}
      {frame.sections.map((section, index) => (
        <Fold key={index} title={section.displayName} defaultOpen={index === 0}>
          {section.records.length ? section.records.map((record, recordIndex) => (
            <Fold key={recordIndex} title={record.displayName}>
              <Facts fields={record.fields} />
              <Fold title="原始记录"><JsonLines text={record.rawJson} /></Fold>
            </Fold>
          )) : <div className={styles.note}>{section.emptyMessage}</div>}
        </Fold>
      ))}
    </>
  );
}

export function FrameFull({ frame }: { frame: Frame }) {
  const contacts = frame.availability.find(item => item.channel === 'physics-contacts');
  return (
    <>
      <Fold title="本帧" defaultOpen>
        <Facts fields={[{ displayName: '秒数', value: frame.seconds.toFixed(3) }, ...frame.fields]} />
      </Fold>
      {frame.actors.map(actor => (
        <Fold key={actor.id} title={actor.displayName} defaultOpen>
          <Facts fields={[
            { displayName: '位置', value: actor.root.position.map(value => value.toFixed(3)).join(', ') },
            { displayName: '旋转', value: actor.root.rotation.map(value => value.toFixed(3)).join(', ') },
            ...actor.fields,
          ]} />
        </Fold>
      ))}
      <Fold title="几何">
        <Facts fields={[
          { displayName: '扫掠', value: String(frame.geometry.sweeps.length) },
          { displayName: '实际KCC接触', value: contacts?.available
            ? String(frame.geometry.contacts.filter(hit => hit.kind === 'kcc-contact').length)
            : contacts?.reason ?? '当前响应未声明实际KCC接触可用性' },
          { displayName: '查询候选', value: String(frame.geometry.contacts.filter(hit => hit.kind === 'query-candidate').length) },
          { displayName: '来源未记录', value: String(frame.geometry.contacts.filter(hit => hit.kind == null).length) },
        ]} />
        {frame.geometry.sweeps.map((query, index) => <Fold key={index} title={`扫掠 ${index + 1} · 对象槽 ${query.objectSlot}`}>
          <Facts fields={[
            ...(query.actorId === null ? [] : [{ displayName: '角色', value: query.actorId }]),
            { displayName: '执行对象句柄', value: query.executionObjectHandle },
            { displayName: '结算对象句柄', value: query.settlementObjectHandle },
          ]} />
        </Fold>)}
      </Fold>
      <FrameSections frame={frame} />
    </>
  );
}
