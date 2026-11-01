import { useEffect, useState } from 'react';
import type { Frame } from '../../api/types';
import { queryService } from '../../state/query';
import { Empty, Facts, Fold } from '../../ui/blocks';
import { VirtualList } from '../../ui/VirtualList';
import styles from './records.module.css';

// 帧记录、记录查询、运行比较共用的记录显示。

export function JsonLines({ text }: { text: string }) {
  const [lines, setLines] = useState<string[] | null>(null);
  useEffect(() => {
    let current = true;
    setLines(null);
    void queryService.formatJson(text).then(result => { if (current) setLines(result); });
    return () => { current = false; };
  }, [text]);
  if (!lines) return <Empty text="格式化中" />;
  return (
    <VirtualList className={styles.json} count={lines.length} rowHeight={17} label="JSON"
      render={index => <code className={styles.jsonLine}>{lines[index]}</code>} />
  );
}

export function FrameSections({ frame }: { frame: Frame }) {
  return (
    <>
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
