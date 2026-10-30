import { NumberField } from '@base-ui/react/number-field';
import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { runLabel, session, stageNames } from '../state/session';
import { contentActions, getState, inputRootKey, runActions, useWorkbench } from '../state/store';
import { transport } from '../state/transport';
import { Button, MenuButton, NumberInput, Select, Tip, type MenuEntry } from '../ui/controls';
import { ErrorLine } from '../ui/ErrorLine';
import { panelMenu } from './GroupActions';
import styles from './toolbar.module.css';

// 顶部工具栏（D7）：左侧内容、输入方案与运行；正中播放、暂停、下一帧和帧号；右侧布局与连接状态。
export function Toolbar() {
  return (
    <header className={styles.toolbar}>
      <div className={styles.left}>
        <ContentChoice />
        <InputChoice />
        <LocalInputs />
        <ArchiveInputChoice />
        <RunChoice />
        <RunMenu />
      </div>
      <div className={styles.center}>
        <Transport />
        <FrameField />
      </div>
      <div className={styles.right}>
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
  const { choices, source } = useWorkbench(useShallow(state => ({ choices: state.sources?.replays, source: state.inputSource })));
  return <Select label="输入来源" placeholder="选择输入来源" value={source?.kind === 'local' ? 'local' : source?.id ?? null}
    options={[{ value: 'local', label: '本地输入方案' }, ...(choices ?? []).map(choice => ({ value: choice.id, label: choice.displayName }))]}
    onChange={id => contentActions.selectInputSource(id === 'local' ? { kind: 'local' } : { kind: 'recorded', id })} />;
}

function LocalInputs() {
  const local = useWorkbench(state => state.inputSource?.kind === 'local');
  return local ? <LocalInputChoices /> : null;
}

function LocalInputChoices() {
  const { scene, catalog, choices, lastSdkFrame } = useWorkbench(useShallow(state => ({
    scene: state.sources?.scenes.find(item => item.path === state.scenePath), catalog: state.inputCatalog,
    choices: state.inputChoices, lastSdkFrame: state.localLastSdkFrame,
  })));
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<number | null>(null);
  useEffect(() => {
    if (!scene) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    void session.loadInputs(controller.signal).then(entryId => {
      if (controller.signal.aborted) return;
      setError(entryId);
      setLoading(false);
    });
    return () => controller.abort();
  }, [scene?.path, scene?.revision]);
  if (!scene) return <Button disabled tip="先选择内容">本地输入</Button>;
  if (error !== null) return <ErrorLine text="输入方案读取失败" entryId={error} />;
  if (!catalog) return <Button disabled tip={loading ? '正在读取当前场景的角色与正式输入方案' : '尚未取得输入方案'}>读取输入</Button>;
  return <>
    {catalog.players.map(player => {
      const selected = choices.find(choice => choice.actorId === player.actorId);
      const options = player.localPlans.flatMap(plan => plan.schemes.map(scheme => ({
        value: JSON.stringify([plan.path, scheme.index]), label: `${plan.name} / ${scheme.name}`,
        choice: { actorId: player.actorId, inputPlanRoot: plan.path, inputPlanRevision: plan.revision, schemeIndex: scheme.index },
        duration: scheme.rangeEndSeconds,
      })));
      const current = selected ? options.find(option => option.choice.inputPlanRoot === selected.inputPlanRoot &&
        option.choice.schemeIndex === selected.schemeIndex) : null;
      return <Select key={player.actorId} label={`${player.actorId} 输入方案`} placeholder={`${player.actorId} 输入方案`}
        value={current?.value ?? null} options={options} disabled={!options.length}
        tip={current ? `${player.actorId} · 方案范围 ${current.duration} 秒；${catalog.localPlanRangeBasis}`
          : options.length ? player.actorId : `${player.actorId} 没有可用方案`}
        onChange={value => contentActions.selectInputPlan(options.find(option => option.value === value)!.choice)} />;
    })}
    <Tip content={catalog.localPlanRangeBasis}><span>结束 SDK</span></Tip>
    <NumberInput label="结束 SDK 帧" value={lastSdkFrame} min={1} step={1} onChange={contentActions.setLocalLastSdkFrame} />
  </>;
}

function ArchiveInputChoice() {
  const { roots, inputRoot, runId } = useWorkbench(useShallow(state => ({
    roots: state.inputTimeline?.roots, inputRoot: state.inputRoot, runId: state.runId,
  })));
  if (!runId || !roots?.length) return null;
  const options = (roots ?? []).map(root => ({ value: inputRootKey(root), label: `输入方案：${root.displayName}` }));
  return (
    <Select label="输入方案" placeholder="输入方案" value={inputRoot} options={options}
      disabled={options.length < 2} tip={!roots ? '读取中' : !options.length ? '本次运行没有预览输入' : undefined}
      onChange={key => runActions.selectInputRoot(key)} />
  );
}

function RunChoice() {
  const { jobs, runId } = useWorkbench(useShallow(state => ({ jobs: state.archiveJobs, runId: state.runId })));
  const options = jobs.map(job => ({ value: job.jobId, label: runLabel(job) }));
  return (
    <Select label="运行" placeholder="选择运行" value={runId} disabled={!options.length}
      tip={options.length ? undefined : '还没有运行'}
      options={options}
      onChange={id => session.selectRun(id)} />
  );
}

function RunMenu() {
  useWorkbench(useShallow(state => ({ liveId: state.liveId, sources: state.sources,
    scenePath: state.scenePath, inputSource: state.inputSource, inputCatalog: state.inputCatalog,
    inputChoices: state.inputChoices, localLastSdkFrame: state.localLastSdkFrame,
    hostAvailable: state.hostAvailable, playerViewport: state.playerViewport })));
  const startBlocked = session.canStart();
  const entries: MenuEntry[] = [
    { label: '新建运行', onSelect: () => void session.startRun(), disabled: !!startBlocked, tip: startBlocked },
  ];
  entries.push({ kind: 'separator' }, { label: '刷新列表', onSelect: () => void session.refresh() });
  return <MenuButton label="运行菜单" entries={entries} caret>运行</MenuButton>;
}

// 播放控制可用性：主工具栏与 Timeline 工具条共用，不可用时给出原因。
export function useTransportState() {
  return useWorkbench(useShallow(state => {
    const source = transport.source();
    const count = state.summary?.frameCount ?? 0;
    const playing = source ? transport.isPlaying() : false;
    let blocked: string | null = null;
    if (!source) blocked = state.runId ? (state.receipt ? stageNames[state.receipt.stage] : '读取中') : '选择一个运行';
    else if (source !== 'player-live' && !count) blocked = '还没有确认帧';
    let previous = blocked, next = blocked;
    if (!blocked) {
      if (playing) previous = next = '需要先暂停';
      else if (source === 'player-live') {
        previous = '试玩不能后退';
        next = state.live?.player?.state === 'paused' ? null : '需要先暂停';
      } else {
        if (state.frameIndex <= 0) previous = '已是第一帧';
        if (state.frameIndex >= count - 1)
          next = '已是最后一帧';
      }
    }
    return { playing, blocked, previous, next };
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

export function FrameField({ showSeconds = true }: { showSeconds?: boolean }) {
  const view = useWorkbench(useShallow(state => {
    if (transport.source() === 'player-live') {
      const player = state.live?.player;
      return player ? { frame: Number(player.hostFrame), last: Number(player.lastFrame ?? player.hostFrame), seconds: null } : null;
    }
    if (!state.summary?.frameCount) return null;
    return {
      frame: state.frameIndex,
      last: state.summary.frameCount - 1,
      seconds: state.seconds,
    };
  }));
  if (!view) return null;
  const commit = (value: number | null) => {
    if (value === null) return;
    if (transport.source() === 'player-live') { transport.seek(value); return; }
    transport.seek(value);
  };
  return (
    <div className={styles.frame}>
      <NumberField.Root value={view.frame} onValueCommitted={commit} step={1}>
        <NumberField.Input className={styles.frameInput} aria-label={transport.source() === 'player-live' ? 'SDK 帧' : '记录序号'} />
      </NumberField.Root>
      <span>/ {view.last} {transport.source() === 'player-live' ? '帧' : '记录'}</span>
      {showSeconds && view.seconds !== null && <span className={styles.seconds}>{view.seconds.toFixed(3)} s</span>}
    </div>
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
