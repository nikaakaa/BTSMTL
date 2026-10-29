import { playerApi } from '../api/player';
import { previewApi } from '../api/preview';
import type { LiveCommand, LiveCommandResult, PlaybackCommand } from '../api/types';
import { consoleActions, getState, isCurrent, useWorkbench, type PlayerCommandRecord } from './store';

// 唯一控制入口：工具栏、Timeline、快捷键都调用这里；cursor 只在这里写入。
//
// 来源                         play / pause            step                 seek
// sealed                       本页按帧秒数回看        移动 cursor          移动 cursor
// sealed-with-player           同上，并向播放器跳帧    同上                 同上
// dotnet-session               发往 /controls          +1 发 Step           只移动 cursor
// player-live（离线包试玩）    发给播放器              +1 发 step           发给播放器
export type TransportSource = 'sealed' | 'sealed-with-player' | 'dotnet-session' | 'player-live';

const source = 'transport';
const pollMilliseconds = 500;

// 帧序号对应的宿主帧与秒数，由读取运行路线的服务提供。
interface Timebase {
  runId: string;
  hostFrames: string[];
  seconds: number[];
}
let timebase: Timebase | null = null;
let review: { generation: number; origin: number; started: number; handle: number } | null = null;

export function transportSource(): TransportSource | null {
  const { liveId, live, runId, receipt, sealed } = getState();
  if (liveId && live?.player?.inputMode === 'device') return 'player-live';
  if (!runId) return null;
  if (receipt && (receipt.stage === 'accepted' || receipt.stage === 'running')) return 'dotnet-session';
  if (!sealed) return null;
  return liveId && live?.runId === runId ? 'sealed-with-player' : 'sealed';
}

function frameCount(): number {
  return getState().summary?.frameCount ?? 0;
}

function writeCursor(frameIndex: number) {
  const tb = timebase && timebase.runId === getState().runId ? timebase : null;
  useWorkbench.setState({
    frameIndex,
    hostFrame: tb?.hostFrames[frameIndex] ?? null,
    seconds: tb?.seconds[frameIndex] ?? null,
  });
}

function moveCursor(frameIndex: number) {
  const count = frameCount();
  if (!count) return;
  const index = Math.max(0, Math.min(count - 1, Math.trunc(frameIndex)));
  if (index !== getState().frameIndex || getState().hostFrame === null) writeCursor(index);
  if (transportSource() === 'sealed-with-player') {
    const hostFrame = getState().hostFrame;
    if (hostFrame !== null) seekPlayer(Number(hostFrame));
  }
}

// ── 已封存运行：按宿主帧秒数回看（沿用 playHistory：按真实流逝时间找到最后一个不晚于目标秒数的帧） ──

function stopReview() {
  if (!review) return;
  cancelAnimationFrame(review.handle);
  review = null;
  useWorkbench.setState({ reviewing: false });
}

function startReview() {
  const { runId, generation } = getState();
  const tb = timebase;
  const count = frameCount();
  if (!runId || !tb || tb.runId !== runId || !count) {
    consoleActions.log('warn', source, '帧时间尚未读取，无法回看');
    return;
  }
  if (getState().frameIndex >= count - 1) moveCursor(0);
  const state = { generation, origin: tb.seconds[getState().frameIndex] ?? 0, started: performance.now(), handle: 0 };
  const advance = (now: number) => {
    if (review !== state) return;
    if (!isCurrent(state.generation)) { stopReview(); return; }
    const target = state.origin + (now - state.started) / 1000;
    const last = frameCount() - 1;
    let index = getState().frameIndex;
    while (index < last && (tb.seconds[index + 1] ?? Infinity) <= target) index++;
    if (index !== getState().frameIndex) moveCursor(index);
    if (index >= last) { stopReview(); return; }
    state.handle = requestAnimationFrame(advance);
  };
  review = state;
  useWorkbench.setState({ reviewing: true });
  state.handle = requestAnimationFrame(advance);
}

// ── 运行中 .NET 会话：/controls，submitted 时按命令身份重查 ──

async function control(command: PlaybackCommand) {
  const { runId, generation } = getState();
  if (!runId) return;
  const commandId = crypto.randomUUID();
  try {
    let result = await previewApi.control(runId, { commandId, command });
    while (result.stage === 'submitted' && isCurrent(generation)) {
      await new Promise(resolve => setTimeout(resolve, pollMilliseconds));
      if (!isCurrent(generation)) return;
      result = await previewApi.readControl(runId, commandId);
    }
    if (result.stage === 'rejected') consoleActions.log('error', source, result.error ?? `${command} 被拒绝`, JSON.stringify(result));
  } catch (error) {
    consoleActions.error(source, error);
  }
}

// ── 播放器命令：记录 sent / submitted / applied / rejected ──

function record(type: string, result: LiveCommandResult): PlayerCommandRecord {
  const ack = result.ack;
  const entry: PlayerCommandRecord = {
    type, commandId: result.commandId, stage: result.stage,
    hostFrame: ack?.hostFrame ?? null, error: ack && !ack.ok ? ack.error ?? '播放器拒绝' : null,
  };
  useWorkbench.setState({ lastCommand: entry });
  if (entry.stage === 'rejected' || entry.stage === 'not-applied')
    consoleActions.log('error', source, `${type}：${entry.error ?? '播放器未执行'}`, ack?.rawJson);
  else if (entry.stage === 'submitted')
    consoleActions.log('warn', source, `${type}：等待时限内未收到播放器回执`);
  return entry;
}

async function playerCommand(command: LiveCommand): Promise<PlayerCommandRecord | null> {
  const liveId = getState().liveId;
  if (!liveId) return null;
  try {
    const entry = record(command.type, await playerApi.command(liveId, command));
    if (entry.stage === 'applied' && entry.hostFrame !== null) useWorkbench.setState({ actualFrame: entry.hostFrame });
    return entry;
  } catch (error) {
    consoleActions.error(source, error);
    return null;
  }
}

// 跳帧合并：只保留最新目标帧；上一条未回执时不发送，回执后与实际帧不同再补发。
function seekPlayer(frame: number) {
  useWorkbench.setState({ targetFrame: frame });
  if (getState().pendingSeek) return;
  void sendSeek(frame);
}

async function sendSeek(frame: number) {
  const liveId = getState().liveId;
  const commandId = crypto.randomUUID();
  useWorkbench.setState({ pendingSeek: { commandId, frame } });
  await playerCommand({ type: 'seek', commandId, frame });
  if (getState().liveId !== liveId) return;
  useWorkbench.setState({ pendingSeek: null });
  const { targetFrame, actualFrame } = getState();
  if (targetFrame !== null && String(targetFrame) !== actualFrame && targetFrame !== frame) void sendSeek(targetFrame);
}

// ── 公开入口 ──

export const transport = {
  source: transportSource,

  // 读取路线后由运行服务登记帧序号与宿主帧、秒数的对应；cursor 同步补齐。
  setTimebase(runId: string, hostFrames: string[], seconds: number[]) {
    timebase = { runId, hostFrames, seconds };
    if (getState().runId === runId && frameCount()) writeCursor(Math.min(getState().frameIndex, frameCount() - 1));
  },

  // 运行载入或地址恢复时定位到指定帧序号，不改变跟随状态。
  place(frameIndex: number) {
    moveCursor(frameIndex);
  },

  // 切换运行后由运行服务调用：停止回看，清空播放器目标。
  reset() {
    stopReview();
    timebase = null;
    useWorkbench.setState({ targetFrame: null, pendingSeek: null });
  },

  // 运行中会话帧数增长时调用；跟随实时帧时 cursor 移到最新帧。
  framesGrown() {
    if (getState().followLive) moveCursor(frameCount() - 1);
  },

  setFollowLive(followLive: boolean) {
    useWorkbench.setState({ followLive });
    if (followLive) moveCursor(frameCount() - 1);
  },

  isPlaying(): boolean {
    const state = getState();
    switch (transportSource()) {
      case 'sealed': case 'sealed-with-player': return state.reviewing;
      case 'dotnet-session': return state.hostPlayback === 'playing';
      case 'player-live': return state.live?.player?.state === 'playing';
      default: return false;
    }
  },

  play() {
    switch (transportSource()) {
      case 'sealed': case 'sealed-with-player': startReview(); break;
      case 'dotnet-session': void control('Play'); break;
      case 'player-live': void playerCommand({ type: 'play', commandId: crypto.randomUUID() }); break;
    }
  },

  pause() {
    switch (transportSource()) {
      case 'sealed': case 'sealed-with-player': stopReview(); break;
      case 'dotnet-session': void control('Pause'); break;
      case 'player-live': void playerCommand({ type: 'pause', commandId: crypto.randomUUID() }); break;
    }
  },

  toggle() {
    if (transport.isPlaying()) transport.pause();
    else transport.play();
  },

  step(delta: 1 | -1) {
    switch (transportSource()) {
      case 'sealed': case 'sealed-with-player':
        stopReview();
        moveCursor(getState().frameIndex + delta);
        break;
      case 'dotnet-session':
        useWorkbench.setState({ followLive: false });
        if (delta > 0 && getState().hostPlayback === 'paused' && getState().frameIndex >= frameCount() - 1) void control('Step');
        else moveCursor(getState().frameIndex + delta);
        break;
      case 'player-live':
        if (delta > 0) void playerCommand({ type: 'step', commandId: crypto.randomUUID(), count: 1 });
        break;
    }
  },

  // .NET 来源按帧序号；离线包试玩按播放器 SDK 帧。
  seek(frame: number) {
    switch (transportSource()) {
      case 'sealed': case 'sealed-with-player':
        stopReview();
        moveCursor(frame);
        break;
      case 'dotnet-session':
        useWorkbench.setState({ followLive: false });
        moveCursor(frame);
        break;
      case 'player-live':
        seekPlayer(frame);
        break;
    }
  },

  home() { transport.seek(0); },
  end() { transport.seek(transportSource() === 'player-live' ? Number(getState().live?.player?.lastFrame ?? 0) : frameCount() - 1); },

  cancelRun() {
    const { runId } = getState();
    if (!runId) return;
    void previewApi.cancel(runId).then(result => {
      if (!result.cancellationRequested) consoleActions.log('warn', source, '宿主没有仍在执行的对应进程');
    }, error => consoleActions.error(source, error));
  },
};

// 运行或场景切换时停止回看。
useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) {
    stopReview();
    if (timebase && timebase.runId !== state.runId) timebase = null;
  }
});
