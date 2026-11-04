import { useState } from 'react';
import type { ExecutionCall, GraphPort, NodeOccurrence, PortSample } from '../../api/types';
import { callKey, callLabel, loopScopeKey, loopScopeLabel, nodeOccurrenceLabel, portSampleKey, portSampleLabel } from '../../state/graphModel';
import { Fold } from '../../ui/blocks';
import { Select } from '../../ui/controls';
import { ObservationAvailability, ObservationDetails } from '../records/records';
import styles from './inspector.module.css';

function PortObservation({ port, samples, reason }: { port: GraphPort; samples: PortSample[]; reason: string }) {
  const [selected, setSelected] = useState<string | null>(null);
  const sample = selected === null ? samples.length === 1 ? samples[0] : undefined
    : samples.find(item => portSampleKey(item) === selected);
  return <div className={styles.field}>
    <div>{port.displayName} · {port.direction === 'input' ? '输入' : '输出'} · {samples.length} 个样本</div>
    {samples.length > 1 && <Select label={`${port.displayName}的实际样本`} value={selected}
      placeholder="选择实际样本" options={samples.map(item => ({ value: portSampleKey(item), label: portSampleLabel(item) }))}
      onChange={setSelected} />}
    {sample === undefined ? <div role="status" className={styles.note}>
      {samples.length > 1 ? '同帧有多次观察，请选择样本。' : reason}
    </div> : <ObservationDetails record={sample} />}
  </div>;
}

export function NodeObservation({ frame, call, events, samples, ports, notice }: {
  frame: string | null; call: ExecutionCall | null; events: NodeOccurrence[]; samples: PortSample[];
  ports: GraphPort[]; notice: string | null;
}) {
  const [activation, setActivation] = useState<string>('all');
  const [loop, setLoop] = useState<string>('all');
  const [occurrence, setOccurrence] = useState<string | null>(null);
  const generations = new Set([...events, ...samples].flatMap(item => item.generation === null ? [] : [item.generation]));
  const scopes = new Map(samples.flatMap(item => (item.loopScopes ?? []).map(scope => [loopScopeKey(scope), scope] as const)));
  for (const event of events) if (event.loopScope !== null) scopes.set(loopScopeKey(event.loopScope), event.loopScope);
  const shownEvents = events.filter(item => (activation === 'all' || item.generation === activation) &&
    (loop === 'all' || item.loopScope !== null && loopScopeKey(item.loopScope) === loop));
  const shownSamples = samples.filter(item => (activation === 'all' || item.generation === activation) &&
    (loop === 'all' || item.loopScopes?.some(scope => loopScopeKey(scope) === loop)));
  const event = occurrence === null ? null : shownEvents.find(item => item.sequence === occurrence) ?? null;
  const sourceKey = JSON.stringify([frame, call === null ? null : callKey(call), activation, loop]);
  return <>
    <Fold title="本帧发生记录" defaultOpen>
      {notice !== null && <div role="status" className={styles.note}>{notice}</div>}
      {call === null ? <div role="status" className={styles.note}>尚未选择实际调用，请在节点图中选择调用实例。</div> : <>
        <div className={styles.note}>{callLabel(call)}</div>
        <ObservationAvailability label="对象来源" value={call.objectAvailability} />
        <div className={styles.note}>对象类型：{call.objectKind ?? '未记录'} · 对象槽：{call.objectSlot ?? '未记录'}</div>
        <div className={styles.note}>定义索引：{call.definitionIndex ?? '未记录'}</div>
        <div className={styles.note}>程序索引：{call.programIndex ?? '未记录'}</div>
      </>}
      {generations.size > 0 && <Select label="节点激活" value={activation} onChange={setActivation}
        options={[{ value: 'all', label: '全部激活' }, ...[...generations].map(value => ({ value, label: `激活 ${value}` }))]} />}
      {scopes.size > 0 && <Select label="循环范围" value={loop} onChange={setLoop}
        options={[{ value: 'all', label: '全部循环范围' }, ...[...scopes].map(([value, scope]) => ({ value, label: loopScopeLabel(scope) }))]} />}
      {shownEvents.length > 0 ? <>
        <Select label="节点发生记录" value={occurrence} placeholder="选择发生记录"
          options={shownEvents.map(item => ({ value: item.sequence, label: `${item.sequence} · ${nodeOccurrenceLabel(item)}` }))}
          onChange={setOccurrence} />
        <div className={styles.list}>{shownEvents.map(item => <div key={item.sequence} className={styles.field}>
          <div className={styles.listRow}>
            <span className={styles.listText}>{nodeOccurrenceLabel(item)}</span>
            <span className={styles.listValue} data-failed={item.result === 'Failure' || undefined}>{item.result}</span>
          </div>
        </div>)}</div>
        {event !== null && <ObservationDetails record={event} />}
      </> : <div className={styles.note}>本帧没有匹配当前选择的节点发生记录。</div>}
    </Fold>
    {ports.length > 0 && <Fold title="端口取值" defaultOpen>
      {ports.map(port => <PortObservation key={`${sourceKey}:${port.direction}:${port.id}`} port={port}
        samples={shownSamples.filter(item => item.portId === port.id && item.direction === port.direction)}
        reason={notice ?? '当前调用、激活与循环范围在本帧没有此端口的实际样本。'} />)}
    </Fold>}
  </>;
}
