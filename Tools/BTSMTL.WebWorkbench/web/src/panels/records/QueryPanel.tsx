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

// 记录查询面板：按范围分页读取或跟随帧流；解析在后台线程，结果用虚拟列表。
// 面板关闭时取消查询，隐藏时保持（D12）。

export function QueryPanel() {
  const { runId, summary, host } = useWorkbench(useShallow(state => ({ runId: state.runId, summary: state.summary, host: state.host })));
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
  const frameName = summary.frameDisplayName || host?.frameDisplayName || '帧';
  const range = (): ObservationQuery | null => {
    if (!form.firstFrame?.trim() || !form.lastFrame?.trim()) return null;
    const value: ObservationQuery = { firstFrame: form.firstFrame.trim(), lastFrame: form.lastFrame.trim() };
    for (const filter of host?.filters ?? []) {
      const text = form[filter.name]?.trim();
      if (text) (value as unknown as Record<string, string>)[filter.name] = text;
    }
    return value;
  };
  const busy = result.reading || result.streaming;
  const invalid = range() === null ? '填写起止帧' : null;
  const item = selected !== null ? result.items[selected] : null;
  const status = result.streaming ? `帧流 · 最近 ${result.items.length} 帧${result.lastEventId ? ` · 最后事件 ${result.lastEventId}` : ''}`
    : result.total !== null ? `${result.total} 个匹配帧 · 已读取 ${result.items.length}${result.ended ? ' · 帧流已结束' : ''}`
      : result.ended ? `帧流已结束 · ${result.items.length} 帧` : '';
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" disabled={!!invalid || result.reading} tip={invalid}
          onClick={() => { setSelected(null); void queryService.read(range()!, false); }}>读取记录</Button>
        <Button variant="ghost" disabled={!!invalid || result.streaming} tip={invalid ?? '只保留最近 512 帧'}
          onClick={() => { setSelected(null); queryService.follow(range()!, false); }}>跟随帧流</Button>
        <Button variant="ghost" disabled={!busy} onClick={() => queryService.stop()}>停止</Button>
        <BarSeparator />
        <Button variant="ghost" disabled={result.nextOffset === null || busy || !result.range}
          onClick={() => void queryService.read(result.range!, true)}>下一页</Button>
        {!result.streaming && result.lastEventId && !result.ended && (
          <Button variant="ghost" disabled={!result.range} onClick={() => queryService.follow(result.range!, true)}>续读帧流</Button>
        )}
      </PanelBar>
      <div className={styles.form}>
        <label>起始{frameName}<TextInput className={styles.input} value={form.firstFrame ?? ''}
          onChange={event => setForm({ ...form, firstFrame: event.currentTarget.value })} /></label>
        <label>结束{frameName}<TextInput className={styles.input} value={form.lastFrame ?? ''}
          onChange={event => setForm({ ...form, lastFrame: event.currentTarget.value })} /></label>
        {host?.filters.map(filter => (
          <label key={filter.name}>{filter.displayName}<TextInput className={styles.input} placeholder={filter.placeholder}
            type={filter.inputType} value={form[filter.name] ?? ''}
            onChange={event => setForm({ ...form, [filter.name]: event.currentTarget.value })} /></label>
        ))}
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
