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
        <Fold key={index} title={section.displayName} kind="var(--node-flow)" defaultOpen={index === 0}>
          {section.records.length ? section.records.map((record, recordIndex) => (
            <Fold key={recordIndex} title={record.displayName}><Facts fields={record.fields} /></Fold>
          )) : <div className={styles.note}>{section.emptyMessage}</div>}
        </Fold>
      ))}
    </>
  );
}

export function FrameFull({ frame }: { frame: Frame }) {
  return (
    <>
      <Fold title="本帧" kind="var(--cursor)" defaultOpen>
        <Facts fields={[{ displayName: '秒数', value: frame.seconds.toFixed(3) }, ...frame.fields]} />
      </Fold>
      {frame.actors.map(actor => (
        <Fold key={actor.id} title={actor.displayName} kind="var(--track-anim)" defaultOpen>
          <Facts fields={[
            { displayName: '位置', value: actor.root.position.map(value => value.toFixed(3)).join(', ') },
            { displayName: '旋转', value: actor.root.rotation.map(value => value.toFixed(3)).join(', ') },
            ...actor.fields,
          ]} />
        </Fold>
      ))}
      <Fold title="几何" kind="var(--logic-sweep-end)">
        <Facts fields={[
          { displayName: '扫掠', value: String(frame.geometry.sweeps.length) },
          { displayName: '接触', value: String(frame.geometry.contacts.length) },
        ]} />
      </Fold>
      <FrameSections frame={frame} />
    </>
  );
}
