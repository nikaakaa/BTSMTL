import { NumberField } from '@base-ui/react/number-field';
import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { clockPosition, type PlayerJobReceipt } from '../api/playerRecords';
import { authoringActions, authoringDocument, useAuthoring } from '../state/authoring';
import { runLabel, session, stageNames } from '../state/session';
import { contentActions, contentSelectionKey, inputRootKey, previewContentChoices, runActions, useWorkbench } from '../state/store';
import { transport } from '../state/transport';
import { quickBounds, quickPlan, quickPreview } from '../state/quickPreview';
import { Button, MenuButton, Select, TextInput, Tip, type MenuEntry } from '../ui/controls';
import { CrumbArrow, RedoIcon, UndoIcon } from '../ui/icons';
import { panelMenu } from './GroupActions';
import { openPanel } from './layout';
import { QuickCursor } from '../panels/timeline/QuickPreview';
import { SaveResult } from './SaveResult';
import styles from './toolbar.module.css';

// 顶部工具栏（D7，只有一行）：左侧内容与技能、运行；正中上一帧、播放／暂停、下一帧、帧号与秒数；
// 右侧预览档位、布局与连接圆点。面板不再各自放播放按钮。
export function Toolbar() {
  const quick = useWorkbench(state => state.previewMode === 'quick');
  return (
    <header className={styles.toolbar}>
      <div className={styles.left}>
        <ContentChoice />
        <SkillChoice />
        <AuthoringStatus />
        {quick ? <Button variant="ghost" tip="在 Timeline 面板选择根与输入方案并推导路线" onClick={() => openPanel('timeline')}>路线</Button> : <RunMenu />}
      </div>
      <div className={styles.center}>
        <Transport />{quick ? <QuickCursor /> : <FrameField />}
      </div>
      <div className={styles.right}>
        <Button variant="ghost" onClick={() => openPanel('export')}>导出</Button>
        <PreviewModeChoice />
        <MenuButton label="布局" variant="ghost" entries={panelMenu()} caret>布局</MenuButton>
        <Connection />
      </div>
    </header>
  );
}

// 场景包按生成时间倒序：菜单直接列最新场景包与 Timeline，旧场景包收在“显示历史场景包”后。
function ContentChoice() {
  const { sources, selection } = useWorkbench(useShallow(state => ({ sources: state.sources, selection: state.contentSelection })));
  const [history, setHistory] = useState(false);
  if (!sources) return null;
  const options = previewContentChoices(sources);
  const key = contentSelectionKey(selection);
  const current = options.find(option => option.value === key);
  const scenes = options.filter(option => option.selection.kind === 'scene');
  const radio = (option: typeof options[number], label = option.label): MenuEntry => ({
    kind: 'radio', label, checked: option.value === key, onSelect: () => contentActions.selectContent(option.selection),
  });
  const entries: MenuEntry[] = [
    ...(scenes.length ? [radio(scenes[0], `最新 · ${scenes[0].label}`)] : []),
    ...options.filter(option => option.selection.kind === 'timeline').map(option => radio(option)),
  ];
  if (scenes.length > 1) entries.push({ kind: 'separator' },
    { kind: 'check', label: `显示历史场景包（${scenes.length - 1}）`, checked: history, onChange: setHistory },
    ...(history ? scenes.slice(1).map(option => radio(option)) : []));
  return (
    <MenuButton label="内容" variant="ghost" caret entries={entries}>
      {current ? current.value === scenes[0]?.value ? `最新 · ${current.label}` : current.label : options.length ? '选择内容' : '宿主尚未提供正式内容'}
    </MenuButton>
  );
}

// 技能是节点图、Timeline 与检查器共同打开的内容，属于全局选择。
function SkillChoice() {
  const { skills, target, available } = useAuthoring(useShallow(state => ({
    skills: state.skills, target: state.target, available: state.available,
  })));
  useEffect(() => { if (skills === null) void authoringActions.listSkills(); }, [skills]);
  if (available === false) return null;
  return (
    <>
      <CrumbArrow className={styles.crumbSeparator} />
      {target?.kind === 'scenario' ? <span className={styles.crumbText}>{target.name}</span> : (
        <Select variant="ghost" label="技能" placeholder="选择技能" value={target?.name ?? null} disabled={!skills?.length}
          options={(skills ?? []).map(item => ({ value: item, label: item }))}
          onChange={value => void authoringActions.open({ kind: 'skill', name: value })} />
      )}
    </>
  );
}

function AuthoringStatus() {
  const state = useAuthoring(useShallow(current => {
    const document = authoringDocument(current);
    return { open: current.author && current.target !== null, skill: current.target?.kind === 'skill', edited: document.edited, removed: document.removed,
      externalChange: document.externalChange, saving: document.saving, lastSave: document.lastSave,
      undoDepth: document.undo.length, redoDepth: document.redo.length };
  }));
  useEffect(() => {
    if (!state.open) return;
    const timer = setInterval(() => void authoringActions.checkExternal(), 3000);
    return () => clearInterval(timer);
  }, [state.open]);
  if (!state.open) return null;
  const dirty = Object.keys(state.edited).length + state.removed.length;
  const lastSave = state.lastSave;
  return (
    <div className={styles.authoring}>
      <Button variant="ghost" aria-label="撤销" className={styles.iconButton} disabled={!state.undoDepth} tip="撤销（Ctrl+Z）"
        onClick={() => authoringActions.undo()}><UndoIcon /></Button>
      <Button variant="ghost" aria-label="重做" className={styles.iconButton} disabled={!state.redoDepth} tip="重做（Ctrl+Y／Ctrl+Shift+Z）"
        onClick={() => authoringActions.redo()}><RedoIcon /></Button>
      {state.externalChange && (
        <Tip content="磁盘来源在别处被修改；保存会拒绝旧修订，还原会丢弃网页修改">
          <span className={styles.warn} tabIndex={0}>外部已修改</span>
        </Tip>
      )}
      {dirty > 0 && (
        <>
          <Tip content={`${dirty} 个文件未保存${state.removed.length ? `（含 ${state.removed.length} 个待删除）` : ''}`}>
            <span className={styles.dirty} tabIndex={0}>未保存</span>
          </Tip>
          <Button variant="ghost" disabled={state.saving} tip={state.saving ? '正在保存，收到回执后可还原' : '丢弃网页中的全部未保存修改'}
            onClick={() => authoringActions.revert()}>还原</Button>
        </>
      )}
      {(dirty > 0 || state.skill) && <Button disabled={state.saving} tip="保存并应用当前载体配置（Ctrl+S）" onClick={() => void authoringActions.save()}>
        {state.saving ? '保存中' : '保存'}
      </Button>}
      {lastSave && (dirty === 0 || !lastSave.ok) && <SaveResult outcome={lastSave} />}
    </div>
  );
}

const shortTime = (job: PlayerJobReceipt) => new Date(job.acceptedAt).toLocaleString(undefined, {
  month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
});

// 运行：在看的运行、新建运行的输入来源、本次运行的输入方案和新建／取消合成一个菜单。
function RunMenu() {
  const { jobs, runId, receipt, inputSource, replays, localAvailable, roots, inputRoot } = useWorkbench(useShallow(state => ({
    jobs: state.archiveJobs, runId: state.runId, receipt: state.receipt, inputSource: state.inputSource,
    replays: state.sources?.replays, localAvailable: state.inputCatalog !== null, roots: state.inputTimeline?.roots, inputRoot: state.inputRoot,
  })));
  const startBlocked = useWorkbench(() => session.canStart());
  const current = jobs.find(job => job.jobId === runId);
  const cancellable = runId !== null && (receipt?.stage === 'queued' || receipt?.stage === 'running');
  const inputs: MenuEntry[] = [
    ...(localAvailable ? [{
      kind: 'radio' as const, label: '本地输入方案', checked: inputSource?.kind === 'local',
      onSelect: () => contentActions.selectInputSource({ kind: 'local' }),
    }] : []),
    ...(replays ?? []).map((replay): MenuEntry => ({
      kind: 'radio', label: replay.displayName, checked: inputSource?.kind === 'recorded' && inputSource.id === replay.id,
      onSelect: () => contentActions.selectInputSource({ kind: 'recorded', id: replay.id }),
    })),
  ];
  const entries: MenuEntry[] = [
    { kind: 'label', label: '运行' },
    ...(jobs.length ? jobs.map((job): MenuEntry => ({
      kind: 'radio', label: runLabel(job), checked: job.jobId === runId, onSelect: () => session.selectRun(job.jobId),
    })) : [{ label: '还没有运行', disabled: true, onSelect: () => {} }]),
    ...(inputs.length ? [{ kind: 'separator' as const }, { kind: 'label' as const, label: '输入来源' }, ...inputs] : []),
  ];
  if (runId && roots && roots.length > 1)
    entries.push({ kind: 'separator' }, { kind: 'label', label: '本次运行的输入方案' }, ...roots.map((root): MenuEntry => ({
      kind: 'radio', label: root.displayName, checked: inputRootKey(root) === inputRoot,
      onSelect: () => runActions.selectInputRoot(inputRootKey(root)),
    })));
  entries.push({ kind: 'separator' },
    { label: '新建运行', onSelect: () => void session.startRun(), disabled: !!startBlocked, tip: startBlocked });
  if (cancellable)
    entries.push({ label: '取消运行', onSelect: () => void session.cancelRun(runId), tip: '请求取消，最终状态以同一作业回执为准' });
  entries.push({ label: '刷新列表', onSelect: () => void session.refresh() });
  return (
    <MenuButton label="运行" variant="ghost" entries={entries} caret>
      <span className={styles.dim}>运行</span>
      {current ? <span className={styles.runLabel}>{shortTime(current)} · {stageNames[receipt?.stage ?? current.stage]}</span> : <span className={styles.dim}>无</span>}
    </MenuButton>
  );
}

// 播放控制可用性：不可用时给出原因。
function useTransportState() {
  return useWorkbench(useShallow(state => {
    if (state.previewMode === 'quick') {
      const plan = quickPlan();
      const bounds = plan === null ? null : quickBounds(plan);
      const playing = state.quickPreview.playback !== null;
      const blocked = quickPreview.playbackReason();
      return { playing, blocked,
        previous: blocked ?? (playing ? '需要先暂停' : bounds !== null && state.quickPreview.seconds <= bounds.first ? '已是路线起点' : null),
        next: blocked ?? (playing ? '需要先暂停' : bounds !== null && state.quickPreview.seconds >= bounds.last ? '已是路线终点' : null) };
    }
    const source = transport.source();
    const count = state.summary?.frameCount ?? 0;
    const playing = source ? transport.isPlaying() : false;
    let blocked: string | null = null;
    if (!source) blocked = state.runId ? (state.receipt ? stageNames[state.receipt.stage] : '读取中') : '选择一个运行';
    else if (source !== 'player-live' && !count) blocked = '还没有确认帧';
    if (source === 'player-live') return {
      playing, blocked: transport.unavailableReason(playing ? 'pause' : 'play'),
      previous: transport.unavailableReason('previous'), next: transport.unavailableReason('next'),
    };
    let previous = blocked, next = blocked;
    if (!blocked) {
      if (playing) previous = next = '需要先暂停';
      else {
        if (state.frameIndex <= 0) previous = '已是第一帧';
        if (state.frameIndex >= count - 1)
          next = '已是最后一帧';
      }
    }
    return { playing, blocked, previous, next };
  }));
}

function Transport() {
  const { playing, blocked, previous, next } = useTransportState();
  const quick = useWorkbench(state => state.previewMode === 'quick');
  const controls = quick ? quickPreview : transport;
  return (
    <div className={styles.play} role="group" aria-label="运行播放">
      <Button aria-label="上一帧" className={styles.playButton} tip={previous ?? '上一帧（←）'} disabled={!!previous}
        onClick={() => controls.step(-1)}>
        <span className={styles.stepGlyph}><i /><i className={styles.triangleBack} /></span>
      </Button>
      <Button aria-label={playing ? '暂停' : '播放'} pressed={playing} className={styles.playButton} data-on={playing || undefined}
        tip={blocked ?? (playing ? '暂停（空格）' : '播放（空格）')} disabled={!!blocked} onClick={() => controls.toggle()}>
        {playing ? <span className={styles.bars}><i /><i /></span> : <i className={styles.triangle} />}
      </Button>
      <Button aria-label="下一帧" className={styles.playButton} tip={next ?? '下一帧（→）'} disabled={!!next}
        onClick={() => controls.step(1)}>
        <span className={styles.stepGlyph}><i className={styles.triangle} /><i /></span>
      </Button>
    </div>
  );
}

function FrameField() {
  const view = useWorkbench(useShallow(state => {
    const source = transport.source();
    if (source === 'player-live') {
      const player = state.live!.player!;
      const clock = player.source!.clock;
      const last = transport.action('last')?.target;
      return { kind: 'live' as const, loadCommandId: player.source!.loadCommandId,
        frame: clockPosition(clock), last: last?.hostFrame, seconds: clock.seconds,
        label: clock.positionDisplayName, blocked: transport.unavailableReason('seek') };
    }
    if (source !== 'sealed' || !state.summary?.frameCount) return null;
    return {
      kind: 'archive' as const,
      frame: state.frameIndex,
      first: 0,
      last: state.summary.frameCount - 1,
      seconds: state.seconds,
      label: '记录序号',
      blocked: null,
    };
  }));
  if (!view) return null;
  return (
    <Tip content={view.blocked ?? view.label}>
      <div className={styles.frame}>
        {view.kind === 'live' ? <LiveFrameField key={view.loadCommandId} value={view.frame} label={view.label} blocked={view.blocked} /> :
        <NumberField.Root value={view.frame} min={view.first} max={view.last}
          onValueCommitted={value => { if (value !== null) transport.seekRecord(value); }} step={1}>
          <NumberField.Input className={styles.frameInput} aria-label={view.label} />
        </NumberField.Root>}
        {view.last !== undefined && <span className={styles.frameLast}>/ {view.last}</span>}
        {view.seconds !== null && <span className={styles.seconds}>{view.seconds.toFixed(3)} s</span>}
      </div>
    </Tip>
  );
}

function LiveFrameField({ value, label, blocked }: { value: string; label: string; blocked: string | null }) {
  const [draft, setDraft] = useState<string | null>(null);
  const [issue, setIssue] = useState<string | null>(null);
  const text = draft ?? value;
  const commit = () => {
    if (!/^-?(0|[1-9]\d*)$/.test(text)) {
      setIssue('请输入十进制整数');
      return;
    }
    if (text !== value) transport.seekPosition({ hostFrame: text });
    setDraft(null);
    setIssue(null);
  };
  return <>
    <TextInput className={styles.frameInput} style={{ width: `${Math.max(4, text.length)}ch` }} aria-label={label}
      inputMode="numeric" value={text} disabled={blocked !== null} aria-invalid={issue !== null}
      onFocus={() => setDraft(value)} onChange={event => setDraft(event.currentTarget.value)} onBlur={commit}
      onKeyDown={event => {
        if (event.key === 'Enter') event.currentTarget.blur();
        if (event.key === 'Escape') { setDraft(null); setIssue(null); }
      }} />
    {issue && <span role="alert">{issue}</span>}
  </>;
}

function PreviewModeChoice() {
  const { modes, selected, observing, adoptedMode } = useWorkbench(useShallow(state => ({
    modes: state.host?.modes, selected: state.previewMode, observing: state.previewMode === 'quick'
      ? state.quickPreview.job.kind !== 'idle' : state.runId !== null || state.live?.player?.source != null,
    adoptedMode: state.previewMode === 'quick'
      ? 'reply' in state.quickPreview.job ? state.quickPreview.job.reply?.data?.adoptedMode ?? null : null
      : state.runId !== null
      ? state.sealed ? state.summary?.adoptedMode ?? null : state.receipt?.adoptedMode ?? null
      : state.live?.player?.source?.adoptedMode ?? null,
  })));
  if (!modes?.length && !observing) return null;
  const adoptedLabel = adoptedMode === null ? '未记录' : modes?.find(mode => mode.id === adoptedMode)?.displayName ?? adoptedMode;
  return (
    <div className={styles.modes} role="group" aria-label="预览档位">
      <Tip content="按宿主提供的可用档位提交请求；实际采用以宿主回执为准"><span className={styles.dim}>请求</span></Tip>
      {modes?.map(mode => (
        <Button key={mode.id} variant="ghost" pressed={selected === mode.id} tip={mode.reason} disabled={!mode.available}
          onClick={() => contentActions.selectPreviewMode(mode.id)}>{mode.displayName}</Button>
      ))}
      {observing && <Tip content={adoptedMode === null ? '该来源未记录实际采用档位；采用值未知不代表载入失败。' : `宿主记录的实际采用档位：${adoptedMode}`}>
        <span className={styles.dim} role="status">实际采用：{adoptedLabel}</span>
      </Tip>}
    </div>
  );
}

function Connection() {
  const { hostAvailable, connected, lastError, host } = useWorkbench(useShallow(state => ({
    hostAvailable: state.hostAvailable, connected: state.connected, lastError: state.lastError, host: state.host,
  })));
  const status = hostAvailable === null && !lastError ? 'pending' : connected && hostAvailable ? 'ok' : 'down';
  const text = status === 'pending' ? '宿主连接中' : status === 'ok' ? '宿主已连接'
    : hostAvailable === false ? '宿主未接入' : '宿主已断开';
  const detail = status === 'ok' ? host?.displayName : status === 'down' ? lastError : null;
  return (
    <Tip content={detail ? `${text}\n${detail}` : text}>
      <span className={styles.connection} data-status={status} role="status" aria-label={text} tabIndex={0}><i /></span>
    </Tip>
  );
}
