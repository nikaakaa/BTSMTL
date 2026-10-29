import { useCallback, useEffect, useMemo, useRef, useState, type RefObject } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { AlignmentState, CameraCommand, PlayerState, VideoFrame } from '../../api/types';
import { usePanelAnimationFrame, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { cachedFrame, readFrame } from '../../state/frames';
import { playerService } from '../../state/player';
import { consoleActions, getState, selectionActions, useWorkbench } from '../../state/store';
import { BarSeparator, Empty, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import styles from './player.module.css';

// 画面面板：构建选择、启动与停止常驻播放器、载入运行或离线包、画面、键鼠和手柄接管、
// 环绕相机、.NET 根位置叠加、点选、截图、逐帧对照角标。播放控制统一走 transport（D6、D10）。

const playerStates: Record<PlayerState, string> = {
  idle: '空闲', loading: '载入中', paused: '已暂停', playing: '播放中', stepping: '单步中', seeking: '跳帧中', failed: '失败',
};
const alignmentNames: Record<AlignmentState, string> = {
  matched: '一致', mismatch: '不一致', pending: '等待对照', 'no-dotnet-record': '.NET 记录未封存',
  'live-input': '实时试玩（无对照）', 'not-loaded': '未载入',
};

interface Params {
  build: string | null;
  overlay: boolean;
  stats: boolean;
}

export function PlayerPanel() {
  const images = useWorkbench(state => state.images);
  const hostAvailable = useWorkbench(state => state.hostAvailable);
  if (hostAvailable === false) return <Empty text="未接入运行宿主" />;
  if (!images) return <Empty text="未接入画面来源" />;
  return <PlayerView frameName={images.frameDisplayName} />;
}

function PlayerView({ frameName }: { frameName: string }) {
  const [params, setParams] = usePanelParams<Params>({ build: null, overlay: false, stats: true });
  const state = useWorkbench(useShallow(current => ({
    builds: current.playerBuilds, lives: current.lives, liveId: current.liveId, live: current.live, viewerOpen: current.viewerOpen,
    runId: current.runId, sealed: current.sealed, scenePath: current.scenePath, sources: current.sources, selection: current.selection,
  })));
  const stage = useRef<HTMLDivElement>(null);
  const [captureEnabled, setCaptureEnabled] = useState(false);
  const player = state.live?.player ?? null;
  const ready = state.viewerOpen && !!player && ['paused', 'playing', 'stepping', 'seeking'].includes(player.state);
  const orbit = player?.cameraMode === 'orbit';

  useEffect(() => {
    void playerService.refresh();
    playerService.resumePendingStart();
    const timer = setInterval(() => void playerService.refresh(), 3000);
    return () => clearInterval(timer);
  }, []);

  const build = state.builds?.find(item => item.buildId === params.build) ?? state.builds?.[0] ?? null;
  const scene = state.sources?.scenes.find(item => item.path === state.scenePath) ?? null;
  const loading = player?.state === 'loading';
  const loadRunBlocked = !state.viewerOpen ? '尚未连接播放器画面' : !state.runId ? '选择一个运行' : !state.sealed ? '运行尚未封存' : loading ? '正在载入' : null;
  const loadSceneBlocked = !state.viewerOpen ? '尚未连接播放器画面' : !scene ? '先选择内容' : loading ? '正在载入' : null;
  const focusActor = state.selection?.kind === 'actor' ? state.selection.id : null;

  return (
    <div className={styles.root}>
      <PanelBar>
        <Select label="播放器构建" placeholder="播放器构建" value={build?.buildId ?? null} disabled={!state.builds?.length}
          tip={state.builds && !state.builds.length ? '画面来源尚未提供可用构建' : build?.fields.map(field => `${field.displayName} ${field.value}`).join('\n')}
          options={(state.builds ?? []).map(item => ({ value: item.buildId, label: item.displayName }))}
          onChange={value => setParams({ build: value })} />
        <Button variant="ghost" disabled={!build} tip={build ? '启动常驻播放器' : '没有可用构建'}
          onClick={() => void playerService.start(build!.buildId, stage.current?.clientWidth ?? 960)}>启动</Button>
        <Select label="播放器" placeholder="播放器" value={state.liveId} disabled={!state.lives.length}
          tip={state.lives.length ? undefined : '尚未启动播放器'}
          options={state.lives.map(live => ({ value: live.liveId, label: `${live.state} · ${new Date(live.startedAt).toLocaleTimeString()}` }))}
          onChange={value => playerService.attach(value)} />
        <Button variant="ghost" disabled={!state.liveId} onClick={() => void playerService.stop()}>停止</Button>
        <BarSeparator />
        <Button variant="ghost" disabled={!!loadRunBlocked} tip={loadRunBlocked ?? '按当前运行载入同一离线包与输入日程'}
          onClick={() => void playerService.loadRun(state.runId!)}>载入运行</Button>
        <Button variant="ghost" disabled={!!loadSceneBlocked} tip={loadSceneBlocked ?? '载入当前内容的离线包，接管输入试玩'}
          onClick={() => void playerService.loadScene(scene!.path, scene!.revision)}>离线包试玩</Button>
      </PanelBar>
      {/* 照 Unity Game 视图工具条：相机与输入在左，Stats、Gizmos 在右 */}
      <PanelBar>
        <Button variant="ghost" pressed={orbit} disabled={!ready} onClick={() => playerService.camera({ mode: orbit ? 'game' : 'orbit' })}>环绕</Button>
        <Button variant="ghost" disabled={!ready || !orbit} onClick={() => playerService.camera({ mode: 'orbit', reset: true })}>重置</Button>
        <Button variant="ghost" disabled={!ready || !focusActor} tip={focusActor ? '环绕所选角色' : '点选画面中的角色'}
          onClick={() => playerService.camera({ mode: 'orbit', focusActorId: focusActor! })}>环绕所选</Button>
        <BarSeparator />
        <Button variant="ghost" pressed={captureEnabled} disabled={!ready || player?.inputMode !== 'device'}
          tip={player?.inputMode === 'device' ? '点击画面接管键鼠，Esc 释放' : '离线包试玩时可接管'}
          onClick={() => setCaptureEnabled(!captureEnabled)}>接管输入</Button>
        <Button variant="ghost" disabled={!ready} onClick={() => void playerService.capture()}>截图</Button>
        <span className={styles.grow} />
        <Button variant="ghost" pressed={params.stats} onClick={() => setParams({ stats: !params.stats })}>Stats</Button>
        <Button variant="ghost" pressed={params.overlay} disabled={!ready || !state.live?.runId}
          tip={state.live?.runId ? '叠加 .NET 根位置' : '载入运行后可叠加'} onClick={() => setParams({ overlay: !params.overlay })}>Gizmos</Button>
      </PanelBar>
      <Stage stageRef={stage} ready={ready} orbit={orbit} overlay={params.overlay} stats={params.stats}
        captureEnabled={captureEnabled && player?.inputMode === 'device'} frameName={frameName} />
    </div>
  );
}

function Stage({ stageRef, ready, orbit, overlay, stats, captureEnabled, frameName }: {
  stageRef: RefObject<HTMLDivElement | null>;
  ready: boolean;
  orbit: boolean;
  overlay: boolean;
  stats: boolean;
  captureEnabled: boolean;
  frameName: string;
}) {
  const video = useRef<HTMLCanvasElement>(null);
  const layer = useRef<HTMLCanvasElement>(null);
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  const liveId = useWorkbench(state => state.liveId);
  const [captured, setCaptured] = useState(false);
  const frameSize = useRef({ width: 16, height: 9, hostFrame: '', matrix: [] as number[] });
  const overlayDraw = useRef<(frame: VideoFrame) => void>(() => {});

  // ── 画面：挂载且可见时解码最新一帧并绘制 ──
  useEffect(() => {
    if (!visible) return;
    let decoding = false, again = false, alive = true;
    const draw = async () => {
      const frame = playerService.latestVideo();
      const canvas = video.current;
      if (!frame || !canvas) return;
      if (decoding) { again = true; return; }
      decoding = true;
      try {
        const image = await panelWindow.createImageBitmap(frame.image);
        if (!alive) { image.close(); return; }
        if (canvas.width !== frame.width || canvas.height !== frame.height) {
          canvas.width = frame.width; canvas.height = frame.height;
          if (layer.current) { layer.current.width = frame.width; layer.current.height = frame.height; }
        }
        const context = canvas.getContext('2d')!;
        context.save();
        if (frame.flipY) { context.translate(0, frame.height); context.scale(1, -1); }
        context.drawImage(image, 0, 0, frame.width, frame.height);
        context.restore();
        image.close();
        frameSize.current = { width: frame.width, height: frame.height, hostFrame: frame.hostFrame, matrix: frame.worldToClip };
        overlayDraw.current(frame);
      } catch (error) {
        consoleActions.error('player', error);
      } finally {
        decoding = false;
        if (again && alive) { again = false; void draw(); }
      }
    };
    void draw();
    const unsubscribe = playerService.subscribeVideo(() => void draw());
    return () => { alive = false; unsubscribe(); };
  }, [visible, panelWindow, liveId]);

  // ── .NET 根位置叠加：只在载入的运行就是当前运行时，按帧缓存读取 ──
  const overlayRef = useRef(overlay);
  overlayRef.current = overlay;
  const hostIndex = useHostFrameIndex();
  const drawOverlay = useCallback((frame: VideoFrame) => {
    const canvas = layer.current;
    if (!canvas) return;
    const context = canvas.getContext('2d')!;
    context.clearRect(0, 0, canvas.width, canvas.height);
    const state = getState();
    if (!overlayRef.current || !state.live?.runId || state.live.runId !== state.runId) return;
    const index = hostIndex.current.get(frame.hostFrame);
    if (index === undefined) return;
    const record = cachedFrame(state.runId, index);
    if (!record) {
      void readFrame(index).then(() => { if (playerService.latestVideo() === frame) drawOverlay(frame); });
      return;
    }
    const color = panelWindow.getComputedStyle(canvas).getPropertyValue('--logic-receiver').trim();
    const m = frame.worldToClip;
    context.font = '12px Consolas, monospace';
    context.lineWidth = 2;
    context.strokeStyle = context.fillStyle = color;
    for (const actor of record.actors) {
      const [x, y, z] = actor.root.position;
      const cx = m[0] * x + m[4] * y + m[8] * z + m[12], cy = m[1] * x + m[5] * y + m[9] * z + m[13];
      const cw = m[3] * x + m[7] * y + m[11] * z + m[15];
      if (cw <= 0) continue;
      const sx = (cx / cw + 1) / 2 * frame.width, sy = (1 - cy / cw) / 2 * frame.height;
      context.beginPath();
      context.arc(sx, sy, 8, 0, Math.PI * 2);
      context.stroke();
      context.fillText(actor.displayName, sx + 11, sy - 6);
    }
  }, [panelWindow, hostIndex]);
  overlayDraw.current = drawOverlay;
  useEffect(() => {
    const frame = playerService.latestVideo();
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
    if (!element) return;
    const doc = panelWindow.document;
    const release = () => {
      const state = input.current;
      state.keys.clear(); state.dx = state.dy = state.buttons = state.scroll = 0;
      if (getState().live?.player?.inputMode === 'device') playerService.input(inputMessage(panelWindow, state));
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
    };
  }, [panelWindow, stageRef]);

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

  return (
    <div ref={stageRef} className={styles.stage} data-captured={captured || undefined}>
      {!liveId && <Empty text="启动或选择一个播放器" />}
      <canvas ref={video} className={styles.video} hidden={!liveId} />
      <canvas ref={layer} className={styles.video} hidden={!liveId} />
      {liveId && <Corner frameName={frameName} stats={stats} />}
      {captured && <div className={styles.hint}>已接管键鼠 · Esc 释放</div>}
    </div>
  );
}

function queueCamera(camera: { current: CameraCommand | null }, delta: Partial<Record<'yaw' | 'pitch' | 'zoom' | 'panX' | 'panY', number>>) {
  const pending = camera.current ?? { mode: 'orbit', yaw: 0, pitch: 0, zoom: 0, panX: 0, panY: 0 };
  for (const [name, value] of Object.entries(delta) as [keyof typeof delta, number][]) pending[name] = (pending[name] ?? 0) + value;
  camera.current = pending;
}

// 帧序号索引：宿主帧 → 帧序号，供叠加读取 .NET 记录。
function useHostFrameIndex() {
  const frames = useWorkbench(state => state.routeFrames);
  const index = useMemo(() => new Map(frames.map((frame, position) => [frame.hostFrame, position])), [frames]);
  const ref = useRef(index);
  ref.current = index;
  return ref;
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

// 右上角 Unity 式 Statistics 小窗：播放器状态、目标帧 / 实际帧、逐帧对照结论；明细在检查器。
function Corner({ frameName, stats }: { frameName: string; stats: boolean }) {
  const view = useWorkbench(useShallow(state => ({
    player: state.live?.player ?? null, process: state.live?.state ?? null, alignment: state.alignment,
    targetFrame: state.targetFrame, actualFrame: state.actualFrame, pending: !!state.pendingSeek,
  })));
  const mismatch = view.targetFrame !== null && view.actualFrame !== null && String(view.targetFrame) !== view.actualFrame;
  return (
    <>
      {mismatch && <div className={styles.badge}>{view.targetFrame} / {view.actualFrame}{view.pending ? ' …' : ''}</div>}
      {stats && (
        <div className={styles.stats}>
          <b>Statistics</b>
          <div><span>播放器</span><span>{view.player ? playerStates[view.player.state] ?? view.player.state : view.process ?? ''}</span></div>
          {view.player && <div><span>{frameName}</span><span>{view.player.hostFrame}</span></div>}
          {view.player?.error && <div className={styles.error}><span>错误</span><span>{view.player.error}</span></div>}
          {view.alignment && (
            <>
              <div><span>对照</span><span data-state={view.alignment.state}>{alignmentNames[view.alignment.state] ?? view.alignment.state}</span></div>
              <div><span>已对照帧</span><span>{view.alignment.comparedFrames}</span></div>
              {view.alignment.firstMismatchFrame !== null && <div><span>首个不一致</span><span>{view.alignment.firstMismatchFrame}</span></div>}
            </>
          )}
        </div>
      )}
    </>
  );
}
