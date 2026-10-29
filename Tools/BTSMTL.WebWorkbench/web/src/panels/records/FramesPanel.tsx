import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Frame } from '../../api/types';
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
  pinned: number | null;
  tab: Tab;
}

export function FramesPanel() {
  const [params, setParams] = usePanelParams<Params>({ pinned: null, tab: 'records' });
  const { runId, frameIndex, current, count } = useWorkbench(useShallow(state => ({
    runId: state.runId, frameIndex: state.frameIndex, current: state.frame, count: state.summary?.frameCount ?? 0,
  })));
  const [pinnedFrame, setPinnedFrame] = useState<Frame | null>(null);
  const pinned = params.pinned !== null && params.pinned < count ? params.pinned : null;

  useEffect(() => {
    setPinnedFrame(null);
    if (pinned === null || !runId) return;
    let alive = true;
    readFrame(pinned).then(frame => { if (alive) setPinnedFrame(frame); }, error => consoleActions.error('frames', error));
    return () => { alive = false; };
  }, [pinned, runId]);

  const frame = pinned === null ? current : pinnedFrame;
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" pressed={pinned === null} disabled={!runId}
          tip={pinned === null ? '固定到当前帧' : '恢复跟随游标'}
          onClick={() => setParams({ pinned: pinned === null ? frameIndex : null })}>跟随游标</Button>
        <BarSeparator />
        {pinned !== null && (
          <>
            <BarLabel>帧序号</BarLabel>
            <NumberInput label="固定的帧序号" value={pinned} min={0} max={Math.max(0, count - 1)} step={1}
              onChange={value => { if (value !== null) setParams({ pinned: Math.trunc(value) }); }} />
          </>
        )}
        <BarLabel>{frame ? `${frame.hostFrame} · ${frame.seconds.toFixed(3)} s` : ''}</BarLabel>
      </PanelBar>
      <div className={styles.body}>
        {!runId ? <Empty text="选择一个运行" /> : !frame ? <Empty text={count ? '读取中' : '还没有确认帧'} /> : (
          <Tabs<Tab> value={params.tab} onChange={tab => setParams({ tab })} items={[
            { value: 'records', label: '帧记录', content: <FrameSections frame={frame} /> },
            { value: 'full', label: '完整记录', content: <FrameFull frame={frame} /> },
            { value: 'json', label: 'JSON', content: <JsonLines text={frame.rawJson} /> },
          ]} />
        )}
      </div>
    </div>
  );
}
