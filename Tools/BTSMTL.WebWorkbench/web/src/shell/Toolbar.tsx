import { useShallow } from 'zustand/react/shallow';
import { runLabel, session, stageNames } from '../state/session';
import { contentActions, runActions, useWorkbench } from '../state/store';
import { transport } from '../state/transport';
import { Button, MenuButton, Select, Tip, type MenuEntry } from '../ui/controls';
import styles from './toolbar.module.css';

// 顶部工具栏（一行）：内容、运行来源、运行、播放控制、帧号与秒数、档位、连接。
export function Toolbar() {
  return (
    <header className={styles.toolbar}>
      <ContentChoice />
      <SourceChoice />
      <RunChoice />
      <RunMenu />
      <span className={styles.divider} />
      <Transport />
      <FrameLabel />
      <span className={styles.grow} />
      <Modes />
      <ConnectionDot />
    </header>
  );
}

function ContentChoice() {
  const { scenes, scenePath } = useWorkbench(useShallow(state => ({ scenes: state.sources?.scenes, scenePath: state.scenePath })));
  if (!scenes) return null;
  return (
    <Select label="内容" placeholder="选择内容" value={scenePath} disabled={!scenes.length}
      tip={scenes.length ? undefined : '宿主尚未提供正式内容'}
      options={scenes.map(scene => ({ value: scene.path, label: scene.path.split('/')[0] }))}
      onChange={path => contentActions.selectScene(path)} />
  );
}

function SourceChoice() {
  const { replays, replayId } = useWorkbench(useShallow(state => ({ replays: state.sources?.replays, replayId: state.replayId })));
  if (!replays) return null;
  return (
    <Select label="运行来源" placeholder="选择录像" value={replayId} disabled={!replays.length}
      tip={replays.length ? undefined : '宿主尚未提供原录像'}
      options={replays.map(replay => ({ value: replay.id, label: replay.displayName }))}
      onChange={id => { contentActions.setSourceKind('replay'); contentActions.selectReplay(id); }} />
  );
}

function RunChoice() {
  const { runs, runId } = useWorkbench(useShallow(state => ({ runs: state.runs, runId: state.runId })));
  return (
    <Select label="运行" placeholder="选择运行" value={runId} disabled={!runs.length}
      tip={runs.length ? undefined : '还没有运行'}
      options={runs.map(run => ({ value: run.jobId, label: runLabel(run) }))}
      onChange={id => session.selectRun(id)} />
  );
}

function RunMenu() {
  const state = useWorkbench(useShallow(state => ({
    interactive: state.interactive, followLive: state.followLive, receipt: state.receipt,
    sources: state.sources, scenePath: state.scenePath, replayId: state.replayId, mode: state.mode,
    hostAvailable: state.hostAvailable, sourceKind: state.sourceKind,
  })));
  const startBlocked = session.canStart();
  const running = state.receipt?.stage === 'accepted' || state.receipt?.stage === 'running';
  const entries: MenuEntry[] = [
    { label: '新建运行', onSelect: () => void session.startRun(), disabled: !!startBlocked, tip: startBlocked },
    { kind: 'check', label: '启动后暂停', checked: state.interactive, onChange: checked => contentActions.setInteractive(checked) },
  ];
  if (running) {
    entries.push({ kind: 'separator' });
    entries.push({ kind: 'check', label: '跟随实时帧', checked: state.followLive, onChange: checked => transport.setFollowLive(checked) });
    entries.push({ label: '取消运行', onSelect: () => transport.cancelRun() });
  }
  entries.push({ kind: 'separator' }, { label: '刷新列表', onSelect: () => void session.refresh() });
  return <MenuButton label="运行菜单" variant="icon" entries={entries}>⋯</MenuButton>;
}

function useTransportState() {
  return useWorkbench(useShallow(state => {
    const source = transport.source();
    const count = state.summary?.frameCount ?? 0;
    const off = state.mode === 'off';
    const playing = source ? transport.isPlaying() : false;
    let blocked: string | null = null;
    if (off) blocked = '预览已关闭';
    else if (!source) blocked = state.runId ? (state.receipt ? stageNames[state.receipt.stage] : '读取中') : '选择一个运行';
    else if (source !== 'player-live' && !count) blocked = '还没有确认帧';
    let previous = blocked, next = blocked;
    if (!blocked) {
      if (playing) previous = next = '需要先暂停';
      else if (source === 'player-live') {
        previous = '试玩不能后退';
        next = state.live?.player?.state === 'paused' ? null : '需要先暂停';
      } else {
        if (state.frameIndex <= 0) previous = '已是第一帧';
        const atLast = state.frameIndex >= count - 1;
        if (atLast && !(source === 'dotnet-session' && state.hostPlayback === 'paused')) next = '已是最后一帧';
      }
    }
    return { playing, blocked, previous, next };
  }));
}

function Transport() {
  const { playing, blocked, previous, next } = useTransportState();
  return (
    <>
      <Button variant="icon" aria-label={playing ? '暂停' : '播放'} tip={blocked ?? (playing ? '暂停（空格）' : '播放（空格）')}
        disabled={!!blocked} onClick={() => transport.toggle()}>{playing ? 'Ⅱ' : '▶'}</Button>
      <Button variant="icon" aria-label="上一帧" tip={previous ?? '上一帧（←）'} disabled={!!previous}
        onClick={() => transport.step(-1)}>‹</Button>
      <Button variant="icon" aria-label="下一帧" tip={next ?? '下一帧（→）'} disabled={!!next}
        onClick={() => transport.step(1)}>›</Button>
    </>
  );
}

function FrameLabel() {
  const label = useWorkbench(useShallow(state => {
    if (transport.source() === 'player-live') {
      const player = state.live?.player;
      return { text: player ? `${player.hostFrame} f` : null, tip: '播放器实际帧' };
    }
    if (state.hostFrame === null || !state.summary) return { text: null, tip: null };
    return {
      text: `${state.hostFrame} f · ${(state.seconds ?? 0).toFixed(3)} s`,
      tip: `第 ${state.frameIndex + 1} / ${state.summary.frameCount} 个确认帧`,
    };
  }));
  if (!label.text) return null;
  return <Tip content={label.tip}><output className={styles.frame}>{label.text}</output></Tip>;
}

function Modes() {
  const { modes, mode } = useWorkbench(useShallow(state => ({ modes: state.host?.modes, mode: state.mode })));
  const available = modes?.filter(item => item.available) ?? [];
  if (available.length < 2) return null;
  return (
    <div className={styles.segment} role="group" aria-label="预览档位">
      {available.map(item => (
        <Button key={item.id} variant="ghost" pressed={item.id === mode} tip={item.reason || undefined}
          onClick={() => { if (item.id === 'off') transport.pause(); runActions.setMode(item.id); }}>{item.displayName}</Button>
      ))}
    </div>
  );
}

function ConnectionDot() {
  const { hostAvailable, connected, lastError, host } = useWorkbench(useShallow(state => ({
    hostAvailable: state.hostAvailable, connected: state.connected, lastError: state.lastError, host: state.host,
  })));
  const status = hostAvailable === null && !lastError ? 'pending' : connected && hostAvailable ? 'ok' : 'down';
  const tip = status === 'pending' ? '连接中' : status === 'ok' ? `已连接 · ${host?.displayName ?? ''}`
    : hostAvailable === false ? '未接入运行宿主' : `已断开 · ${lastError ?? ''}`;
  return (
    <Tip content={tip}>
      <span className={styles.dot} data-status={status} role="status" aria-label={tip} tabIndex={0} />
    </Tip>
  );
}
