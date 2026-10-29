import { NumberField } from '@base-ui/react/number-field';
import { useShallow } from 'zustand/react/shallow';
import { runLabel, session, stageNames } from '../state/session';
import { contentActions, getState, inputRootKey, runActions, useWorkbench } from '../state/store';
import { transport } from '../state/transport';
import { Button, MenuButton, Select, Tip, type MenuEntry } from '../ui/controls';
import { panelMenu } from './GroupActions';
import styles from './toolbar.module.css';

// 顶部工具栏（D7）：左侧内容、输入方案与运行；正中播放、暂停、下一帧和帧号；右侧档位、布局与连接状态。
export function Toolbar() {
  return (
    <header className={styles.toolbar}>
      <div className={styles.left}>
        <ContentChoice />
        <InputChoice />
        <RunChoice />
        <RunMenu />
      </div>
      <div className={styles.center}>
        <Transport />
        <FrameField />
      </div>
      <div className={styles.right}>
        <Modes />
        <MenuButton label="布局" entries={panelMenu()} caret>Layout</MenuButton>
        <Connection />
      </div>
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

function InputChoice() {
  const { roots, inputRoot, runId } = useWorkbench(useShallow(state => ({
    roots: state.inputTimeline?.roots, inputRoot: state.inputRoot, runId: state.runId,
  })));
  if (!runId) return null;
  const options = (roots ?? []).map(root => ({ value: inputRootKey(root), label: `输入方案：${root.displayName}` }));
  return (
    <Select label="输入方案" placeholder="输入方案" value={inputRoot} options={options}
      disabled={options.length < 2} tip={!roots ? '读取中' : !options.length ? '本次运行没有预览输入' : undefined}
      onChange={key => runActions.selectInputRoot(key)} />
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
  return <MenuButton label="运行菜单" entries={entries} caret>运行</MenuButton>;
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
    let next = blocked;
    if (!blocked) {
      if (playing) next = '需要先暂停';
      else if (source === 'player-live') next = state.live?.player?.state === 'paused' ? null : '需要先暂停';
      else if (state.frameIndex >= count - 1 && !(source === 'dotnet-session' && state.hostPlayback === 'paused'))
        next = '已是最后一帧';
    }
    return { playing, blocked, next };
  }));
}

function Transport() {
  const { playing, blocked, next } = useTransportState();
  return (
    <div className={styles.play} role="group" aria-label="运行播放">
      <Button aria-label="播放" pressed={playing} className={styles.playButton} data-on={playing || undefined}
        tip={blocked ?? '播放（空格）'} disabled={!!blocked} onClick={() => playing ? transport.pause() : transport.play()}>
        <i className={styles.triangle} />
      </Button>
      <Button aria-label="暂停" className={styles.playButton} tip={blocked ?? (playing ? '暂停（空格）' : '没有在播放')}
        disabled={!!blocked || !playing} onClick={() => transport.pause()}>
        <span className={styles.bars}><i /><i /></span>
      </Button>
      <Button aria-label="下一帧" className={styles.playButton} tip={next ?? '下一帧（→）'} disabled={!!next}
        onClick={() => transport.step(1)}>
        <span className={styles.stepGlyph}><i className={styles.triangle} /><i /></span>
      </Button>
    </div>
  );
}

// 帧号框显示 SDK 帧；输入后换算成帧序号经 transport 跳转。
function FrameField() {
  const view = useWorkbench(useShallow(state => {
    if (transport.source() === 'player-live') {
      const player = state.live?.player;
      return player ? { frame: Number(player.hostFrame), last: Number(player.lastFrame ?? player.hostFrame), seconds: null } : null;
    }
    if (state.hostFrame === null || !state.routeFrames.length) return null;
    return {
      frame: Number(state.hostFrame),
      last: Number(state.routeFrames[state.routeFrames.length - 1].hostFrame),
      seconds: state.seconds,
    };
  }));
  if (!view) return null;
  const commit = (value: number | null) => {
    if (value === null) return;
    if (transport.source() === 'player-live') { transport.seek(value); return; }
    const frames = getState().routeFrames;
    let index = 0;
    while (index + 1 < frames.length && Number(frames[index + 1].hostFrame) <= value) index++;
    transport.seek(index);
  };
  return (
    <div className={styles.frame}>
      <NumberField.Root value={view.frame} onValueCommitted={commit} step={1}>
        <NumberField.Input className={styles.frameInput} aria-label="帧号" />
      </NumberField.Root>
      <span>/ {view.last} 帧</span>
      {view.seconds !== null && <span className={styles.seconds}>{view.seconds.toFixed(3)} s</span>}
    </div>
  );
}

function Modes() {
  const { modes, mode } = useWorkbench(useShallow(state => ({ modes: state.host?.modes, mode: state.mode })));
  const available = modes?.filter(item => item.available) ?? [];
  if (available.length < 2) return null;
  return (
    <Select label="预览档位" value={mode}
      options={available.map(item => ({ value: item.id, label: `预览档位：${item.displayName}` }))}
      onChange={id => { if (id === 'off') transport.pause(); runActions.setMode(id); }} />
  );
}

function Connection() {
  const { hostAvailable, connected, lastError, host } = useWorkbench(useShallow(state => ({
    hostAvailable: state.hostAvailable, connected: state.connected, lastError: state.lastError, host: state.host,
  })));
  const status = hostAvailable === null && !lastError ? 'pending' : connected && hostAvailable ? 'ok' : 'down';
  const text = status === 'pending' ? '宿主 连接中' : status === 'ok' ? '宿主 已连接'
    : hostAvailable === false ? '宿主 未接入' : '宿主 已断开';
  const tip = status === 'ok' ? host?.displayName : status === 'down' ? lastError ?? undefined : undefined;
  return (
    <Tip content={tip}>
      <span className={styles.connection} data-status={status} role="status" tabIndex={0}><i />{text}</span>
    </Tip>
  );
}
