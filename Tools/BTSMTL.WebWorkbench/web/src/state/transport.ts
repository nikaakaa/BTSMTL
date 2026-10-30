import { playerApi } from '../api/player';
import { isAbort } from '../api/http';
import type { PlayerJobId } from '../api/playerRecords';
import type { LiveCommand, LiveCommandResult } from '../api/types';
import { consoleActions, getState, isCurrent, useWorkbench, type PlayerCommandRecord } from './store';

export type TransportSource = 'sealed' | 'player-live';

const source = 'transport';

export type ArchiveTimebase = Map<number, { hostFrame: string; seconds: number }>;
interface Timebase {
  runId: PlayerJobId;
  frames: ArchiveTimebase;
}
let timebase: Timebase | null = null;
let timeSeek: AbortController | null = null;
let review: { generation: number; origin: number; started: number; handle: number } | null = null;

export function transportSource(): TransportSource | null {
  const { liveId, live, runId, sealed } = getState();
  if (runId) return sealed ? 'sealed' : null;
  if (liveId && live?.player?.inputMode === 'device') return 'player-live';
  return null;
}

function frameCount(): number {
  return getState().summary?.frameCount ?? 0;
}

function writeCursor(frameIndex: number) {
  const tb = timebase && timebase.runId === getState().runId ? timebase : null;
  useWorkbench.setState({
    frameIndex,
    hostFrame: tb?.frames.get(frameIndex)?.hostFrame ?? null,
    seconds: tb?.frames.get(frameIndex)?.seconds ?? null,
  });
}

function moveCursor(frameIndex: number) {
  const count = frameCount();
  if (!count) return;
  const index = Math.max(0, Math.min(count - 1, Math.trunc(frameIndex)));
  if (index !== getState().frameIndex || getState().hostFrame === null) writeCursor(index);
}

async function seekTime(seconds: number) {
  const { runId, generation, summary } = getState();
  const context = timebase;
  if (!runId || !summary?.frameCount || !context || context.runId !== runId) {
    consoleActions.log('warn', source, '尚无可定位的 Player 档案时钟');
    return;
  }
  stopReview();
  timeSeek?.abort();
  const controller = timeSeek = new AbortController();
  const at = async (index: number) => {
    const known = context.frames.get(index);
    if (known) return known;
    const page = await playerApi.frames(runId, index, 1, controller.signal);
    controller.signal.throwIfAborted();
    const frame = page.items[0];
    const entry = { hostFrame: frame.hostFrame, seconds: frame.seconds };
    context.frames.set(index, entry);
    return entry;
  };
  try {
    let first = 0, last = summary.frameCount - 1;
    while (first < last) {
      const middle = (first + last) >> 1;
      if ((await at(middle)).seconds < seconds) first = middle + 1;
      else last = middle;
    }
    const after = await at(first);
    const before = first > 0 ? await at(first - 1) : null;
    controller.signal.throwIfAborted();
    if (isCurrent(generation)) moveCursor(before && seconds - before.seconds < after.seconds - seconds ? first - 1 : first);
  } catch (error) {
    if (!isAbort(error)) consoleActions.error(source, error);
  }
}

// ── 已封存运行：按宿主帧秒数回看（按真实流逝时间找到最后一个不晚于目标秒数的帧） ──

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
  const current = tb.frames.get(getState().frameIndex);
  if (!current) { consoleActions.log('warn', source, '当前帧时间尚未读取，无法回看'); return; }
  const state = { generation, origin: current.seconds, started: performance.now(), handle: 0 };
  const advance = (now: number) => {
    if (review !== state) return;
    if (!isCurrent(state.generation)) { stopReview(); return; }
    const target = state.origin + (now - state.started) / 1000;
    const last = frameCount() - 1;
    let index = getState().frameIndex;
    while (index < last && (tb.frames.get(index + 1)?.seconds ?? Infinity) <= target) index++;
    if (index !== getState().frameIndex) moveCursor(index);
    if (index >= last) { stopReview(); return; }
    state.handle = requestAnimationFrame(advance);
  };
  review = state;
  useWorkbench.setState({ reviewing: true });
  state.handle = requestAnimationFrame(advance);
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
  seekTime,

  secondsAt(index: number): number | null {
    return timebase?.runId === getState().runId ? timebase.frames.get(index)?.seconds ?? null : null;
  },

  duration(): number | null {
    const metadata = getState().archiveMetadata;
    return metadata ? (metadata.lastRecordedSdkFrame - metadata.firstRecordedSdkFrame) / metadata.sdkTickRate : null;
  },

  // 读取路线后由运行服务登记帧序号与宿主帧、秒数的对应；cursor 同步补齐。
  setTimebase(runId: PlayerJobId, frames: ArchiveTimebase) {
    timebase = { runId, frames };
    if (getState().runId === runId && frameCount()) writeCursor(Math.min(getState().frameIndex, frameCount() - 1));
  },

  // 运行载入或地址恢复时定位到指定帧序号，不改变跟随状态。
  place(frameIndex: number) {
    moveCursor(frameIndex);
  },

  // 切换运行后由运行服务调用：停止回看，清空播放器目标。
  reset() {
    stopReview();
    timeSeek?.abort();
    timebase = null;
    useWorkbench.setState({ targetFrame: null, pendingSeek: null });
  },

  isPlaying(): boolean {
    const state = getState();
    switch (transportSource()) {
      case 'sealed': return state.reviewing;
      case 'player-live': return state.live?.player?.state === 'playing';
      default: return false;
    }
  },

  play() {
    timeSeek?.abort();
    switch (transportSource()) {
      case 'sealed': startReview(); break;
      case 'player-live': void playerCommand({ type: 'play', commandId: crypto.randomUUID() }); break;
    }
  },

  pause() {
    switch (transportSource()) {
      case 'sealed': stopReview(); break;
      case 'player-live': void playerCommand({ type: 'pause', commandId: crypto.randomUUID() }); break;
    }
  },

  toggle() {
    if (transport.isPlaying()) transport.pause();
    else transport.play();
  },

  step(delta: 1 | -1) {
    timeSeek?.abort();
    switch (transportSource()) {
      case 'sealed':
        stopReview();
        moveCursor(getState().frameIndex + delta);
        break;
      case 'player-live':
        if (delta > 0) void playerCommand({ type: 'step', commandId: crypto.randomUUID(), count: 1 });
        break;
    }
  },

  // 档案按记录序号；实时试玩按播放器 SDK 帧。
  seek(frame: number) {
    timeSeek?.abort();
    switch (transportSource()) {
      case 'sealed':
        stopReview();
        moveCursor(frame);
        break;
      case 'player-live':
        seekPlayer(frame);
        break;
    }
  },

  home() { transport.seek(0); },
  end() { transport.seek(transportSource() === 'player-live' ? Number(getState().live?.player?.lastFrame ?? 0) : frameCount() - 1); },

};

// 运行或场景切换时停止回看。
useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) {
    stopReview();
    if (timebase && timebase.runId !== state.runId) timebase = null;
  }
});
