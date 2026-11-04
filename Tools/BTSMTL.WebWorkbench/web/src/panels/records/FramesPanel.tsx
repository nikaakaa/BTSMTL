import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Frame } from '../../api/types';
import { samePreviewLoad, type PlayerJobId } from '../../api/playerRecords';
import { isAbort } from '../../api/http';
import { usePanelParams } from '../../shell/panel';
import { readFrame } from '../../state/frames';
import { consoleActions, useWorkbench } from '../../state/store';
import { BarLabel, BarSeparator, Empty, PanelBar, Tabs } from '../../ui/blocks';
import { Button, NumberInput } from '../../ui/controls';
import { FrameFull, FrameSections, JsonLines } from './records';
import styles from './records.module.css';

// 帧记录面板（承接原设置弹窗的“帧记录／完整记录／JSON”）：可多开，每个实例跟随游标或固定到某一帧（D3）。

type Tab = 'records' | 'full' | 'json';
interface Params {
  pinned: { runId: PlayerJobId; contentRevision: string; index: number; source: NonNullable<Frame['source']> } | null;
  tab: Tab;
}

export function FramesPanel() {
  const [params, setParams] = usePanelParams<Params>({ pinned: null, tab: 'records' });
  const { runId, generation, frameIndex, hostFrame, current, summary, frameUnavailableReason } = useWorkbench(useShallow(state => ({
    runId: state.runId, generation: state.generation, frameIndex: state.frameIndex, hostFrame: state.hostFrame,
    current: state.frame, summary: state.summary,
    frameUnavailableReason: state.routeAvailability.filter(item => !item.available || !item.complete).map(item => item.reason).join('；'),
  })));
  const [loaded, setLoaded] = useState<{ key: string } & (
    { kind: 'ready'; frame: Frame } | { kind: 'unavailable'; reason: string }) | null>(null);
  const pinned = params.pinned;
  const count = summary?.frameCount ?? 0;
  const pinNotice = pinned === null ? null : pinned.runId !== runId || pinned.contentRevision !== summary?.contentRevision
    ? '固定帧属于另一运行或内容修订，固定身份已保留'
    : pinned.index < 0 || pinned.index >= count ? '固定帧不在当前档案范围内，固定身份已保留' : null;
  const requestKey = JSON.stringify([generation, summary?.contentRevision, summary?.sceneRevision, summary?.execution, pinned]);

  useEffect(() => {
    if (pinned === null || pinNotice !== null) return;
    const controller = new AbortController();
    void readFrame(pinned.index, { kind: 'pinned', signal: controller.signal }).then(frame => {
      if (controller.signal.aborted) return;
      setLoaded(frame === null ? { key: requestKey, kind: 'unavailable', reason: '当前档案没有读到固定帧' }
        : frame.source === null || frame.source.content === null || !samePreviewLoad(pinned.source, frame.source)
          ? { key: requestKey, kind: 'unavailable', reason: '固定帧的实际执行或内容来源不同，固定身份已保留' }
          : { key: requestKey, kind: 'ready', frame });
    }, error => {
      if (controller.signal.aborted || isAbort(error)) return;
      consoleActions.error('frames', error);
      setLoaded({ key: requestKey, kind: 'unavailable', reason: '固定帧读取失败，详见控制台' });
    });
    return () => controller.abort();
  }, [pinned, pinNotice, requestKey]);

  const frame = pinned === null ? current?.hostFrame === hostFrame ? current : null
    : pinNotice === null && loaded?.key === requestKey && loaded.kind === 'ready' ? loaded.frame : null;
  const readNotice = pinNotice ?? (loaded?.key === requestKey && loaded.kind === 'unavailable' ? loaded.reason : null);
  const pinCandidate = runId === null || summary === null || frame === null || frame.source === null || frame.source.content === null
    ? null : { runId, contentRevision: summary.contentRevision, index: frameIndex, source: frame.source };
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" pressed={pinned === null} disabled={pinned === null && pinCandidate === null}
          tip={pinned !== null ? '恢复跟随游标' : pinCandidate !== null ? '固定到当前帧及其实际来源' : '当前帧尚无完整内容来源，不能固定'}
          onClick={() => setParams({ pinned: pinned === null ? pinCandidate : null })}>跟随游标</Button>
        <BarSeparator />
        {pinned !== null && pinNotice === null && (
          <>
            <BarLabel>帧序号</BarLabel>
            <NumberInput label="固定的帧序号" value={pinned.index} min={0} max={Math.max(0, count - 1)} step={1}
              onChange={value => { if (value !== null) setParams({ pinned: { ...pinned, index: Math.trunc(value) } }); }} />
          </>
        )}
        <BarLabel>{frame ? `${frame.hostFrame} · ${frame.seconds.toFixed(3)} s` : ''}</BarLabel>
      </PanelBar>
      <div className={styles.body}>
        {!runId ? <Empty text="选择一个运行" /> : readNotice !== null && pinned !== null ? <Empty text={readNotice} />
          : !frame ? <Empty text={frameUnavailableReason || (count ? '读取中' : '还没有确认帧')} /> : (
          <div className={styles.body} key={JSON.stringify([runId, summary?.contentRevision, frame.hostFrame])}>
          <Tabs<Tab> value={params.tab} onChange={tab => setParams({ tab })} items={[
            { value: 'records', label: '帧记录', content: <FrameSections frame={frame} /> },
            { value: 'full', label: '完整记录', content: <FrameFull frame={frame} /> },
            { value: 'json', label: 'JSON', content: <JsonLines text={frame.rawJson} /> },
          ]} />
          </div>
        )}
      </div>
    </div>
  );
}
