import { useEffect, useState, useSyncExternalStore } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { ObservationQuery } from '../../api/types';
import { queryService } from '../../state/query';
import { useWorkbench } from '../../state/store';
import { BarSeparator, Empty, Fold, PanelBar } from '../../ui/blocks';
import { Button, TextInput } from '../../ui/controls';
import { VirtualList } from '../../ui/VirtualList';
import { FrameSections, JsonLines } from './records';
import styles from './records.module.css';

export function QueryPanel() {
  const { runId, summary } = useWorkbench(useShallow(state => ({ runId: state.runId, summary: state.summary })));
  const result = useSyncExternalStore(queryService.subscribe, queryService.snapshot);
  const [form, setForm] = useState<Record<string, string>>({});
  const [selected, setSelected] = useState<number | null>(null);

  useEffect(() => () => queryService.close(), []);
  useEffect(() => {
    setForm(summary ? { firstFrame: summary.firstFrame, lastFrame: summary.lastFrame } : {});
    setSelected(null);
  }, [summary?.runId]);

  if (!runId) return <Empty text="选择一个运行" />;
  if (!summary) return <Empty text="读取中" />;
  const frameName = summary.frameDisplayName;
  const range = (): ObservationQuery | null => {
    if (!form.firstFrame?.trim() || !form.lastFrame?.trim()) return null;
    return { firstFrame: form.firstFrame.trim(), lastFrame: form.lastFrame.trim() };
  };
  const busy = result.reading || result.readingAll;
  const invalid = range() === null ? '填写起止帧' : null;
  const item = selected !== null ? result.items[selected] : null;
  const status = result.total !== null
    ? `${result.total} 个匹配帧 · 当前显示 ${result.items.length}${result.ended ? ' · 已读完档案范围' : ''}` : '';
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" disabled={!!invalid || busy} tip={invalid}
          onClick={() => { setSelected(null); void queryService.read(range()!, false); }}>读取记录</Button>
        <Button variant="ghost" disabled={!!invalid || busy} tip={invalid ?? '分批读取已封存档案，保留最近 512 帧'}
          onClick={() => { setSelected(null); void queryService.readAll(range()!, false); }}>连续读取</Button>
        <Button variant="ghost" disabled={!busy} onClick={() => queryService.stop()}>停止</Button>
        <BarSeparator />
        <Button variant="ghost" disabled={result.nextOffset === null || busy || !result.range}
          onClick={() => void queryService.read(result.range!, true)}>下一页</Button>
        {!busy && result.nextOffset !== null && !result.ended && (
          <Button variant="ghost" disabled={!result.range} onClick={() => void queryService.readAll(result.range!, true)}>继续读取</Button>
        )}
      </PanelBar>
      <div className={styles.form}>
        <label>起始{frameName}<TextInput className={styles.input} value={form.firstFrame ?? ''}
          onChange={event => setForm({ ...form, firstFrame: event.currentTarget.value })} /></label>
        <label>结束{frameName}<TextInput className={styles.input} value={form.lastFrame ?? ''}
          onChange={event => setForm({ ...form, lastFrame: event.currentTarget.value })} /></label>
      </div>
      {status && <div className={styles.status}>{status}</div>}
      {!result.items.length ? <Empty text={busy ? '读取中' : '没有结果'} /> : (
        <div className={styles.split}>
          <VirtualList className={styles.list} count={result.items.length} rowHeight={22} label="查询结果"
            render={index => {
              const frame = result.items[index];
              return (
                <button className={styles.item} aria-selected={index === selected} onClick={() => setSelected(index)}>
                  <span>{frameName} {frame.hostFrame}</span>
                  <span>{frame.seconds.toFixed(3)} s · {frame.actors.length} 角色</span>
                </button>
              );
            }} />
          <div className={styles.scroll}>
            {item ? (
              <>
                <FrameSections frame={item} />
                <Fold title="宿主记录" kind="var(--node-flow)">
                  <div className={styles.rawBox}><JsonLines text={item.rawJson} /></div>
                </Fold>
              </>
            ) : <Empty text="选择一帧" />}
          </div>
        </div>
      )}
    </div>
  );
}
