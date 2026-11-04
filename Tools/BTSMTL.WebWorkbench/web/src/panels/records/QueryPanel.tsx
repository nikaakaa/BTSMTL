import { useEffect, useState, useSyncExternalStore } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { ObservationQuery } from '../../api/types';
import type { PlayerJobId } from '../../api/playerRecords';
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
  const [selected, setSelected] = useState<{ runId: PlayerJobId; contentRevision: string; hostFrame: string } | null>(null);

  useEffect(() => () => queryService.stop(), []);
  useEffect(() => {
    const range = result.runId === runId ? result.range : null;
    setForm(summary ? { firstFrame: range?.firstFrame ?? summary.firstFrame, lastFrame: range?.lastFrame ?? summary.lastFrame } : {});
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
  const resultNotice = result.runId !== null && result.runId !== runId ? '查询结果属于另一运行，原结果已保留'
    : result.contentRevision !== null && result.contentRevision !== summary.contentRevision ? '查询结果与当前内容修订不同，原结果已保留' : null;
  const item = selected !== null && selected.runId === result.runId && selected.contentRevision === result.contentRevision
    ? result.items.find(frame => frame.hostFrame === selected.hostFrame) : undefined;
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
        <Button variant="ghost" disabled={resultNotice !== null || result.nextOffset === null || busy || !result.range} tip={resultNotice}
          onClick={() => void queryService.read(result.range!, true)}>下一页</Button>
        {!busy && result.nextOffset !== null && !result.ended && (
          <Button variant="ghost" disabled={resultNotice !== null || !result.range} tip={resultNotice}
            onClick={() => void queryService.readAll(result.range!, true)}>继续读取</Button>
        )}
      </PanelBar>
      <div className={styles.form}>
        <label>起始{frameName}<TextInput className={styles.input} value={form.firstFrame ?? ''}
          onChange={event => setForm({ ...form, firstFrame: event.currentTarget.value })} /></label>
        <label>结束{frameName}<TextInput className={styles.input} value={form.lastFrame ?? ''}
          onChange={event => setForm({ ...form, lastFrame: event.currentTarget.value })} /></label>
      </div>
      {status && <div className={styles.status}>{status}</div>}
      {resultNotice !== null ? <Empty text={resultNotice} /> : !result.items.length ? <Empty text={busy ? '读取中' : '没有结果'} /> : (
        <div className={styles.split}>
          <VirtualList className={styles.list} count={result.items.length} rowHeight={22} label="查询结果"
            render={index => {
              const frame = result.items[index];
              return (
                <button className={styles.item} aria-selected={item === frame} onClick={() => {
                  if (result.runId !== null && result.contentRevision !== null)
                    setSelected({ runId: result.runId, contentRevision: result.contentRevision, hostFrame: frame.hostFrame });
                }}>
                  <span>{frameName} {frame.hostFrame}</span>
                  <span>{frame.seconds.toFixed(3)} s · {frame.actors.length} 角色</span>
                </button>
              );
            }} />
          <div className={styles.scroll}>
            {item ? (
              <div key={JSON.stringify(selected)}>
                <FrameSections frame={item} />
                <Fold title="宿主记录">
                  <div className={styles.rawBox}><JsonLines text={item.rawJson} /></div>
                </Fold>
              </div>
            ) : <Empty text={selected === null ? '选择一帧' : '所选帧已不在当前查询页中，选择身份已保留'} />}
          </div>
        </div>
      )}
    </div>
  );
}
