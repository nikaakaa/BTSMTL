import { playerApi } from '../api/player';
import { isAbort } from '../api/http';
import { samePreviewContent, samePreviewLoad, type PlayerJobId, type PreviewPosition, type PreviewSampleSource } from '../api/playerRecords';
import type { Frame, LiveCommand, LiveCommandResult, PlayerAck } from '../api/types';
import { consoleActions, getState, isCurrent, useWorkbench, type PlayerCommandRecord } from './store';

export type TransportSource = 'sealed' | 'player-live';

const source = 'transport';

export type ArchiveTimebase = Map<number, Pick<Frame, 'hostFrame' | 'seconds' | 'source'>>;
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
  if (liveId && live?.player?.source && ['paused', 'playing', 'stepping', 'seeking'].includes(live.player.state)) return 'player-live';
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

async function readTime(context: Timebase, index: number, signal: AbortSignal) {
  const known = context.frames.get(index);
  if (known) return known;
  const page = await playerApi.frames(context.runId, index, 1, signal);
  signal.throwIfAborted();
  const frame = page.items[0];
  const entry = { hostFrame: frame.hostFrame, seconds: frame.seconds, source: frame.source };
  context.frames.set(index, entry);
  return entry;
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
  const at = (index: number) => readTime(context, index, controller.signal);
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

function record(type: string, result: LiveCommandResult): PlayerCommandRecord | null {
  const ack = result.ack;
  const context = getState().live?.player?.source ?? null;
  if (ack !== null && (ack.source !== null && !samePreviewLoad(ack.source, context) ||
    ack.content !== null && !samePreviewContent(ack.content, context?.content ?? null))) return null;
  const entry: PlayerCommandRecord = { type, ...result };
  useWorkbench.setState({ lastCommand: entry });
  if (entry.stage === 'rejected' || entry.stage === 'not-applied')
    consoleActions.log('error', source, `${type}：${ack?.error ?? '播放器未执行'}`, ack?.rawJson);
  else if (entry.stage === 'submitted')
    consoleActions.log('warn', source, `${type}：等待时限内未收到播放器回执`);
  return entry;
}

async function playerCommand(command: LiveCommand): Promise<PlayerCommandRecord | null> {
  const liveId = getState().liveId;
  const context = getState().live?.player?.source ?? null;
  if (!liveId) return null;
  const reason = transport.unavailableReason(command.type);
  if (reason !== null) {
    consoleActions.log('warn', source, reason);
    return null;
  }
  try {
    const result = await playerApi.command(liveId, command);
    if (getState().liveId !== liveId || !samePreviewLoad(context, getState().live?.player?.source ?? null)) return null;
    return record(command.type, result);
  } catch (error) {
    consoleActions.error(source, error);
    return null;
  }
}

// 定位合并绑定本次载入，上一条回执后只补发更新的目标。
function seekPlayer(position: PreviewPosition) {
  useWorkbench.setState({ targetPosition: position });
  if (getState().pendingSeek) return;
  void sendSeek(position);
}

async function sendSeek(position: PreviewPosition) {
  const liveId = getState().liveId;
  const context = getState().live!.player!.source!;
  const commandId = crypto.randomUUID();
  useWorkbench.setState({ pendingSeek: { commandId, source: context, position } });
  await playerCommand({ type: 'seek', commandId, position });
  if (getState().liveId !== liveId || getState().pendingSeek?.commandId !== commandId ||
    !samePreviewLoad(context, getState().live?.player?.source ?? null)) return;
  const { targetPosition } = getState();
  if (targetPosition !== null && targetPosition.hostFrame !== position.hostFrame)
    void sendSeek(targetPosition);
  else useWorkbench.setState({ pendingSeek: null, targetPosition: null });
}

// ── 公开入口 ──

export const transport = {
  source: transportSource,
  seekTime,

  acceptAck(ack: PlayerAck) {
    const command = getState().lastCommand;
    if (command?.commandId === ack.commandId)
      record(command.type, { commandId: ack.commandId, stage: ack.ok ? 'applied' : 'rejected', adoptedMode: ack.adoptedMode, ack });
  },

  action(name: string) {
    return getState().live?.player?.transport?.find(item => item.action === name);
  },

  unavailableReason(name: string) {
    const player = getState().live?.player;
    if (player?.transport === null) return player.transportUnavailableReason;
    const action = transport.action(name);
    return action === undefined ? '宿主未提供此操作的资格' : action.available ? null : action.reason;
  },

  secondsAt(index: number): number | null {
    return timebase?.runId === getState().runId ? timebase.frames.get(index)?.seconds ?? null : null;
  },

  recordPosition(runId: PlayerJobId, index: number, sample: PreviewSampleSource) {
    if (timebase?.runId !== runId || getState().runId !== runId)
      return { kind: 'unavailable', reason: '采样点不属于当前打开的档案' } as const;
    if (transportSource() !== 'sealed') return { kind: 'unavailable', reason: '当前档案尚未封存，不能定位录制帧' } as const;
    const frame = timebase.frames.get(index);
    if (frame === undefined) return { kind: 'unavailable', reason: '采样点对应的录制帧时间尚未读取' } as const;
    if (frame.source === null || sample.execution.kind === null || sample.execution.identity === null)
      return { kind: 'unavailable', reason: '采样点或录制帧缺少实际执行身份' } as const;
    if (frame.hostFrame !== sample.clock.position.hostFrame || !samePreviewLoad(frame.source, sample))
      return { kind: 'unavailable', reason: '采样点与录制帧的来源或内容身份不匹配' } as const;
    return { kind: 'record', index, seconds: frame.seconds } as const;
  },

  duration(): number | null {
    const first = transport.secondsAt(0), last = transport.secondsAt(frameCount() - 1);
    return first === null || last === null ? null : last - first;
  },

  async readBounds(context: Timebase, count: number, signal: AbortSignal) {
    await readTime(context, 0, signal);
    await readTime(context, count - 1, signal);
    signal.throwIfAborted();
  },

  // 读取路线后由运行服务登记帧序号与宿主帧、秒数的对应；cursor 同步补齐。
  setTimebase(runId: PlayerJobId, frames: ArchiveTimebase) {
    timebase = { runId, frames };
    if (getState().runId === runId && frameCount()) writeCursor(Math.min(getState().frameIndex, frameCount() - 1));
  },

  recordFrames(runId: PlayerJobId, offset: number, frames: ReadonlyArray<Pick<Frame, 'hostFrame' | 'seconds' | 'source'>>) {
    if (timebase?.runId !== runId) return;
    for (const [index, frame] of frames.entries())
      timebase.frames.set(offset + index, { hostFrame: frame.hostFrame, seconds: frame.seconds, source: frame.source });
    if (frameCount()) writeCursor(Math.min(getState().frameIndex, frameCount() - 1));
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
    useWorkbench.setState({ targetPosition: null, pendingSeek: null });
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
        else {
          const previous = transport.action('previous');
          if (previous?.available && previous.target !== null) seekPlayer(previous.target);
        }
        break;
    }
  },

  seekRecord(frame: number) {
    timeSeek?.abort();
    stopReview();
    moveCursor(frame);
  },

  seekPosition(position: PreviewPosition) {
    timeSeek?.abort();
    seekPlayer(position);
  },

  home() {
    if (transportSource() === 'player-live') {
      const first = transport.action('first');
      if (first?.available && first.target !== null) seekPlayer(first.target);
    } else if (transportSource() === 'sealed') transport.seekRecord(0);
  },
  end() {
    if (transportSource() === 'player-live') {
      const last = transport.action('last');
      if (last?.available && last.target !== null) seekPlayer(last.target);
    } else if (transportSource() === 'sealed') transport.seekRecord(frameCount() - 1);
  },

};

// 运行或场景切换时停止回看。
useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) {
    stopReview();
    if (timebase && timebase.runId !== state.runId) timebase = null;
  }
});
