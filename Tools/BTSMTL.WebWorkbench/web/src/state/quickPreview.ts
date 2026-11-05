import { previewApi } from '../api/preview';
import type {
  PreviewQuickDescription, PreviewQuickDescriptionReply, PreviewQuickInputOverlay, PreviewQuickRequest,
  PreviewQuickStatusReply, PreviewQuickTarget, PreviewQuickPlan, PreviewQuickJobId,
} from '../api/previewQuickRecords';
import { authoringActions, useAuthoring } from './authoring';
import { consoleActions, getState, selectedScene, useWorkbench } from './store';

type Description = { kind: 'idle' } | { kind: 'loading' } | { kind: 'stale'; reason: string }
  | { kind: 'ready'; reply: PreviewQuickDescriptionReply & { data: PreviewQuickDescription } }
  | { kind: 'failed'; reason: string; reply: PreviewQuickDescriptionReply | null };
type ActiveReply = PreviewQuickStatusReply & { data: NonNullable<PreviewQuickStatusReply['data']> };
type Job = { kind: 'idle' }
  | { kind: 'starting' | 'starting-cancelled'; request: PreviewQuickRequest }
  | { kind: 'running' | 'cancelling'; request: PreviewQuickRequest; reply: ActiveReply }
  | { kind: 'settled'; request: PreviewQuickRequest; reply: PreviewQuickStatusReply }
  | { kind: 'failed' | 'cancel-failed'; request: PreviewQuickRequest; reply: ActiveReply | null; reason: string };

export interface QuickPreviewState {
  target: PreviewQuickTarget | null;
  description: Description;
  scheme: number | null;
  job: Job;
  accepted: { request: PreviewQuickRequest; jobId: PreviewQuickJobId; plan: PreviewQuickPlan } | null;
  seconds: number;
  playback: { origin: number; startedAt: number } | null;
  outputDirectory: string;
}

let descriptionController: AbortController | null = null;
let jobController: AbortController | null = null;
let pollTimer: ReturnType<typeof setTimeout> | null = null;
let playbackFrame: number | null = null;
const current = () => getState().quickPreview;
const set = (patch: Partial<QuickPreviewState>) => useWorkbench.setState(state => ({ quickPreview: { ...state.quickPreview, ...patch } }));
export function quickTargetKey(target: PreviewQuickTarget | null) {
  const root = target?.root;
  switch (root?.kind) {
    case 'webEditingView': return JSON.stringify([target?.scenePath, root.kind, root.skill, root.machine]);
    case 'nativeDocument': return JSON.stringify([target?.scenePath, root.kind, root.path, root.machine]);
    case 'timelineAsset': return JSON.stringify([target?.scenePath, root.kind, root.path, root.elementId]);
    default: return JSON.stringify([target?.scenePath]);
  }
}

function selectedSource(workbench = getState()) {
  const sources = workbench.sources;
  const selection = workbench.contentSelection;
  if (sources === null || selection === null) return { kind: 'unavailable', reason: '先在顶部选择正式场景或 Timeline' } as const;
  if (selection.kind === 'timeline') {
    const timeline = sources.timelines.find(item => item.content.package === selection.package);
    return timeline === undefined ? { kind: 'unavailable', reason: '所选 Timeline 发布记录已不在来源目录中' } as const
      : { kind: 'timeline', timeline } as const;
  }
  const publication = selectedScene(workbench);
  if (publication === undefined) return { kind: 'unavailable', reason: '所选场景发布记录已不在来源目录中' } as const;
  const scene = sources.quickScenes.find(item => item.publication.path === publication.path && item.publication.revision === publication.revision);
  if (scene === undefined) return { kind: 'unavailable', reason: '所选场景发布记录没有对应修订的 Unity 场景资产身份，请重新读取来源目录' } as const;
  const target = useAuthoring.getState().target;
  if (target?.kind !== 'skill') return { kind: 'unavailable', reason: '先在顶部选择本次推导的技能' } as const;
  return { kind: 'skill', scene, skill: target.name } as const;
}

function selectionKey(workbench = getState()) {
  const source = selectedSource(workbench);
  return JSON.stringify(source.kind === 'timeline' ? [source.kind, source.timeline.content, source.timeline.scene]
    : source.kind === 'skill' ? [source.kind, source.scene, source.skill] : source);
}
const matchesEditingView = (target: PreviewQuickTarget) => target.root?.kind !== 'webEditingView' ||
  JSON.stringify(target.root.editingView) === JSON.stringify(authoringActions.editingView());

function sourceUnavailableReason(): string | null {
  const workbench = getState();
  if (workbench.previewMode !== 'quick') return '先选择快预览档位';
  const mode = workbench.host?.modes.find(item => item.id === 'quick');
  if (!mode?.available) return mode?.reason ?? '宿主尚未声明快预览能力';
  const source = selectedSource(workbench);
  return source.kind === 'unavailable' ? source.reason : null;
}

function selectedRequest(request: PreviewQuickRequest): boolean {
  const state = current();
  if (getState().previewMode !== 'quick' || state.description.kind !== 'ready') return false;
  if ((state.job.kind === 'starting-cancelled' || state.job.kind === 'cancelling' || state.job.kind === 'cancel-failed') && state.job.request === request) return false;
  const description = state.description.reply.data;
  const source = description.source;
  if (source === null) return false;
  const scheme = description.root?.inputSchemes.find(item => item.index === state.scheme);
  return JSON.stringify(state.target) === JSON.stringify(request.target) && matchesEditingView(request.target) && source.revision === request.sourceRevision &&
    scheme?.index === request.input.scheme && scheme.revision === request.input.revision &&
    description.scenePath === request.target.scenePath;
}

function ownsRequest(request: PreviewQuickRequest) {
  const job = current().job;
  return job.kind !== 'idle' && job.request === request;
}

function stopPoll() {
  if (pollTimer !== null) clearTimeout(pollTimer);
  pollTimer = null;
  jobController?.abort();
  jobController = null;
}

function schedulePoll(request: PreviewQuickRequest) {
  pollTimer = setTimeout(() => { pollTimer = null; void readJob(request, false); }, 500);
}

function accept(request: PreviewQuickRequest, reply: PreviewQuickStatusReply, cancelling: boolean) {
  if (!ownsRequest(request)) return;
  const status = reply.data;
  if (status?.plan && (status.source?.revision !== request.sourceRevision ||
    status.plan.inputScheme !== request.input.scheme || status.plan.inputSchemeRevision !== request.input.revision)) {
    const reason = '宿主计划的来源或输入方案修订与本次请求不一致，未采用该路线';
    set({ job: { kind: cancelling ? 'cancel-failed' : 'failed', request, reply: { ...reply, data: status }, reason } });
    consoleActions.log('error', '快预览', reason, reply.rawJson);
    return;
  }
  if (status !== null && !status.completed && reply.pending) {
    set({ job: { kind: cancelling || status.cancellationRequested ? 'cancelling' : 'running', request, reply: { ...reply, data: status } } });
    if (!selectedRequest(request) && !cancelling && !status.cancellationRequested) void quickPreview.cancel();
    else schedulePoll(request);
  } else if (status !== null && !status.completed) {
    set({ job: { kind: cancelling ? 'cancel-failed' : 'failed', request, reply: { ...reply, data: status }, reason: reply.message ?? status.error ?? '作业未完成，状态读取未获推进回执' } });
  } else {
    const selected = selectedRequest(request) && !cancelling && !status?.cancellationRequested;
    set({ job: selected ? { kind: 'settled', request, reply } : { kind: 'idle' },
      ...(selected && reply.success && status?.plan ? {
        accepted: { request, jobId: status.jobId, plan: status.plan }, seconds: status.plan.clock.origin,
      } : {}),
    });
  }
  if (!reply.success) consoleActions.log('error', '快预览', `${reply.code ?? ''} ${reply.message ?? status?.error ?? '快预览失败'}`.trim(), reply.rawJson);
}

async function readJob(request: PreviewQuickRequest, cancel: boolean) {
  const job = current().job;
  if (!ownsRequest(request) || (job.kind !== 'running' && job.kind !== 'cancelling' && job.kind !== 'failed' && job.kind !== 'cancel-failed') || job.reply === null) return;
  const receipt = job.reply;
  const status = receipt.data;
  stopPoll();
  const controller = jobController = new AbortController();
  const cancelling = cancel || job.kind === 'cancelling' || job.kind === 'cancel-failed';
  if (cancel) set({ job: { kind: 'cancelling', request, reply: receipt } });
  try {
    const reply = await (cancel ? previewApi.cancelQuick(status.jobId, controller.signal) : previewApi.quickStatus(status.jobId, controller.signal));
    if (controller.signal.aborted) return;
    accept(request, reply, cancelling);
  } catch (error) {
    if (controller.signal.aborted || !ownsRequest(request)) return;
    consoleActions.error('快预览', error);
    set({ job: { kind: cancelling ? 'cancel-failed' : 'failed', request, reply: receipt, reason: error instanceof Error ? error.message : String(error) } });
  }
}

function invalidateJob() {
  const job = current().job;
  if (job.kind === 'idle') return;
  if (job.kind === 'settled' || ((job.kind === 'failed' || job.kind === 'cancel-failed') && (job.reply === null || job.reply.data.completed))) set({ job: { kind: 'idle' } });
  else void quickPreview.cancel();
}

export function quickPlan() {
  return current().accepted?.plan ?? null;
}

export function quickPlanStale() {
  const state = current();
  return state.accepted !== null && (!selectedRequest(state.accepted.request) ||
    state.job.kind !== 'settled' || state.job.request !== state.accepted.request);
}

export function quickBounds(plan: PreviewQuickPlan) {
  return plan.segments.reduce((range, segment) => ({ first: Math.min(range.first, segment.start), last: Math.max(range.last, segment.end) }),
    { first: plan.clock.origin, last: plan.clock.origin + plan.duration });
}

function playbackReason() {
  return quickPlan() === null ? '先推导路线' : quickPlanStale() ? '路线已过期，请按当前内容与输入重新推导' : null;
}

function advancePlayback(now: number) {
  playbackFrame = null;
  const playback = current().playback;
  const plan = quickPlan();
  if (playback === null || plan === null) return;
  const end = quickBounds(plan).last;
  const seconds = Math.min(end, playback.origin + (now - playback.startedAt) / 1000);
  set({ seconds });
  if (seconds === end) quickPreview.pause();
  else playbackFrame = requestAnimationFrame(advancePlayback);
}

function preparation() {
  const workbench = getState();
  const mode = workbench.host?.modes.find(item => item.id === 'quick');
  if (workbench.previewMode !== 'quick') return { kind: 'unavailable', reason: '先选择快预览档位' } as const;
  if (!mode?.available) return { kind: 'unavailable', reason: mode?.reason ?? '宿主尚未声明快预览能力' } as const;
  const state = workbench.quickPreview;
  if (state.description.kind !== 'ready') return { kind: 'unavailable', reason: state.description.kind === 'failed' || state.description.kind === 'stale'
    ? state.description.reason : state.description.kind === 'loading' ? '正在读取来源与输入方案' : '先读取快预览来源' } as const;
  const target = state.target;
  if (target === null || target.root === undefined) return { kind: 'unavailable', reason: '选择本次推导的技能根或独立 Timeline' } as const;
  const data = state.description.reply.data;
  if (data.source === null || data.root === null) return { kind: 'unavailable', reason: '宿主没有返回当前根的来源与输入方案' } as const;
  if (!matchesEditingView(target)) return { kind: 'unavailable', reason: '网页编辑内容已变化，请重新读取来源' } as const;
  const scheme = data.root.inputSchemes.find(item => item.index === state.scheme);
  if (scheme === undefined) return { kind: 'unavailable', reason: '选择宿主提供的输入方案' } as const;
  if (state.job.kind === 'starting' || state.job.kind === 'starting-cancelled' || state.job.kind === 'running' ||
    state.job.kind === 'cancelling' || ((state.job.kind === 'failed' || state.job.kind === 'cancel-failed') && state.job.reply !== null && !state.job.reply.data.completed))
    return { kind: 'unavailable', reason: state.job.kind === 'cancelling' || state.job.kind === 'starting-cancelled'
      ? '正在取消原作业，等待清理结束' : '原作业尚未结束，请先完成或取消' } as const;
  const directory = state.outputDirectory || workbench.sources?.runDirectory;
  if (!directory || !/^D:[\\/]/i.test(directory)) return { kind: 'unavailable', reason: '快预览输出目录须明确位于 D 盘' } as const;
  return { kind: 'ready', target: { ...target, root: target.root }, source: data.source, scheme, directory } as const;
}

export const quickPreview = {
  sourceUnavailableReason,
  playbackReason,
  pause() {
    if (playbackFrame !== null) cancelAnimationFrame(playbackFrame);
    playbackFrame = null;
    set({ playback: null });
  },
  toggle() {
    if (current().playback !== null) { quickPreview.pause(); return; }
    if (playbackReason() !== null) return;
    const plan = quickPlan()!;
    const bounds = quickBounds(plan);
    const origin = current().seconds >= bounds.last ? bounds.first : current().seconds;
    set({ seconds: origin, playback: { origin, startedAt: performance.now() } });
    playbackFrame = requestAnimationFrame(advancePlayback);
  },
  step(direction: -1 | 1) {
    if (playbackReason() !== null) return;
    quickPreview.pause();
    const plan = quickPlan()!;
    const bounds = quickBounds(plan);
    const boundaries = [...new Set([bounds.first, bounds.last, ...plan.segments.flatMap(segment => [segment.start, segment.end])])].sort((a, b) => a - b);
    const seconds = current().seconds;
    const target = direction < 0 ? boundaries.filter(time => time < seconds).at(-1) : boundaries.find(time => time > seconds);
    if (target !== undefined) quickPreview.seek(target);
  },
  async readSource(machine?: number) {
    const reason = sourceUnavailableReason();
    const source = selectedSource();
    if (reason !== null || source.kind === 'unavailable') return;
    descriptionController?.abort();
    const controller = descriptionController = new AbortController();
    const scope = selectionKey();
    const previousRoot = current().target?.root;
    let target: PreviewQuickTarget = source.kind === 'skill' ? {
      scenePath: source.scene.scenePath,
      root: { kind: 'webEditingView', skill: source.skill, editingView: authoringActions.editingView(),
        machine: machine ?? (previousRoot?.kind === 'webEditingView' && previousRoot.skill === source.skill ? previousRoot.machine : 0) },
    } : { scenePath: source.timeline.scene.path,
      ...(previousRoot?.kind === 'timelineAsset' && previousRoot.elementId === source.timeline.content.timeline ? { root: previousRoot } : {}),
    };
    if (quickTargetKey(target) !== quickTargetKey(current().target)) set({ scheme: null, accepted: null, seconds: 0 });
    set({ target, description: { kind: 'loading' } });
    invalidateJob();
    try {
      if (source.kind === 'timeline') {
        const catalog = await previewApi.describeQuick({ scenePath: target.scenePath }, controller.signal);
        if (controller.signal.aborted || scope !== selectionKey()) return;
        if (!catalog.success || catalog.data === null) {
          set({ description: { kind: 'failed', reply: catalog, reason: catalog.message ?? catalog.code ?? '读取场景 Timeline 目录失败' }, scheme: null });
          consoleActions.log('error', '快预览', catalog.message ?? catalog.code ?? '读取场景 Timeline 目录失败', catalog.rawJson);
          return;
        }
        const matches = catalog.data.timelines.filter(item => item.elementId === source.timeline.content.timeline);
        if (matches.length !== 1) {
          set({ description: { kind: 'failed', reply: catalog, reason: `场景授权的独立 Timeline 中无法唯一定位根 ${source.timeline.content.timeline}` }, scheme: null });
          return;
        }
        target = { scenePath: target.scenePath, root: { kind: 'timelineAsset', path: matches[0].path, elementId: matches[0].elementId } };
        if (quickTargetKey(target) !== quickTargetKey(current().target)) set({ scheme: null, accepted: null, seconds: 0 });
        set({ target });
      }
      const reply = await previewApi.describeQuick(target, controller.signal);
      if (controller.signal.aborted) return;
      if (scope !== selectionKey() || !matchesEditingView(target)) { quickPreview.invalidate('来源或网页编辑内容已变化，请重新读取'); return; }
      if (!reply.success || reply.data === null) {
        set({ description: { kind: 'failed', reply, reason: reply.message ?? reply.code ?? '读取快预览来源失败' }, scheme: null });
        consoleActions.log('error', '快预览', reply.message ?? reply.code ?? '读取快预览来源失败', reply.rawJson);
        invalidateJob();
        return;
      }
      const data = reply.data;
      const previous = current().scheme;
      const scheme = data.root?.inputSchemes.find(item => item.index === previous)
        ?? data.root?.inputSchemes.find(item => item.index === data.root?.currentInputScheme);
      set({ description: { kind: 'ready', reply: { ...reply, data } }, scheme: scheme?.index ?? null });
      const job = current().job;
      if (job.kind !== 'idle' && !selectedRequest(job.request)) invalidateJob();
    } catch (error) {
      if (controller.signal.aborted) return;
      consoleActions.error('快预览', error);
      set({ description: { kind: 'failed', reply: null, reason: error instanceof Error ? error.message : String(error) }, scheme: null });
      invalidateJob();
    }
  },
  selectScheme(scheme: number) {
    set({ scheme, accepted: null, seconds: 0 });
    invalidateJob();
  },
  outputDirectory(outputDirectory: string) { set({ outputDirectory }); },
  unavailableReason(): string | null {
    const result = preparation();
    return result.kind === 'ready' ? null : result.reason;
  },
  async start(input: PreviewQuickInputOverlay = {}, rangeEndSeconds?: number) {
    const prepared = preparation();
    if (prepared.kind === 'unavailable') throw new Error(prepared.reason);
    const request: PreviewQuickRequest = {
      mode: 'quick', target: prepared.target, sourceRevision: prepared.source.revision,
      input: { scheme: prepared.scheme.index, revision: prepared.scheme.revision, ...input },
      outputDirectory: `${prepared.directory.replace(/[\\/]$/, '')}/quick-preview/${crypto.randomUUID()}`,
      ...(rangeEndSeconds === undefined ? {} : { rangeEndSeconds }),
    };
    stopPoll();
    set({ job: { kind: 'starting', request } });
    try {
      const reply = await previewApi.planQuick(request);
      if (!ownsRequest(request)) return;
      const cancelling = current().job.kind === 'starting-cancelled' || !selectedRequest(request);
      accept(request, reply, cancelling);
      if (cancelling && (current().job.kind === 'running' || current().job.kind === 'cancelling')) await readJob(request, true);
    } catch (error) {
      if (!ownsRequest(request)) return;
      consoleActions.error('快预览', error);
      set({ job: { kind: current().job.kind === 'starting-cancelled' ? 'cancel-failed' : 'failed', request, reply: null, reason: error instanceof Error ? error.message : String(error) } });
    }
  },
  async cancel() {
    const job = current().job;
    if (job.kind === 'starting') { set({ job: { kind: 'starting-cancelled', request: job.request } }); return; }
    if (job.kind === 'running' || job.kind === 'failed' || job.kind === 'cancel-failed') await readJob(job.request, true);
  },
  async readStatus() {
    const job = current().job;
    if (job.kind === 'failed' && job.reply?.data) await readJob(job.request, false);
  },
  seek(seconds: number) {
    const plan = quickPlan();
    if (plan === null) return;
    const bounds = quickBounds(plan);
    const value = Math.max(bounds.first, Math.min(bounds.last, seconds));
    set({ seconds: value, playback: current().playback === null ? null : { origin: value, startedAt: performance.now() } });
  },
  inputChanged() { invalidateJob(); },
  invalidate(reason: string) {
    descriptionController?.abort();
    set({ description: { kind: 'stale', reason } });
    invalidateJob();
  },
  clearTarget(reason: string) {
    set({ target: null, accepted: null, seconds: 0 });
    quickPreview.invalidate(reason);
  },
};

useWorkbench.subscribe((state, previous) => {
  if (state.quickPreview.playback !== null && quickPlanStale()) quickPreview.pause();
  const contentChanged = selectionKey(state) !== selectionKey(previous);
  if (state.previewMode !== previous.previewMode || contentChanged) {
    if (state.quickPreview.target !== null) {
      if (contentChanged) quickPreview.clearTarget('场景来源已变化，请重新读取');
      else quickPreview.invalidate('预览档位或场景来源已变化，请重新读取');
    }
  }
});

useAuthoring.subscribe((authoring, previous) => {
  if ((authoring.target?.kind !== previous.target?.kind || authoring.target?.name !== previous.target?.name) &&
    getState().previewMode === 'quick' && getState().contentSelection?.kind === 'scene') {
    quickPreview.clearTarget('作者根已变化，请读取当前技能的快预览来源');
    return;
  }
  const state = current();
  if (state.target?.root?.kind !== 'webEditingView' || (state.description.kind !== 'ready' && state.description.kind !== 'loading')) return;
  const documentsChanged = Object.keys(authoring.documents).filter(key => key.startsWith('skill/')).some(key => {
    const before = previous.documents[key];
    const after = authoring.documents[key];
    return before === undefined || before.files !== after.files || before.revision !== after.revision || before.sourceRevision !== after.sourceRevision;
  });
  if (documentsChanged || !matchesEditingView(state.target)) quickPreview.invalidate('网页编辑内容或作者来源已变化，请重新读取');
});
