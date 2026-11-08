import { useCallback, useEffect, useRef, useState, useSyncExternalStore, type RefObject } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { Matrix4 } from 'three';
import type { CameraCommand, PlayerState, Vec3, VideoFrame } from '../../api/types';
import { clockPosition, positionValue, samePreviewLoad, type LivePlayer } from '../../api/playerRecords';
import type { PreviewQuickFrame } from '../../api/previewQuickRecords';
import { usePanelAnimationFrame, usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, reportRendered } from '../../shell/readiness';
import { playerService, sceneLoadContent } from '../../state/player';
import { useAuthoring, useDocumentObservationNotice } from '../../state/authoring';
import { session } from '../../state/session';
import { quickJobCancellable, quickJobFailure, quickJobProgress, quickPictureCurrent, quickPreview, type QuickPreviewState } from '../../state/quickPreview';
import { openPanel } from '../../shell/layout';
import { consoleActions, getState, selectedScene, selectionActions, useWorkbench, type ArchiveFrame, type ArchiveImage } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, type MenuEntry } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './player.module.css';
import { playerTools } from '../../extensions';

const playerStates: Record<PlayerState, string> = {
  idle: '空闲', loading: '载入中', paused: '已暂停', playing: '播放中', stepping: '单步中', seeking: '跳帧中', failed: '失败',
};

const processStates: Record<string, string> = { starting: '启动中', running: '运行中', exited: '已退出' };
const liveLabel = (live: LivePlayer) => `${processStates[live.state] ?? live.state} · ${new Date(live.startedAt).toLocaleTimeString()}`;

interface Params {
  overlay: boolean;
  stats: boolean;
  tools: string[];
}

export function PlayerPanel() {
  const hostAvailable = useWorkbench(state => state.hostAvailable);
  const mode = useWorkbench(state => state.previewMode);
  if (hostAvailable === false) return <Empty text="未接入运行宿主" />;
  if (mode === 'quick') return <QuickPreviewPicture />;
  return <PlayerView />;
}

function QuickPreviewPicture() {
  const state = useWorkbench(current => current.quickPreview);
  const pictureCurrent = useWorkbench(quickPictureCurrent);
  const job = state.job;
  const progress = quickJobProgress(job);
  const reason = state.description.kind === 'failed' || state.description.kind === 'stale' ? state.description.reason
    : quickPreview.sourceUnavailableReason();
  const accepted = pictureCurrent ? state.accepted : null;
  return <div className={styles.root}>
    <PanelBar wrap><strong>快速预览</strong>
      <Button onClick={() => openPanel('timeline')}>在 Timeline 看路线</Button>
      {quickJobCancellable(job) &&
        <Button onClick={() => void quickPreview.cancel()}>{job.kind === 'settled' ? '结束预览' : '取消预览'}</Button>}
    </PanelBar>
    {reason !== null && <Empty text={reason} />}
    {quickJobFailure(job) !== null && <Empty text={quickJobFailure(job)!} />}
    {state.description.kind === 'loading' && <Empty text="正在读取当前作者来源与输入方案" />}
    {progress !== null && <div role="status">{progress}</div>}
    {accepted === null ? reason === null && state.description.kind !== 'loading' && progress === null && quickJobFailure(job) === null &&
      <Empty text="快档按当前技能与输入方案自动推导路线；在 Timeline 输入轨编辑输入，拖动游标查看对应画面。" />
      : <QuickPreviewStage key={accepted.jobId} result={accepted} seconds={state.seconds} playback={state.playback} picture={state.picture} />}
  </div>;
}

function QuickPreviewStage({ result, seconds, playback, picture }: {
  result: NonNullable<QuickPreviewState['accepted']>; seconds: number; playback: QuickPreviewState['playback']; picture: QuickPreviewState['picture'];
}) {
  const video = useRef<HTMLCanvasElement>(null);
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  const [drawn, setDrawn] = useState<PreviewQuickFrame | null>(null);
  const [failure, setFailure] = useState<{ reason: string; entryId: number | null } | null>(null);
  const snapshot = picture.frame;
  const ownsTarget = snapshot !== null && (seconds === snapshot.value.requestedSeconds ||
    snapshot.playback !== null && playback === snapshot.playback);
  useEffect(() => {
    const canvas = video.current!;
    if (!visible || snapshot === null) {
      canvas.getContext('2d')!.clearRect(0, 0, canvas.width, canvas.height);
      setDrawn(null);
      setFailure(null);
      return;
    }
    if (!ownsTarget) return;
    let active = true;
    const frame = snapshot.value;
    const ownsFrame = () => {
      const state = getState().quickPreview;
      return active && state.picture.frame === snapshot && quickPictureCurrent() &&
        (state.seconds === frame.requestedSeconds || snapshot.playback !== null && state.playback === snapshot.playback);
    };
    const image = panelWindow.document.createElement('img');
    image.src = `data:${frame.mediaType};base64,${frame.imageBase64}`;
    void image.decode().then(() => {
      if (!ownsFrame()) return;
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d')!;
      context.save();
      try {
        if (frame.flipY) {
          context.translate(0, canvas.height);
          context.scale(1, -1);
        }
        context.drawImage(image, 0, 0);
      } finally {
        context.restore();
      }
      setDrawn(frame);
      setFailure(null);
    }).catch((error: unknown) => {
      if (!ownsFrame()) return;
      setFailure({ reason: `技能预览画面解码失败：${error instanceof Error ? error.message : String(error)}`,
        entryId: consoleActions.error('快预览画面', error) });
    });
    return () => { active = false; };
  }, [snapshot, ownsTarget, visible, panelWindow]);
  const request = picture.request;
  return <div className={styles.stage} data-quick-job-id={result.jobId} data-quick-target-seconds={seconds}
    data-quick-frame-request-id={drawn?.requestId} data-quick-sampled-seconds={drawn?.sampledSeconds}
    data-quick-sampled-revision={drawn?.sampledRevision} data-quick-source-revision={drawn?.sourceRevision}
    data-quick-input-revision={drawn?.inputRevision}>
    <canvas ref={video} className={styles.video} aria-label="技能快速预览画面" hidden={drawn === null} />
    {failure !== null ? <ErrorLine text={failure.reason} entryId={failure.entryId} />
      : request.kind === 'failed' ? <ErrorLine text={request.reason} entryId={null} />
        : drawn === null && <Empty text="正在读取并绘制当前技能画面" />}
    {(failure !== null || request.kind === 'failed') && <Button onClick={() => quickPreview.retryPicture()}>重试当前画面</Button>}
    <div className={styles.source} role="status">目标 {seconds.toFixed(3)} 秒 ·
      {drawn === null ? ' 尚未显示实际画面' : ` 画面 ${drawn.sampledSeconds.toFixed(3)} 秒 · 采样 ${drawn.sampledRevision}`}
      {request.kind === 'pending' && ' · 正在取帧'}</div>
  </div>;
}

function PlayerView() {
  const [params, setParams] = usePanelParams<Params>({ overlay: false, stats: false, tools: [] });
  const authoringSkill = useAuthoring(state => state.target?.kind === 'skill');
  const documentNotice = useDocumentObservationNotice();
  const state = useWorkbench(useShallow(current => ({
    builds: current.playerBuilds, lives: current.lives, liveId: current.liveId, live: current.live, viewerOpen: current.viewerOpen,
    runId: current.runId, archiveImage: current.archiveImage, contentSelection: current.contentSelection, sources: current.sources, selection: current.selection,
  })));
  const startBlocked = useWorkbench(() => session.canStartNewRun());
  const stage = useRef<HTMLDivElement>(null);
  const [captureBinding, setCaptureBinding] = useState<string | null>(null);
  const [codeUpdate, setCodeUpdate] = useState<{ kind: 'compiling' | 'ready' | 'adopting'; buildId: string } | null>(null);
  const selectedLiveId = state.liveId;
  const codeBusy = codeUpdate !== null && codeUpdate.kind !== 'ready';
  const [buildHistory, setBuildHistory] = useState(false);
  const updateCode = async (buildId: string) => {
    setCodeUpdate({ kind: 'compiling', buildId });
    const result = await playerService.updateCode(buildId);
    setCodeUpdate(result?.state === 'created' ? { kind: 'ready', buildId: result.buildId } : null);
  };
  const player = state.live?.player ?? null;
  const source = player?.source;
  const binding = JSON.stringify([state.runId, state.liveId, source?.loadCommandId ?? null, source?.execution ?? null,
    source?.content ?? null, source?.contentRevision ?? null, source?.sceneRevision ?? null, source?.inputSource ?? null, source?.inputRevision ?? null]);
  const captureEnabled = captureBinding === binding;
  const ready = !state.runId && state.viewerOpen && !!player && ['paused', 'playing', 'stepping', 'seeking'].includes(player.state);
  const orbit = ready && player?.cameraMode === 'orbit';

  useEffect(() => { setCaptureBinding(null); }, [binding, ready]);

  useEffect(() => {
    void playerService.refresh();
    playerService.resumePendingStart();
    const timer = setInterval(() => void playerService.refresh(), 3000);
    return () => clearInterval(timer);
  }, []);

  const scene = selectedScene(state);
  const contentSelection = state.contentSelection;
  const timeline = contentSelection?.kind === 'timeline'
    ? state.sources?.timelines.find(item => item.content.package === contentSelection.package) : undefined;
  const content = timeline?.content ?? (scene ? sceneLoadContent(scene) : null);
  const loading = player?.state === 'loading';
  const selectedLive = state.lives.find(item => item.liveId === state.liveId);
  const loadBlocked = !state.viewerOpen ? '尚未连接播放器画面' : !selectedLive || selectedLive.hello === null ? '正在读取播放器能力'
    : selectedLive.identity.liveProtocol !== 'btsmtl.preview-live/1' ? '所选播放器需要更新到支持当前预览协议的构建'
    : !content ? '先选择内容' : loading ? '正在载入' : null;
  const focusActor = state.selection?.kind === 'actor' ? state.selection.id : null;
  const authoringNotice = authoringSkill ? documentNotice : null;
  const overlayReason = authoringNotice ?? archiveOverlayReason(state.archiveImage);

  // 照 Game 视图的 Display 下拉：在用的播放器、启动与停止合成一个菜单。
  // 宿主按构建时间倒序给出构建；默认只列最新构建，历史构建按需展开。
  const builds = (state.builds ?? []).slice(0, buildHistory ? undefined : 1);
  const playerMenu: MenuEntry[] = [
    { kind: 'label', label: '播放器' },
    ...(state.lives.length ? state.lives.map((live): MenuEntry => ({
      kind: 'radio', label: liveLabel(live), checked: live.liveId === state.liveId, onSelect: () => playerService.attach(live.liveId),
    })) : [{ label: '尚未启动播放器', disabled: true, onSelect: () => {} }]),
    { kind: 'separator' },
    { kind: 'label', label: '启动常驻播放器' },
    ...(builds.length ? builds.map((build, index): MenuEntry => ({
      label: index === 0 ? `最新 · ${build.displayName}` : build.displayName,
      tip: build.fields.map(field => `${field.displayName} ${field.value}`).join('\n'),
      onSelect: () => void playerService.start(build.buildId, stage.current!.clientWidth),
    })) : [{ label: state.builds ? '画面来源尚未提供可用构建' : '正在读取构建', disabled: true, onSelect: () => {} }]),
  ];
  if (builds.length) playerMenu.push(
    { label: '更新最新构建的代码', disabled: codeBusy,
      tip: '使用最新构建的 Player 代码生成新版本，失败时保留当前版本。',
      onSelect: () => void updateCode(builds[0].buildId) },
  );
  if ((state.builds?.length ?? 0) > 1) playerMenu.push({ kind: 'check',
    label: `显示历史构建（${state.builds!.length - 1}）`, checked: buildHistory, onChange: setBuildHistory });
  if (state.liveId) playerMenu.push({ kind: 'separator' }, { label: '停止播放器', onSelect: () => void playerService.stop() });
  const orbitMenu: MenuEntry[] = [
    { label: '重置视角', disabled: !orbit, tip: orbit ? undefined : '环绕相机下可用',
      onSelect: () => playerService.camera({ mode: 'orbit', reset: true }) },
    { label: '环绕所选角色', disabled: !focusActor, tip: focusActor ? undefined : '先在画面中点选角色',
      onSelect: () => playerService.camera({ mode: 'orbit', focusActorId: focusActor! }) },
  ];

  return (
    <div className={styles.root}>
      <PanelBar wrap>
        {state.runId ? <Button variant="ghost" onClick={() => session.selectRun(null)}>退出回看</Button> : <>
          <MenuButton label="播放器" variant="ghost" caret entries={playerMenu}>
            <span className={styles.dim}>播放器</span>{selectedLive ? liveLabel(selectedLive) : '无'}
          </MenuButton>
          {codeUpdate?.kind === 'compiling' && <span className={styles.dim}>正在编译 Player 代码…</span>}
          {codeUpdate?.kind === 'adopting' && <span className={styles.dim}>正在采用新代码版本…</span>}
          {codeUpdate?.kind === 'ready' && (selectedLiveId
            ? <Button variant="ghost" tip="重启所选播放器并重新载入其现有内容" onClick={async () => {
              const buildId = codeUpdate.buildId;
              setCodeUpdate({ kind: 'adopting', buildId });
              const applied = await playerService.adoptCode(selectedLiveId, buildId);
              setCodeUpdate(applied ? null : { kind: 'ready', buildId });
            }}>采用新代码版本</Button>
            : <span className={styles.dim}>新代码版本已生成，可从播放器菜单启动</span>)}
          {state.liveId && <Button variant="ghost" disabled={!!loadBlocked} tip={loadBlocked ?? '载入所选内容'}
            onClick={async () => {
              const result = await playerService.loadContent(content!);
              if (result?.stage === 'applied') session.selectRun(null);
            }}>载入</Button>}
          <Button disabled={startBlocked !== null} tip={startBlocked ?? '按当前场景和输入方案运行一次并录制，Timeline 显示实际路线'}
            onClick={() => void session.startNewRun()}>新建运行</Button>
          {ready && <span className={styles.split}>
            <Button variant="ghost" pressed={orbit} tip="左键拖动旋转 · 右键或 Shift 拖动平移 · 滚轮缩放"
              onClick={() => playerService.camera({ mode: orbit ? 'game' : 'orbit' })}>环绕</Button>
            <MenuButton label="环绕选项" variant="ghost" className={styles.splitCaret} iconOnly caret entries={orbitMenu}>{null}</MenuButton>
          </span>}
        </>}
        {state.runId && authoringNotice !== null && <span role="status" className={styles.dim}>{authoringNotice}</span>}
        <span className={styles.grow} />
        {ready && player!.inputMode === 'device' && <Button variant="ghost" pressed={captureEnabled} tip="点击画面接管键鼠，Esc 释放"
          onClick={() => setCaptureBinding(captureEnabled ? null : binding)}>接管输入</Button>}
        {ready && <Button variant="ghost" onClick={() => void playerService.capture()}>截图</Button>}
        <Button variant="ghost" pressed={params.stats} onClick={() => setParams({ stats: !params.stats })}>Stats</Button>
        {playerTools.map(tool => <Button key={tool.id} variant="ghost" pressed={params.tools.includes(tool.id)}
          onClick={() => setParams({ tools: params.tools.includes(tool.id) ? params.tools.filter(id => id !== tool.id) : [...params.tools, tool.id] })}>{tool.label}</Button>)}
        {state.runId && <Button variant="ghost" pressed={params.overlay} disabled={overlayReason !== null}
          tip={overlayReason ?? '叠加画面同次表现的角色根位置'} onClick={() => setParams({ overlay: !params.overlay })}>Gizmos</Button>}
      </PanelBar>
      {playerTools.filter(tool => params.tools.includes(tool.id)).map(tool => <tool.component key={tool.id} />)}
      <Stage key={binding} stageRef={stage} ready={ready} orbit={orbit} overlay={params.overlay && overlayReason === null} stats={params.stats}
        captureEnabled={ready && captureEnabled && player?.inputMode === 'device'} />
    </div>
  );
}

function Stage({ stageRef, ready, orbit, overlay, stats, captureEnabled }: {
  stageRef: RefObject<HTMLDivElement | null>;
  ready: boolean;
  orbit: boolean;
  overlay: boolean;
  stats: boolean;
  captureEnabled: boolean;
}) {
  const video = useRef<HTMLCanvasElement>(null);
  const layer = useRef<HTMLCanvasElement>(null);
  const panelWindow = usePanelWindow();
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const livePicture = useSyncExternalStore(playerService.subscribeVideo, playerService.latestVideo);
  const view = useWorkbench(useShallow(state => ({
    liveId: state.liveId, runId: state.runId, archiveImage: state.archiveImage, frameKey: state.frameKey,
    viewerOpen: state.viewerOpen, player: state.live?.player ?? null, failure: state.failure,
  })));
  const [captured, setCaptured] = useState(false);
  const [decodeFailure, setDecodeFailure] = useState<{ image: Blob; reason: string; entryId: number | null } | null>(null);
  const frameSize = useRef({ width: 16, height: 9 });
  const overlayDraw = useRef<(frame: Picture) => void>(() => {});

  useEffect(() => {
    const element = stageRef.current!;
    if (!visible) return;
    const Observer = (panelWindow as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    const observer = new Observer(entries => {
      const { width, height } = entries[0].contentRect;
      useWorkbench.setState({ playerViewport: width > 0 && height > 0
        ? { width: Math.round(width), height: Math.round(height) } : null });
    });
    observer.observe(element);
    return () => { observer.disconnect(); useWorkbench.setState({ playerViewport: null }); };
  }, [visible, panelWindow, stageRef]);

  // ── 画面：挂载且可见时解码最新一帧并绘制 ──
  useEffect(() => {
    if (!visible) return;
    let decoding = false, again = false, alive = true;
    const draw = async () => {
      const frame = currentPicture();
      const drawnKey = getState().frameKey;
      const canvas = video.current;
      if (!canvas) return;
      if (!frame) {
        clearRendered(panelId);
        canvas.getContext('2d')!.clearRect(0, 0, canvas.width, canvas.height);
        const overlayCanvas = layer.current;
        if (overlayCanvas) overlayCanvas.getContext('2d')!.clearRect(0, 0, overlayCanvas.width, overlayCanvas.height);
        return;
      }
      if (decoding) { again = true; return; }
      decoding = true;
      try {
        const image = await panelWindow.createImageBitmap(frame.image);
        try {
          if (!alive || currentPicture()?.image !== frame.image) return;
          if (canvas.width !== frame.width || canvas.height !== frame.height) {
            canvas.width = frame.width; canvas.height = frame.height;
            if (layer.current) { layer.current.width = frame.width; layer.current.height = frame.height; }
          }
          const context = canvas.getContext('2d')!;
          context.save();
          try {
            if (frame.flipY) { context.translate(0, frame.height); context.scale(1, -1); }
            context.drawImage(image, 0, 0, frame.width, frame.height);
          } finally { context.restore(); }
        } finally { image.close(); }
        setDecodeFailure(null);
        frameSize.current = { width: frame.width, height: frame.height };
        overlayDraw.current(frame);
        const state = getState();
        if (state.runId && drawnKey && state.frameKey === drawnKey && state.frame?.hostFrame === frame.hostFrame)
          reportRendered(panelId, drawnKey);
      } catch (error) {
        if (!alive || currentPicture()?.image !== frame.image) return;
        clearRendered(panelId);
        canvas.getContext('2d')!.clearRect(0, 0, canvas.width, canvas.height);
        const overlayCanvas = layer.current;
        if (overlayCanvas) overlayCanvas.getContext('2d')!.clearRect(0, 0, overlayCanvas.width, overlayCanvas.height);
        setDecodeFailure({ image: frame.image, reason: `画面解码或绘制失败：${error instanceof Error ? error.message : String(error)}`,
          entryId: consoleActions.error('player', error) });
      } finally {
        decoding = false;
        if (again && alive) { again = false; void draw(); }
      }
    };
    void draw();
    const unsubscribeVideo = playerService.subscribeVideo(() => { if (getState().runId === null) void draw(); });
    const unsubscribeState = useWorkbench.subscribe((state, previous) => {
      if (state.liveId !== previous.liveId || state.runId !== previous.runId || state.archiveImage !== previous.archiveImage ||
        state.frameKey !== previous.frameKey || state.viewerOpen !== previous.viewerOpen) void draw();
    });
    return () => { alive = false; unsubscribeVideo(); unsubscribeState(); clearRendered(panelId); };
  }, [visible, panelWindow, panelId]);

  const overlayRef = useRef(overlay);
  overlayRef.current = overlay;
  const drawOverlay = useCallback((frame: Picture) => {
    const canvas = layer.current;
    if (!canvas) return;
    const context = canvas.getContext('2d')!;
    context.clearRect(0, 0, canvas.width, canvas.height);
    if (!overlayRef.current || !frame.worldToClip) return;
    const color = panelWindow.getComputedStyle(canvas).getPropertyValue('--logic-receiver').trim();
    const m = frame.worldToClip;
    context.font = '12px Consolas, monospace';
    context.lineWidth = 2;
    context.strokeStyle = context.fillStyle = color;
    for (const actor of frame.actors) {
      const [x, y, z] = actor.position;
      const cx = m[0] * x + m[4] * y + m[8] * z + m[12], cy = m[1] * x + m[5] * y + m[9] * z + m[13];
      const cw = m[3] * x + m[7] * y + m[11] * z + m[15];
      if (cw <= 0) continue;
      const sx = (cx / cw + 1) / 2 * frame.width, sy = (1 - cy / cw) / 2 * frame.height;
      context.beginPath();
      context.arc(sx, sy, 8, 0, Math.PI * 2);
      context.stroke();
      context.fillText(actor.actorId, sx + 11, sy - 6);
    }
  }, [panelWindow]);
  overlayDraw.current = drawOverlay;
  useEffect(() => {
    const frame = currentPicture();
    if (frame) drawOverlay(frame);
  }, [overlay, drawOverlay]);

  // ── 点选结果写入 selection ──
  useEffect(() => playerService.subscribePick(pick => {
    if (pick.actorId) {
      selectionActions.select({ kind: 'actor', id: pick.actorId });
      consoleActions.log('info', 'player', `点选 ${pick.actorId}${pick.hitCollider ? ` · 射线命中 ${pick.hitCollider}` : ''}`, pick.rawJson);
    } else consoleActions.log('info', 'player', `点击处附近没有角色${pick.hitCollider ? `；射线命中 ${pick.hitCollider}` : ''}`, pick.rawJson);
  }), []);

  // ── 输入接管：键鼠按面板所在窗口绑定；隐藏、关闭、移窗、失焦或 Esc 时立即释放（D12） ──
  const input = useRef({ keys: new Set<string>(), dx: 0, dy: 0, buttons: 0, scroll: 0 });
  const camera = useRef<CameraCommand | null>(null);
  const capturedRef = useRef(false);

  useEffect(() => {
    const element = stageRef.current;
    if (!element || !visible || !ready) return;
    const doc = panelWindow.document;
    const target = getState();
    const targetSource = target.live?.player?.source ?? null;
    const release = () => {
      const state = input.current;
      state.keys.clear(); state.dx = state.dy = state.buttons = state.scroll = 0;
      const current = getState();
      if (current.liveId === target.liveId && samePreviewLoad(targetSource, current.live?.player?.source ?? null) &&
        current.live?.player?.inputMode === 'device') playerService.releaseInput();
    };
    const lockChanged = () => {
      const locked = doc.pointerLockElement === element;
      capturedRef.current = locked;
      setCaptured(locked);
      useWorkbench.setState({ capturing: locked });
      if (!locked) release();
    };
    const buttonBit = (button: number) => 1 << ([0, 2, 1][button] ?? 0);
    let drag: { x: number; y: number; button: number; moved: boolean } | null = null;
    const down = (event: MouseEvent) => {
      if (capturedRef.current) { input.current.buttons |= buttonBit(event.button); return; }
      if (captureEnabledRef.current) { void element.requestPointerLock(); return; }
      drag = { x: event.clientX, y: event.clientY, button: event.button, moved: false };
    };
    const move = (event: MouseEvent) => {
      if (capturedRef.current) { input.current.dx += event.movementX; input.current.dy += event.movementY; return; }
      if (!drag) return;
      const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
      if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
      drag.x = event.clientX; drag.y = event.clientY;
      if (!drag.moved || !orbitRef.current) return;
      if (drag.button === 0 && !event.shiftKey) queueCamera(camera, { yaw: dx * 0.3, pitch: dy * 0.3 });
      else queueCamera(camera, { panX: -dx * 0.002, panY: dy * 0.002 });
    };
    const up = (event: MouseEvent) => {
      if (capturedRef.current) { input.current.buttons &= ~buttonBit(event.button); return; }
      if (drag && !drag.moved && drag.button === 0 && readyRef.current) {
        const point = imagePoint(video.current, event, frameSize.current);
        if (point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1) playerService.pick(point.x, point.y);
      }
      drag = null;
    };
    const wheel = (event: WheelEvent) => {
      event.preventDefault();
      if (capturedRef.current) input.current.scroll += Math.sign(event.deltaY) * 120;
      else if (orbitRef.current) queueCamera(camera, { zoom: Math.sign(event.deltaY) * 0.1 });
    };
    const key = (pressed: boolean) => (event: KeyboardEvent) => {
      if (!capturedRef.current) return;
      event.preventDefault();
      event.stopPropagation();
      if (pressed) input.current.keys.add(event.code);
      else input.current.keys.delete(event.code);
    };
    const keyDown = key(true), keyUp = key(false);
    const blur = () => { if (capturedRef.current) doc.exitPointerLock(); };
    const menu = (event: Event) => event.preventDefault();
    element.addEventListener('mousedown', down);
    element.addEventListener('wheel', wheel, { passive: false });
    element.addEventListener('contextmenu', menu);
    panelWindow.addEventListener('mousemove', move);
    panelWindow.addEventListener('mouseup', up);
    panelWindow.addEventListener('keydown', keyDown, true);
    panelWindow.addEventListener('keyup', keyUp, true);
    panelWindow.addEventListener('blur', blur);
    doc.addEventListener('pointerlockchange', lockChanged);
    return () => {
      element.removeEventListener('mousedown', down);
      element.removeEventListener('wheel', wheel);
      element.removeEventListener('contextmenu', menu);
      panelWindow.removeEventListener('mousemove', move);
      panelWindow.removeEventListener('mouseup', up);
      panelWindow.removeEventListener('keydown', keyDown, true);
      panelWindow.removeEventListener('keyup', keyUp, true);
      panelWindow.removeEventListener('blur', blur);
      doc.removeEventListener('pointerlockchange', lockChanged);
      if (doc.pointerLockElement === element) doc.exitPointerLock();
      if (capturedRef.current) {
        capturedRef.current = false;
        useWorkbench.setState({ capturing: false });
        release();
      }
      camera.current = null;
    };
  }, [panelWindow, stageRef, visible, ready]);

  const orbitRef = useRef(orbit);
  orbitRef.current = orbit;
  const readyRef = useRef(ready);
  readyRef.current = ready;
  const captureEnabledRef = useRef(captureEnabled);
  captureEnabledRef.current = captureEnabled;

  // 关闭接管或面板隐藏时立即释放。
  useEffect(() => {
    if ((!captureEnabled || !visible) && panelWindow.document.pointerLockElement === stageRef.current) panelWindow.document.exitPointerLock();
  }, [captureEnabled, visible, panelWindow, stageRef]);

  // 每个显示帧发送输入与合并后的相机增量。
  usePanelAnimationFrame(useCallback(() => {
    if (!playerService.connected()) return;
    const pads = panelWindow.navigator.getGamepads?.() ?? [];
    if (capturedRef.current || (captureEnabledRef.current && [...pads].some(pad => pad?.connected)))
      playerService.input(inputMessage(panelWindow, input.current));
    if (camera.current) { playerService.camera(camera.current); camera.current = null; }
  }, [panelWindow]), ready);

  const currentFailure = decodeFailure !== null && currentPicture()?.image === decodeFailure.image ? decodeFailure : null;
  const readingFailure = view.runId !== null && view.archiveImage === null ? view.failure : null;
  const emptyReason = currentFailure?.reason ?? readingFailure?.reason ?? (view.runId
    ? view.archiveImage === null ? '正在读取档案画面' : view.archiveImage.kind === 'unavailable' ? view.archiveImage.reason : null
    : !view.liveId ? '启动或选择一个播放器' : !view.viewerOpen ? '播放器画面连接尚未建立'
    : livePicture !== null ? null : view.player?.error ?? (view.player?.state === 'loading' ? '正在载入所选内容'
      : view.player?.source ? '等待当前内容的实际画面' : '尚无实际载入内容'));

  return (
    <div ref={stageRef} className={styles.stage} data-captured={captured || undefined}>
      {currentFailure !== null ? <ErrorLine text={currentFailure.reason} entryId={currentFailure.entryId} />
        : readingFailure !== null ? <ErrorLine text={readingFailure.reason} entryId={readingFailure.entryId} />
          : emptyReason !== null && <Empty text={emptyReason} />}
      <canvas ref={video} className={styles.video} hidden={emptyReason !== null} />
      <canvas ref={layer} className={styles.video} hidden={emptyReason !== null} />
      {(view.runId || view.liveId) && <Corner stats={stats} video={livePicture} />}
      {captured && <div className={styles.hint}>已接管键鼠 · Esc 释放</div>}
    </div>
  );
}

function queueCamera(camera: { current: CameraCommand | null }, delta: Partial<Record<'yaw' | 'pitch' | 'zoom' | 'panX' | 'panY', number>>) {
  const pending = camera.current ?? { mode: 'orbit', yaw: 0, pitch: 0, zoom: 0, panX: 0, panY: 0 };
  for (const [name, value] of Object.entries(delta) as [keyof typeof delta, number][]) pending[name] = (pending[name] ?? 0) + value;
  camera.current = pending;
}

type Picture = Omit<VideoFrame, 'worldToClip' | 'source'> & {
  worldToClip: number[] | null;
  actors: { actorId: string; position: Vec3 }[];
};

function currentPicture(): Picture | null {
  const state = getState();
  if (!state.runId) {
    if (!state.viewerOpen || state.liveId === null) return null;
    const frame = playerService.latestVideo();
    return frame ? { ...frame, actors: [] } : null;
  }
  const image = state.archiveImage;
  if (!image || image.kind === 'unavailable') return null;
  const camera = archiveOverlayReason(image) === null ? image.cameras[0] : null;
  return {
    hostFrame: image.metadata.sourceHostFrame, image: image.image, width: image.metadata.width, height: image.metadata.height,
    mediaType: image.metadata.mediaType, flipY: image.metadata.flipY,
    worldToClip: camera ? new Matrix4().fromArray(camera.projection).multiply(new Matrix4().fromArray(camera.worldToCamera)).toArray() : null,
    actors: image.presentation?.actors.map(actor => ({ actorId: actor.actorId, position: actor.worldRoot.position })) ?? [],
  };
}

function archiveOverlayReason(image: ArchiveImage | null): string | null {
  if (image === null) return '正在读取画面';
  if (image.kind === 'unavailable') return image.reason;
  if (image.overlayReason !== null) return image.overlayReason;
  if (image.metadata.source.content === null) return '画面未记录实际采用内容身份，不能确认叠加来源';
  const source = image.metadata.source;
  if (source.execution.kind === null || source.execution.identity === null) return '画面未记录实际执行身份，不能确认叠加来源';
  const presentation = image.presentation?.source ?? null, camera = image.cameras[0]?.source ?? null;
  if (!samePreviewLoad(source, presentation) || !samePreviewLoad(source, camera)) return '画面、骨骼与相机的实际来源或内容身份不一致';
  if (presentation?.clock.position.hostFrame !== source.clock.position.hostFrame || camera?.clock.position.hostFrame !== source.clock.position.hostFrame)
    return '画面、骨骼与相机的实际来源帧不一致';
  return null;
}

// 画布 object-fit: contain 留边时换算到画面 0–1。
function imagePoint(canvas: HTMLCanvasElement | null, event: MouseEvent, size: { width: number; height: number }) {
  if (!canvas) return { x: -1, y: -1 };
  const rect = canvas.getBoundingClientRect();
  const scale = Math.min(rect.width / size.width, rect.height / size.height);
  const left = rect.left + (rect.width - size.width * scale) / 2, top = rect.top + (rect.height - size.height * scale) / 2;
  return { x: (event.clientX - left) / (size.width * scale), y: (event.clientY - top) / (size.height * scale) };
}

// 浏览器 KeyboardEvent.code 转 Input System Key 名。
const keyNames: Record<string, string> = {
  Space: 'Space', Enter: 'Enter', Escape: 'Escape', Tab: 'Tab', Backspace: 'Backspace', ShiftLeft: 'LeftShift', ShiftRight: 'RightShift',
  ControlLeft: 'LeftCtrl', ControlRight: 'RightCtrl', AltLeft: 'LeftAlt', AltRight: 'RightAlt', ArrowUp: 'UpArrow', ArrowDown: 'DownArrow',
  ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow', CapsLock: 'CapsLock', Backquote: 'Backquote', Minus: 'Minus', Equal: 'Equals',
  BracketLeft: 'LeftBracket', BracketRight: 'RightBracket', Semicolon: 'Semicolon', Quote: 'Quote', Comma: 'Comma', Period: 'Period',
  Slash: 'Slash', Backslash: 'Backslash',
};
function keyName(code: string): string | undefined {
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit[0-9]$/.test(code) || /^F([1-9]|1[0-2])$/.test(code)) return code;
  return keyNames[code];
}

function inputMessage(view: Window, state: { keys: Set<string>; dx: number; dy: number; buttons: number; scroll: number }) {
  const pad = [...(view.navigator.getGamepads?.() ?? [])].find(item => item?.connected) ?? null;
  const message = {
    keys: [...state.keys].map(keyName).filter((name): name is string => !!name),
    mouseDx: state.dx, mouseDy: state.dy, mouseButtons: state.buttons, scroll: state.scroll, x: 0.5, y: 0.5,
    lx: pad?.axes[0] ?? 0, ly: pad?.axes[1] ?? 0, rx: pad?.axes[2] ?? 0, ry: pad?.axes[3] ?? 0,
    lt: pad?.buttons[6]?.value ?? 0, rt: pad?.buttons[7]?.value ?? 0,
    padButtons: (pad?.buttons ?? []).slice(0, 16).reduce((bits, button, index) => button.pressed ? bits | (1 << index) : bits, 0),
  };
  state.dx = state.dy = state.scroll = 0;
  return message;
}

function Corner({ stats, video }: { stats: boolean; video: VideoFrame | null }) {
  const view = useWorkbench(useShallow(state => ({
    player: state.live?.player ?? null, process: state.live?.state ?? null, runId: state.runId, archiveImage: state.archiveImage,
    archiveFrame: state.archiveFrame, failure: state.failure,
    hostFrame: state.hostFrame,
    targetPosition: state.targetPosition,
    frameDisplayName: state.summary?.frameDisplayName,
    logicSource: state.frame?.source ?? null,
    adoptedMode: state.runId !== null ? state.summary?.adoptedMode ?? null : state.live?.player?.adoptedMode ?? null,
  })));
  const actual = view.runId ? undefined : view.player?.source?.clock;
  const pictureSource = view.runId ? view.archiveImage?.kind === 'frame' ? view.archiveImage.metadata.source : null : video?.source ?? null;
  const picture = pictureSource?.clock;
  const imageHostFrame = view.runId && view.archiveImage && view.archiveImage.kind !== 'unavailable'
    ? view.archiveImage.metadata.sourceHostFrame : null;
  const adopted = view.runId ? view.logicSource : view.player?.source ?? null;
  const compared = view.runId ? '当前逻辑记录' : '当前 Player 状态';
  const sourceNotice = pictureSource === null || pictureSource.content === null
    ? '画面采用内容未记录'
    : adopted === null || adopted.content === null ? '当前采用内容未记录，无法确认同源'
    : adopted.execution.kind === null || adopted.execution.identity === null || pictureSource.execution.kind === null || pictureSource.execution.identity === null
      ? '执行身份未记录，无法确认同源'
    : !samePreviewLoad(adopted, pictureSource) ? `画面与${compared}的来源或内容不同`
    : pictureSource.clock.position.hostFrame !== adopted.clock.position.hostFrame ? `画面与${compared}的来源帧不同` : `与${compared}同源、同帧`;
  const notices = [
    !view.runId && view.targetPosition !== null && actual
      ? `定位 ${positionValue(view.targetPosition)} / 实际 ${clockPosition(actual)} …` : null,
    picture && actual && clockPosition(picture) !== clockPosition(actual)
      ? `画面 ${picture.positionDisplayName} ${clockPosition(picture)}` : null,
  ].filter(text => text !== null);
  return (
    <>
      {notices.length !== 0 && <div className={styles.badge}>{notices.join(' · ')}</div>}
      <div className={styles.source} title={`原始播放器画面\n${view.runId ? `Run：${view.runId}` : '实时 Player'}\n${sourceNotice}\n内容类型：${pictureSource?.content?.kind ?? '未记录'}\n实际采用修订：${pictureSource?.content?.revision ?? '未记录'}\n画面来源帧：${imageHostFrame ?? picture?.position.hostFrame ?? '未记录'}`}>
        原始画面 · {view.runId ? `Run ${view.runId}` : '实时 Player'} · {pictureSource?.content?.kind ?? '内容未记录'}
        {pictureSource?.content && ` · ${pictureSource.content.revision}`}
        {picture ? ` · ${picture.positionDisplayName} ${clockPosition(picture)}` : imageHostFrame !== null ? ` · 画面来源帧 ${imageHostFrame}` : ''} · {sourceNotice}
      </div>
      {stats && (
        <div className={styles.stats}>
          <b>Statistics</b>
          <div><span>来源</span><span>{view.runId ? '运行档案' : '实时 Player'}</span></div>
          <div><span>实际采用档位</span><span>{view.adoptedMode ?? '未记录'}</span></div>
          {view.runId ? <div><span>{view.frameDisplayName}</span><span>{view.hostFrame ?? '读取中'}</span></div> : <>
            <div><span>播放器</span><span>{view.player ? playerStates[view.player.state] : view.process}</span></div>
            {actual && <div><span>{actual.positionDisplayName}</span><span>{clockPosition(actual)}</span></div>}
            {pictureSource && picture && <>
              <div><span>画面{picture.positionDisplayName}</span><span>{clockPosition(picture)}</span></div>
              <div><span>采样序号</span><span>{pictureSource.sequence ?? '未记录'}</span></div>
              <div><span>画面采用档位</span><span>{pictureSource.adoptedMode ?? '未记录'}</span></div>
            </>}
            {view.player?.error && <div className={styles.error}><span>错误</span><span>{view.player.error}</span></div>}
          </>}
          {view.archiveImage && view.archiveImage.kind !== 'unavailable' && <>
            <div><span>画面来源帧</span><span>{view.archiveImage.metadata.sourceHostFrame}</span></div>
            <div><span>表现序号</span><span>{view.archiveImage.metadata.presentationSequence}</span></div>
          </>}
          {view.runId !== null && <CameraHistory frame={view.archiveFrame} image={view.archiveImage} failure={view.failure} />}
        </div>
      )}
    </>
  );
}

function CameraHistory({ frame, image, failure }: { frame: ArchiveFrame | null; image: ArchiveImage | null; failure: ReturnType<typeof getState>['failure'] }) {
  if (frame === null) return <div><span>历史相机输出</span><span>{failure?.reason ?? '正在读取当前帧的相机记录'}</span></div>;
  if (frame.cameras.length === 0) {
    const reason = frame.availability.find(item => item.channel === 'camera' && (!item.available || !item.complete))?.reason
      ?? frame.unavailable.find(item => item.channel === 'camera')?.reason ?? '当前逻辑帧未记录相机提交';
    return <div><span>历史相机输出</span><span title={reason}>{reason}</span></div>;
  }
  const picture = image?.kind === 'frame' ? image : null;
  return <>
    <b>历史相机输出 · {frame.cameras.length} 条记录</b>
    {frame.cameras.map((camera, index) => <CameraHistoryRecord key={`${camera.presentationSequence}/${camera.actorRosterIndex}/${camera.actorInstanceId}/${camera.rigInstanceId}/${index}`} camera={camera} picture={picture} />)}
  </>;
}

function CameraHistoryRecord({ camera, picture }: { camera: ArchiveFrame['cameras'][number]; picture: Extract<ArchiveImage, { kind: 'frame' }> | null }) {
  const source = camera.source;
  const hasOwner = camera.actorRosterIndex !== -1;
  const linked = picture !== null && camera.presentationSequence === picture.metadata.presentationSequence &&
    camera.cameraInstanceId === picture.metadata.cameraInstanceId && samePreviewLoad(source, picture.metadata.source) &&
    source.clock.position.hostFrame === picture.metadata.source.clock.position.hostFrame;
  const fields = [
    { label: '画面关联', value: picture === null ? '当前无档案帧画面' : linked ? '与当前画面同次提交' : '未与当前画面确认关联' },
    { label: '相机实例编号', value: camera.cameraInstanceId },
    { label: '历史绑定采用', value: camera.bindingAdopted ? '已采用' : '未采用' },
    { label: '角色目录序号', value: camera.actorRosterIndex },
    { label: '角色身份', value: camera.actorId },
    { label: '角色实例', value: camera.actorInstanceId },
    { label: 'rig 实例', value: hasOwner ? camera.rigInstanceId : '本帧未记录' },
    { label: '绑定身份', value: camera.bindingId },
    { label: '请求来源', value: camera.requestedSourceId },
    { label: '请求修订', value: camera.requestedSourceRevision },
    { label: 'Profile 身份', value: camera.profileId },
    { label: 'Profile 修订', value: camera.profileRevision },
    { label: '构图身份', value: camera.compositionId },
    { label: 'rig 绑定', value: camera.rigBindingId },
    { label: '虚拟相机', value: camera.virtualCameraId },
    { label: '输出管线', value: camera.pipeline },
    { label: '表现序号', value: camera.presentationSequence },
    { label: '来源帧', value: `${source.clock.positionDisplayName} ${source.clock.position.hostFrame}` },
    { label: '来源时间', value: `${source.clock.seconds} s` },
    { label: '实际采用内容', value: source.content === null ? '未记录' : `${source.content.kind} · ${source.content.revision}` },
    { label: '执行类型', value: source.execution.kind ?? '未记录' },
    { label: '执行身份', value: source.execution.identity ?? '未记录' },
    { label: '画面尺寸', value: `${camera.width} × ${camera.height}` },
    { label: '表现 Tick', value: camera.presentationTick },
    { label: '插值比例', value: camera.interpolationFactor },
  ];
  return <>
    <b>角色相机记录</b>
    {!hasOwner && <div><span>来源状态</span><span title="本帧没有角色相机来源，绑定与rig身份未记录；原相机输出矩阵仍保留。">本帧没有角色相机来源</span></div>}
    {hasOwner && !camera.bindingAdopted && <div><span>采用状态</span><span title="本帧绑定尚未确认采用；以下身份只表达同次历史记录。">本帧绑定未确认采用</span></div>}
    {fields.map(field => {
      const shown = field.value === '' ? '本帧未记录' : String(field.value);
      return <div key={field.label}><span>{field.label}</span><span title={shown}>{shown}</span></div>;
    })}
  </>;
}
