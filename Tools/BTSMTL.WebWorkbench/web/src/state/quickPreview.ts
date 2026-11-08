import { previewApi } from '../api/preview';
import type {
  PreviewQuickDescription, PreviewQuickDescriptionReply, PreviewQuickRequest,
  PreviewQuickStatusReply, PreviewQuickTarget, PreviewQuickPlan, PreviewQuickJobId,
  PreviewQuickAuthoringTarget, PreviewQuickInputSource,
  PreviewQuickFrame, PreviewQuickSeekRequest,
} from '../api/previewQuickRecords';
import { authoringTargetKey } from '../api/authoring';
import { authoringActions, currentDocument, useAuthoring } from './authoring';
import type { JsonObject } from './skillJson';
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
type QuickPlayback = { origin: number; startedAt: number };

export interface QuickPreviewState {
  target: PreviewQuickTarget | null;
  description: Description;
  scheme: PreviewQuickInputSource | null;
  job: Job;
  accepted: { request: PreviewQuickRequest; jobId: PreviewQuickJobId; plan: PreviewQuickPlan; sourceRevision: string } | null;
  // Host 静态推导的路线：作业状态一带计划就采用，不等播放器画面；画面准备失败时仍显示输入推出的 Timeline。
  route: { request: PreviewQuickRequest; plan: PreviewQuickPlan } | null;
  seconds: number;
  playback: QuickPlayback | null;
  picture: { frame: { value: PreviewQuickFrame; playback: QuickPlayback | null } | null; request: { kind: 'idle' } | { kind: 'pending'; target: PreviewQuickSeekRequest }
    | { kind: 'failed'; reason: string } };
  outputDirectory: string;
}

export const quickJobCancellable = (job: Job) => job.kind === 'starting' || job.kind === 'running' ||
  (job.kind === 'settled' && job.reply.data?.stage === 'ready') ||
  ((job.kind === 'failed' || job.kind === 'cancel-failed') && job.reply !== null &&
    (!job.reply.data.completed || job.reply.data.stage === 'ready' || job.reply.data.cleanupErrors.length > 0));

const quickStageText: Record<string, string> = { planning: '正在生成', loading: '正在加载画面', ready: '画面就绪' };

export function quickJobProgress(job: Job): string | null {
  switch (job.kind) {
    case 'starting': return '正在提交';
    case 'starting-cancelled': return '等待作业身份以取消';
    case 'running': return `${quickStageText[job.reply.data.stage] ?? job.reply.data.stage} · ${job.reply.data.samplesDone}/${job.reply.data.samplesTotal}`;
    case 'cancelling': return `取消与清理中 · ${job.reply.data.samplesDone}/${job.reply.data.samplesTotal}`;
    case 'idle':
    case 'settled':
    case 'failed':
    case 'cancel-failed': return null;
    default: { const unhandled: never = job; return unhandled; }
  }
}

// 作业失败的原因：本地失败或宿主已完成但回执失败（如播放器拒绝载入）。
export function quickJobFailure(job: Job): string | null {
  if (job.kind === 'failed' || job.kind === 'cancel-failed') return job.reason;
  if (job.kind !== 'settled' || job.reply.success) return null;
  return job.reply.message ?? job.reply.data?.error ?? job.reply.code ?? '快预览失败';
}

let descriptionController: AbortController | null = null;
let jobController: AbortController | null = null;
let pollTimer: ReturnType<typeof setTimeout> | null = null;
let playbackFrame: number | null = null;
type Accepted = NonNullable<QuickPreviewState['accepted']>;
interface PictureRequest {
  accepted: Accepted;
  target: PreviewQuickSeekRequest;
  playback: QuickPreviewState['playback'];
  controller: AbortController;
  timer: ReturnType<typeof setTimeout> | null;
}
let pictureRequest: PictureRequest | null = null;
const current = () => getState().quickPreview;
const set = (patch: Partial<QuickPreviewState>) => useWorkbench.setState(state => ({ quickPreview: { ...state.quickPreview, ...patch } }));
export function quickTargetKey(target: PreviewQuickTarget | null) {
  const root = target?.root;
  const publication = target?.scenePublication;
  return JSON.stringify([target?.scenePath, publication?.path, publication?.revision,
    root?.kind, root == null ? null : quickAuthoringKey(root.authoring)]);
}

export const quickAuthoringKey = (target: PreviewQuickAuthoringTarget) => JSON.stringify([target.documentPath, target.entry]);

// 快档跟随当前内容：来源过期即重读，来源与方案就绪后按同一来源与方案修订推导一次；失败不自动重试同一身份。
let followTimer: ReturnType<typeof setTimeout> | null = null;
let autoAttempt: string | null = null;
const requestKey = (request: Pick<PreviewQuickRequest, 'target' | 'sourceRevision' | 'input'>) =>
  JSON.stringify([quickTargetKey(request.target), request.sourceRevision, quickInputSourceKey(request.input.source), request.input.revision]);

// 已读来源后网页编辑内容变化：不再先读来源，直接按当前内容推导一次，来源描述随作业状态交回。
let contentFollow = false;

function followContent() {
  followTimer = null;
  if (sourceUnavailableReason() !== null) return;
  const description = current().description.kind;
  if (description === 'idle' || description === 'stale') { contentFollow = false; void quickPreview.readSource(); return; }
  if (contentFollow) {
    const job = current().job.kind;
    // 原作业正在提交或清理：作业状态变化会再排一次跟随。
    if (description !== 'ready' || job === 'starting' || job === 'starting-cancelled' || job === 'cancelling') return;
    contentFollow = false;
    void quickPreview.start(true).catch(error => consoleActions.error('快预览', error));
    return;
  }
  const prepared = preparation();
  if (prepared.kind !== 'ready' || requestKey({ target: prepared.target, sourceRevision: prepared.source.revision,
    input: { source: prepared.scheme.source, revision: prepared.scheme.revision } }) === autoAttempt) return;
  void quickPreview.start().catch(error => consoleActions.error('快预览', error));
}

function scheduleFollow() {
  if (followTimer !== null) clearTimeout(followTimer);
  followTimer = setTimeout(followContent, 200);
}

export function quickInputSourceKey(source: PreviewQuickInputSource | null): string {
  return source?.kind === 'documentInput'
    ? JSON.stringify([source.kind, source.documentPath, source.inputId, source.schemeId])
    : JSON.stringify(source?.kind ?? null);
}

// 默认入口：独立 Timeline 取所选 Timeline；技能文档取 skill.json 声明的能力根图，文档只有一个入口时直接取它。
function defaultRoot(catalog: PreviewQuickDescription): PreviewQuickAuthoringTarget | null {
  const source = selectedSource();
  if (source.kind === 'unavailable') return null;
  const active = useAuthoring.getState().target;
  const choices = catalog.documents.flatMap(document => document.entries.map(choice => ({ document, choice })));
  const ability = (currentDocument('skill.json') as JsonObject | null)?.m_AbilityGraph;
  const owned = source.kind === 'timeline'
    ? choices.filter(item => item.choice.entry.kind === 'timeline' && item.choice.entry.timelineId === source.timeline.content.timeline)
    : choices.filter(item => active !== null && item.document.documentPath === authoringTargetKey(active));
  const rooted = owned.filter(item => item.choice.entry.kind === 'graph' && item.choice.entry.graphId === ability);
  const matches = owned.length === 1 ? owned : rooted.slice(0, 1);
  return matches.length !== 1 ? null
    : { documentPath: matches[0].document.documentPath, savedRevision: matches[0].document.savedRevision, entry: matches[0].choice.entry };
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
  return { kind: 'scene', scene } as const;
}

function selectionKey(workbench = getState()) {
  const source = selectedSource(workbench);
  return JSON.stringify(source.kind === 'timeline' ? [source.kind, source.timeline.content, source.timeline.scene]
    : source.kind === 'scene' ? [source.kind, source.scene] : source);
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
  const scheme = description.root?.inputSchemes.find(item => quickInputSourceKey(item.source) === quickInputSourceKey(state.scheme));
  // 跟随当前内容的请求在宿主交回修订前只核对目标、编辑内容与方案身份。
  const following = request.sourceRevision === null;
  return state.target?.root === request.target.root &&
    (following || source.revision === request.sourceRevision && JSON.stringify(source.authoring) === JSON.stringify(request.target.root.authoring)) &&
    scheme !== undefined && quickInputSourceKey(scheme.source) === quickInputSourceKey(request.input.source) &&
    (following || scheme.revision === request.input.revision) && description.scenePath === request.target.scenePath;
}

// 跟随作业带回计划后换成带实际修订的请求继续，网页来源描述同步为宿主同一次捕获的结果。
function resolveFollow(request: PreviewQuickRequest, reply: PreviewQuickStatusReply): PreviewQuickRequest {
  const status = reply.data;
  if (request.sourceRevision !== null || !status?.plan || status.source === null || status.description === null) return request;
  const resolved: PreviewQuickRequest = { ...request, sourceRevision: status.source.revision,
    input: { source: status.plan.input, revision: status.plan.inputSchemeRevision } };
  const selected = selectedRequest(request);
  const job = current().job;
  if (job.kind !== 'idle') set({ job: { ...job, request: resolved } as Job });
  if (selected) {
    autoAttempt = requestKey(resolved);
    set({ description: { kind: 'ready', reply: { success: true, pending: false, code: null, message: null, data: status.description, rawJson: reply.rawJson } },
      scheme: status.plan.input });
  }
  return resolved;
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
  // 状态读取由宿主等到阶段变化再答复，答复后立即接着读。
  pollTimer = setTimeout(() => { pollTimer = null; void readJob(request, false); }, 0);
}

function receiveFrame(frame: PreviewQuickFrame | null, accepted: Accepted, requestId: string, seconds: number) {
  if (frame === null) throw new Error('原快预览未返回本次时间请求的实际画面');
  if (frame.jobId !== accepted.jobId || frame.requestId !== requestId || frame.sourceRevision !== accepted.sourceRevision ||
    frame.inputRevision !== accepted.plan.inputRevision || frame.requestedSeconds !== seconds)
    throw new Error('快预览画面的作业、来源、输入或时间请求身份与当前请求不一致');
  return frame;
}

function clearPicture() {
  const request = pictureRequest;
  pictureRequest = null;
  if (request !== null) {
    if (request.timer !== null) clearTimeout(request.timer);
    request.controller.abort();
  }
  const picture = current().picture;
  if (picture.frame !== null || picture.request.kind !== 'idle') set({ picture: { frame: null, request: { kind: 'idle' } } });
}

function pictureRequestCurrent(request: PictureRequest) {
  const state = current();
  return state.accepted === request.accepted && quickPictureCurrent() &&
    (state.seconds === request.target.seconds || request.playback !== null && state.playback === request.playback);
}

function requestPicture(force = false) {
  const state = current();
  const accepted = state.accepted;
  if (accepted === null || !quickPictureCurrent()) { clearPicture(); return; }
  if (pictureRequest !== null || !force && state.picture.frame?.value.requestedSeconds === state.seconds) return;
  const target: PreviewQuickSeekRequest = { requestId: crypto.randomUUID(), sourceRevision: accepted.sourceRevision,
    inputRevision: accepted.plan.inputRevision, seconds: state.seconds };
  const request: PictureRequest = { accepted, target, playback: state.playback, controller: new AbortController(), timer: null };
  pictureRequest = request;
  set({ picture: { ...state.picture, request: { kind: 'pending', target } } });
  void readPicture(request, true);
}

async function readPicture(request: PictureRequest, submit: boolean) {
  if (pictureRequest !== request) return;
  if (!pictureRequestCurrent(request)) { pictureRequest = null; requestPicture(true); return; }
  try {
    const reply = await (submit ? previewApi.seekQuick(request.accepted.jobId, request.target, request.controller.signal)
      : previewApi.quickStatus(request.accepted.jobId, request.controller.signal));
    if (pictureRequest !== request || request.controller.signal.aborted) return;
    if (!pictureRequestCurrent(request)) { pictureRequest = null; requestPicture(true); return; }
    if (!reply.success) throw new Error(reply.message ?? reply.code ?? reply.data?.error ?? '快预览取帧失败');
    if (reply.pending) {
      request.timer = setTimeout(() => { request.timer = null; void readPicture(request, false); }, 500);
      return;
    }
    const frame = receiveFrame(reply.data === null ? null : reply.data.frame, request.accepted, request.target.requestId, request.target.seconds);
    set({ picture: { frame: { value: frame, playback: request.playback }, request: { kind: 'idle' } } });
    pictureRequest = null;
    requestPicture();
  } catch (error) {
    if (pictureRequest !== request || request.controller.signal.aborted) return;
    if (!pictureRequestCurrent(request)) { pictureRequest = null; requestPicture(true); return; }
    quickPreview.pause();
    const reason = error instanceof Error ? error.message : String(error);
    set({ picture: { frame: null, request: { kind: 'failed', reason } } });
    pictureRequest = null;
    consoleActions.error('快预览画面', error);
  }
}

function accept(request: PreviewQuickRequest, reply: PreviewQuickStatusReply, cancelling: boolean) {
  if (!ownsRequest(request)) return;
  if (contentFollow && !cancelling && current().target?.root !== request.target.root) {
    set({ job: { kind: 'idle' } });
    return;
  }
  request = resolveFollow(request, reply);
  const status = reply.data;
  if (status?.plan && (status.source?.revision !== request.sourceRevision ||
    JSON.stringify(status.plan.input) !== JSON.stringify(request.input.source) || status.plan.inputSchemeRevision !== request.input.revision)) {
    const reason = '宿主计划的来源或输入方案修订与本次请求不一致，未采用该结果';
    set({ job: { kind: cancelling ? 'cancel-failed' : 'failed', request, reply: { ...reply, data: status }, reason } });
    consoleActions.log('error', '快预览', reason, reply.rawJson);
    return;
  }
  if (status?.plan && selectedRequest(request) && current().route?.request !== request) set({ route: { request, plan: status.plan } });
  if (status !== null && !status.completed && reply.pending) {
    set({ job: { kind: cancelling || status.cancellationRequested ? 'cancelling' : 'running', request, reply: { ...reply, data: status } } });
    if (!selectedRequest(request) && !cancelling && !status.cancellationRequested) void quickPreview.cancel();
    else schedulePoll(request);
  } else if (status !== null && !status.completed) {
    set({ job: { kind: cancelling ? 'cancel-failed' : 'failed', request, reply: { ...reply, data: status }, reason: reply.message ?? status.error ?? '作业未完成，状态读取未获推进回执' } });
  } else {
    const selected = selectedRequest(request) && !cancelling && !status?.cancellationRequested;
    if (cancelling && status !== null && status.cleanupErrors.length > 0) {
      set({ job: { kind: 'cancel-failed', request, reply: { ...reply, data: status }, reason: status.cleanupErrors.join('\n') } });
    } else if (selected && reply.success && status?.plan && status.source !== null) {
      const accepted = { request, jobId: status.jobId, plan: status.plan, sourceRevision: status.source.revision };
      try {
        const frame = receiveFrame(status.frame, accepted, status.jobId, request.initialSeconds);
        set({ job: { kind: 'settled', request, reply }, accepted,
          picture: { frame: frame.requestedSeconds === current().seconds ? { value: frame, playback: null } : null, request: { kind: 'idle' } } });
      } catch (error) {
        const reason = error instanceof Error ? error.message : String(error);
        set({ job: { kind: 'failed', request, reply: { ...reply, data: status }, reason } });
        consoleActions.error('快预览画面', error);
      }
    } else set({ job: selected ? { kind: 'settled', request, reply } : { kind: 'idle' } });
  }
  if (!reply.success && !(cancelling && status?.stage === 'cancelled' && status.cleanupErrors.length === 0))
    consoleActions.log('error', '快预览', `${reply.code ?? ''} ${reply.message ?? status?.error ?? '快预览失败'}`.trim(), reply.rawJson);
  // 跟随作业在交回计划前失败（读取、编译或输入方案不再成立）：重新读取来源，由来源描述给出当前可选内容。
  const after = current().job;
  if (request.sourceRevision === null && !cancelling && after.kind !== 'idle' && after.request === request &&
    (after.kind === 'failed' || after.kind === 'settled' && !reply.success))
    quickPreview.invalidate(quickJobFailure(after) ?? '按当前内容生成失败，重新读取来源');
}

async function readJob(request: PreviewQuickRequest, cancel: boolean) {
  const job = current().job;
  if (!ownsRequest(request) || (job.kind !== 'running' && job.kind !== 'cancelling' && job.kind !== 'failed' && job.kind !== 'cancel-failed' && job.kind !== 'settled') || job.reply === null || job.reply.data === null) return;
  const receipt = { ...job.reply, data: job.reply.data };
  const status = receipt.data;
  stopPoll();
  const controller = jobController = new AbortController();
  const cancelling = cancel || job.kind === 'cancelling' || job.kind === 'cancel-failed';
  if (cancel) set({ job: { kind: 'cancelling', request, reply: receipt } });
  try {
    const reply = await (cancel ? previewApi.cancelQuick(status.jobId, controller.signal)
      : previewApi.quickStatus(status.jobId, controller.signal, status.stage));
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
  if (quickJobCancellable(job)) void quickPreview.cancel();
  else if (job.kind === 'settled' || job.kind === 'failed' || job.kind === 'cancel-failed') set({ job: { kind: 'idle' } });
}

export function quickPlan() {
  return current().route?.plan ?? null;
}

export function quickPlanStale() {
  const state = current();
  return state.route !== null && (getState().previewMode !== 'quick' || state.description.kind !== 'ready' ||
    state.target?.root !== state.route.request.target.root || state.job.kind === 'idle' || state.job.request !== state.route.request);
}

// 画面只认与当前路线同一请求、已拿到首帧的作业；路线先到而画面未到或失败时不显示旧画面。
export function quickPictureCurrent() {
  const state = current();
  return state.accepted !== null && !quickPlanStale() && state.accepted.request === state.route?.request;
}

export function quickBounds(plan: PreviewQuickPlan) {
  return plan.segments.reduce((range, segment) => ({ first: Math.min(range.first, segment.start), last: Math.max(range.last, segment.end) }),
    { first: plan.clock.origin, last: plan.clock.origin + plan.duration });
}

function playbackReason() {
  return quickPlan() === null ? '预览尚未生成' : quickPlanStale() ? '内容已变化，正在按当前内容与输入重新生成' : null;
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

// supersede：发起新作业时由宿主取消并等完原作业，原作业不挡本次准备。
function preparation(supersede = false) {
  const workbench = getState();
  const mode = workbench.host?.modes.find(item => item.id === 'quick');
  if (workbench.previewMode !== 'quick') return { kind: 'unavailable', reason: '先选择快预览档位' } as const;
  if (!mode?.available) return { kind: 'unavailable', reason: mode?.reason ?? '宿主尚未声明快预览能力' } as const;
  const state = workbench.quickPreview;
  if (state.description.kind !== 'ready') return { kind: 'unavailable', reason: state.description.kind === 'failed' || state.description.kind === 'stale'
    ? state.description.reason : state.description.kind === 'loading' ? '正在读取来源与输入方案' : '先读取快预览来源' } as const;
  const target = state.target;
  if (target === null || target.root === null) return { kind: 'unavailable', reason: '选择作者文档和入口图或 Timeline' } as const;
  const data = state.description.reply.data;
  if (data.source === null || data.root === null) return { kind: 'unavailable', reason: '宿主没有返回当前根的来源与输入方案' } as const;
  const scheme = data.root.inputSchemes.find(item => quickInputSourceKey(item.source) === quickInputSourceKey(state.scheme));
  if (scheme === undefined) return { kind: 'unavailable', reason: '选择宿主提供的输入方案' } as const;
  if (!supersede && (state.job.kind === 'starting' || state.job.kind === 'starting-cancelled' || state.job.kind === 'running' ||
    state.job.kind === 'cancelling' || ((state.job.kind === 'failed' || state.job.kind === 'cancel-failed') && quickJobCancellable(state.job))))
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
    const plan = quickPlan();
    if (plan === null || quickPlanStale()) return;
    const bounds = quickBounds(plan);
    const origin = current().seconds >= bounds.last ? bounds.first : current().seconds;
    set({ seconds: origin, playback: { origin, startedAt: performance.now() } });
    playbackFrame = requestAnimationFrame(advancePlayback);
  },
  step(direction: -1 | 1) {
    const plan = quickPlan();
    if (plan === null || quickPlanStale()) return;
    quickPreview.pause();
    const bounds = quickBounds(plan);
    const boundaries = [...new Set([bounds.first, bounds.last, ...plan.segments.flatMap(segment => [segment.start, segment.end])])].sort((a, b) => a - b);
    const seconds = current().seconds;
    const target = direction < 0 ? boundaries.filter(time => time < seconds).at(-1) : boundaries.find(time => time > seconds);
    if (target !== undefined) quickPreview.seek(target);
  },
  async readSource(authoring?: PreviewQuickAuthoringTarget) {
    const reason = sourceUnavailableReason();
    const source = selectedSource();
    if (reason !== null || source.kind === 'unavailable') return;
    descriptionController?.abort();
    const controller = descriptionController = new AbortController();
    const scope = selectionKey();
    const previousRoot = current().target?.root;
    let chosen = authoring ?? previousRoot?.authoring ?? null;
    let target: PreviewQuickTarget = source.kind === 'scene'
      ? { scenePath: source.scene.scenePath, scenePublication: source.scene.publication, root: null }
      : { scenePath: source.timeline.scene.path, root: null };
    set({ description: { kind: 'loading' } });
    invalidateJob();
    try {
      if (chosen === null) {
        const catalog = await previewApi.describeQuick(target, controller.signal);
        if (controller.signal.aborted || scope !== selectionKey()) return;
        if (!catalog.success || catalog.data === null) {
          set({ description: { kind: 'failed', reply: catalog, reason: catalog.message ?? catalog.code ?? '读取场景作者文档目录失败' } });
          consoleActions.log('error', '快预览', catalog.message ?? catalog.code ?? '读取场景作者文档目录失败', catalog.rawJson);
          return;
        }
        chosen = defaultRoot(catalog.data);
        if (chosen === null) {
          // 技能 skill.json 尚未载入时选不出能力根；载入后由作者订阅按这份目录补选，不重读目录。
          set({ target, description: { kind: 'ready', reply: { ...catalog, data: catalog.data } }, scheme: null });
          return;
        }
      }
      const document = useAuthoring.getState().documents[chosen.documentPath];
      target = { ...target, root: document === undefined
        ? { kind: 'authoringDocument', authoring: chosen }
        : { kind: 'webEditingView', authoring: { ...chosen, savedRevision: document.revision ?? '' }, editingView: authoringActions.editingView() } };
      if (quickTargetKey(target) !== quickTargetKey(current().target)) set({ scheme: null, accepted: null, route: null, seconds: 0 });
      set({ target });
      const reply = await previewApi.describeQuick(target, controller.signal);
      if (controller.signal.aborted) return;
      if (scope !== selectionKey() || !matchesEditingView(target)) { quickPreview.invalidate('来源或网页编辑内容已变化，请重新读取'); return; }
      if (!reply.success || reply.data === null) {
        set({ description: { kind: 'failed', reply, reason: reply.message ?? reply.code ?? '读取快预览来源失败' } });
        consoleActions.log('error', '快预览', reply.message ?? reply.code ?? '读取快预览来源失败', reply.rawJson);
        invalidateJob();
        return;
      }
      const data = reply.data;
      const previous = current().scheme;
      const selected = previous ?? data.root?.currentInputScheme ?? null;
      const offered = data.root?.inputSchemes.find(item => quickInputSourceKey(item.source) === quickInputSourceKey(selected));
      set({ description: { kind: 'ready', reply: { ...reply, data } }, scheme: offered?.source ?? selected });
      const job = current().job;
      if (job.kind !== 'idle' && !selectedRequest(job.request)) invalidateJob();
    } catch (error) {
      if (controller.signal.aborted) return;
      consoleActions.error('快预览', error);
      set({ description: { kind: 'failed', reply: null, reason: error instanceof Error ? error.message : String(error) } });
      invalidateJob();
    }
  },
  selectScheme(scheme: PreviewQuickInputSource) {
    set({ scheme, accepted: null, route: null, seconds: 0 });
    invalidateJob();
  },
  // 手动重推：清掉失败记忆，来源可用时直接推导，否则先重读来源。
  rederive() {
    autoAttempt = null;
    if (current().description.kind === 'ready') scheduleFollow();
    else void quickPreview.readSource();
  },
  outputDirectory(outputDirectory: string) { set({ outputDirectory }); },
  unavailableReason(): string | null {
    const result = preparation();
    return result.kind === 'ready' ? null : result.reason;
  },
  // follow：按当前网页编辑内容直接推导，不先读来源；两个修订留空，由宿主同一次捕获交回。
  async start(follow = false) {
    const prepared = preparation(true);
    if (prepared.kind === 'unavailable') throw new Error(prepared.reason);
    // 宿主接受新作业时先取消并等完旧作业，网页不再另发取消、等它的回执。
    if (quickJobCancellable(current().job)) {
      quickPreview.pause();
      clearPicture();
    }
    const request: PreviewQuickRequest = {
      mode: 'quick', target: prepared.target, sourceRevision: follow ? null : prepared.source.revision,
      input: { source: prepared.scheme.source, revision: follow ? null : prepared.scheme.revision },
      initialSeconds: current().seconds,
      outputDirectory: `${prepared.directory.replace(/[\\/]$/, '')}/quick-preview/${crypto.randomUUID()}`,
    };
    autoAttempt = requestKey(request);
    stopPoll();
    set({ job: { kind: 'starting', request } });
    try {
      const reply = await previewApi.planQuick(request);
      if (!ownsRequest(request)) return;
      const cancelling = current().job.kind === 'starting-cancelled';
      accept(request, reply, cancelling);
      const job = current().job;
      if (cancelling && job.kind === 'cancelling') await readJob(job.request, true);
    } catch (error) {
      if (!ownsRequest(request)) return;
      consoleActions.error('快预览', error);
      set({ job: { kind: current().job.kind === 'starting-cancelled' ? 'cancel-failed' : 'failed', request, reply: null, reason: error instanceof Error ? error.message : String(error) } });
    }
  },
  async cancel() {
    quickPreview.pause();
    clearPicture();
    const job = current().job;
    if (job.kind === 'starting') { set({ job: { kind: 'starting-cancelled', request: job.request } }); return; }
    if (job.kind === 'running' || job.kind === 'failed' || job.kind === 'cancel-failed' || job.kind === 'settled') await readJob(job.request, true);
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
    requestPicture();
  },
  retryPicture() { requestPicture(true); },
  invalidate(reason: string) {
    contentFollow = false;
    descriptionController?.abort();
    set({ description: { kind: 'stale', reason } });
    invalidateJob();
  },
  clearTarget(reason: string) {
    set({ target: null, accepted: null, route: null, seconds: 0 });
    quickPreview.invalidate(reason);
  },
};

useWorkbench.subscribe((state, previous) => {
  if (state.quickPreview.playback !== null && quickPlanStale()) quickPreview.pause();
  if (quickPlanStale()) clearPicture();
  else if (state.quickPreview.accepted !== previous.quickPreview.accepted || state.quickPreview.seconds !== previous.quickPreview.seconds ||
    state.quickPreview.playback !== previous.quickPreview.playback) requestPicture();
  const contentChanged = selectionKey(state) !== selectionKey(previous);
  if (state.previewMode !== previous.previewMode || contentChanged) {
    if (state.quickPreview.target !== null) {
      if (contentChanged) quickPreview.clearTarget('场景来源已变化，请重新读取');
      else quickPreview.invalidate('预览档位或场景来源已变化，请重新读取');
    }
  }
  const quick = state.quickPreview, before = previous.quickPreview;
  if (state.previewMode === 'quick' && (state.previewMode !== previous.previewMode || contentChanged || state.host !== previous.host ||
    quick.description !== before.description || quick.job !== before.job || quick.scheme !== before.scheme || quick.target !== before.target))
    scheduleFollow();
});

useAuthoring.subscribe((authoring, previous) => {
  if (authoring.target?.documentPath !== previous.target?.documentPath &&
    getState().contentSelection?.kind === 'scene') {
    quickPreview.clearTarget('作者文档已变化，请重新读取快预览来源');
    return;
  }
  const state = current();
  if (state.target !== null && state.target.root === null && state.description.kind === 'ready' && authoring.documents !== previous.documents) {
    const chosen = defaultRoot(state.description.reply.data);
    if (chosen !== null) void quickPreview.readSource(chosen);
    return;
  }
  const target = state.target;
  const root = target?.root;
  if (target === null || root?.kind !== 'webEditingView' || authoring.documents === previous.documents) return;
  const owners = Object.entries(authoring.documents);
  const changed = owners.length !== Object.keys(previous.documents).length || owners.some(([key, owner]) => {
    const before = previous.documents[key];
    return before === undefined || before.revision !== owner.revision || before.version !== owner.version;
  });
  if (!changed) return;
  const document = authoring.documents[root.authoring.documentPath];
  if (document !== undefined && state.description.kind === 'ready' && state.scheme !== null) {
    const editingView = authoringActions.editingView();
    const savedRevision = document.revision ?? '';
    if (root.authoring.savedRevision === savedRevision && JSON.stringify(root.editingView) === JSON.stringify(editingView)) return;
    contentFollow = true;
    set({ target: { ...target, root: { ...root, authoring: { ...root.authoring, savedRevision }, editingView } } });
  } else quickPreview.invalidate('网页编辑内容或作者来源已变化，请重新读取');
});
