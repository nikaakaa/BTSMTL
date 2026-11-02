import { previewApi } from '../api/preview';
import { isAbort } from '../api/http';
import type {
  PreviewQuickDescription, PreviewQuickDescriptionReply, PreviewQuickInput, PreviewQuickRequest,
  PreviewQuickSource, PreviewQuickStatusReply, PreviewQuickTarget,
} from '../api/previewQuickRecords';
import { authoringTargetKey } from '../api/authoring';
import { useAuthoring } from './authoring';
import { consoleActions, getState, selectedScene, useWorkbench } from './store';

type Description = { kind: 'idle' } | { kind: 'loading' } | { kind: 'stale'; reason: string }
  | { kind: 'ready'; reply: PreviewQuickDescriptionReply & { data: PreviewQuickDescription } }
  | { kind: 'failed'; reason: string; reply: PreviewQuickDescriptionReply | null };
type ActiveReply = PreviewQuickStatusReply & { data: NonNullable<PreviewQuickStatusReply['data']> };
type Job = { kind: 'idle' }
  | { kind: 'starting' | 'starting-cancelled'; request: PreviewQuickRequest }
  | { kind: 'running' | 'cancelling'; request: PreviewQuickRequest; reply: ActiveReply }
  | { kind: 'settled'; request: PreviewQuickRequest; reply: PreviewQuickStatusReply }
  | { kind: 'failed'; request: PreviewQuickRequest; reply: ActiveReply | null; reason: string };

export interface QuickPreviewState {
  target: PreviewQuickTarget | null;
  description: Description;
  scheme: number | null;
  job: Job;
  seconds: number;
  outputDirectory: string;
}

let descriptionController: AbortController | null = null;
let jobController: AbortController | null = null;
let pollTimer: ReturnType<typeof setTimeout> | null = null;
const current = () => getState().quickPreview;
const set = (patch: Partial<QuickPreviewState>) => useWorkbench.setState(state => ({ quickPreview: { ...state.quickPreview, ...patch } }));
const targetKey = (target: PreviewQuickTarget | null) => JSON.stringify(target);

export function quickDraftIssue(source: PreviewQuickSource): string | null {
  const documents = useAuthoring.getState().documents;
  for (const item of source.documents) {
    const path = item.path.replaceAll('\\', '/');
    const name = path.slice(path.lastIndexOf('/') + 1);
    const document = documents[authoringTargetKey({ kind: 'skill', name })];
    if (document && (Object.keys(document.edited).length > 0 || document.removed.length > 0))
      return `${name} 的网页修改尚未保存；当前快预览接口读取 Native 文档，没有接收这份网页草稿`;
  }
  return null;
}

function selectedRequest(request: PreviewQuickRequest): boolean {
  const state = current();
  if (getState().previewMode !== 'quick' || state.description.kind !== 'ready') return false;
  const description = state.description.reply.data;
  const source = description.source;
  if (source === null) return false;
  const scheme = description.root?.inputSchemes.find(item => item.index === state.scheme);
  return targetKey(state.target) === targetKey(request.target) && source.revision === request.sourceRevision &&
    scheme?.index === request.input.scheme && scheme.revision === request.input.revision &&
    quickDraftIssue(source) === null;
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
    set({ job: { kind: 'failed', request, reply: { ...reply, data: status }, reason } });
    consoleActions.log('error', '快预览', reason, reply.rawJson);
    return;
  }
  if (status !== null && !status.completed && reply.pending) {
    set({ job: { kind: cancelling || status.cancellationRequested ? 'cancelling' : 'running', request, reply: { ...reply, data: status } } });
    if (!selectedRequest(request) && !cancelling && !status.cancellationRequested) void quickPreview.cancel();
    else schedulePoll(request);
  } else if (status !== null && !status.completed) {
    set({ job: { kind: 'failed', request, reply: { ...reply, data: status }, reason: reply.message ?? status.error ?? '作业未完成，状态读取未获推进回执' } });
  } else {
    set({ job: selectedRequest(request) ? { kind: 'settled', request, reply } : { kind: 'idle' }, seconds: 0 });
  }
  if (!reply.success) consoleActions.log('error', '快预览', `${reply.code ?? ''} ${reply.message ?? status?.error ?? '快预览失败'}`.trim(), reply.rawJson);
}

async function readJob(request: PreviewQuickRequest, cancel: boolean) {
  const job = current().job;
  if (!ownsRequest(request) || (job.kind !== 'running' && job.kind !== 'cancelling' && job.kind !== 'failed') || job.reply === null) return;
  const receipt = job.reply;
  const status = receipt.data;
  stopPoll();
  const controller = jobController = new AbortController();
  const cancelling = cancel || job.kind === 'cancelling';
  if (cancel) set({ job: { kind: 'cancelling', request, reply: receipt } });
  try {
    const reply = await (cancel ? previewApi.cancelQuick(status.jobId, controller.signal) : previewApi.quickStatus(status.jobId, controller.signal));
    if (controller.signal.aborted) return;
    accept(request, reply, cancelling);
  } catch (error) {
    if (isAbort(error) || !ownsRequest(request)) return;
    consoleActions.error('快预览', error);
    set({ job: { kind: 'failed', request, reply: receipt, reason: error instanceof Error ? error.message : String(error) } });
  }
}

function invalidateJob() {
  const job = current().job;
  if (job.kind === 'idle') return;
  if (job.kind === 'settled' || (job.kind === 'failed' && (job.reply === null || job.reply.data?.completed))) set({ job: { kind: 'idle' } });
  else void quickPreview.cancel();
}

export function quickPlan() {
  const state = current();
  return state.job.kind === 'settled' && selectedRequest(state.job.request) ? state.job.reply.data?.plan ?? null : null;
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
  const issue = quickDraftIssue(data.source);
  if (issue !== null) return { kind: 'unavailable', reason: issue } as const;
  const scheme = data.root.inputSchemes.find(item => item.index === state.scheme);
  if (scheme === undefined) return { kind: 'unavailable', reason: '选择宿主提供的输入方案' } as const;
  if (state.job.kind === 'starting' || state.job.kind === 'starting-cancelled' || state.job.kind === 'running' ||
    state.job.kind === 'cancelling' || (state.job.kind === 'failed' && state.job.reply !== null && !state.job.reply.data.completed))
    return { kind: 'unavailable', reason: state.job.kind === 'cancelling' || state.job.kind === 'starting-cancelled'
      ? '正在取消原作业，等待清理结束' : '原作业尚未结束，请先完成或取消' } as const;
  const directory = state.outputDirectory || workbench.sources?.runDirectory;
  if (!directory || !/^D:[\\/]/i.test(directory)) return { kind: 'unavailable', reason: '快预览输出目录须明确位于 D 盘' } as const;
  return { kind: 'ready', target: { ...target, root: target.root }, source: data.source, scheme, directory } as const;
}

export const quickPreview = {
  async describe(target: PreviewQuickTarget) {
    descriptionController?.abort();
    if (targetKey(target) !== targetKey(current().target)) { invalidateJob(); set({ scheme: null }); }
    const controller = descriptionController = new AbortController();
    set({ target, description: { kind: 'loading' }, seconds: 0 });
    try {
      const reply = await previewApi.describeQuick(target, controller.signal);
      if (controller.signal.aborted) return;
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
      if (isAbort(error)) return;
      consoleActions.error('快预览', error);
      set({ description: { kind: 'failed', reply: null, reason: error instanceof Error ? error.message : String(error) }, scheme: null });
      invalidateJob();
    }
  },
  selectScheme(scheme: number) {
    set({ scheme });
    invalidateJob();
  },
  outputDirectory(outputDirectory: string) { set({ outputDirectory }); },
  unavailableReason(): string | null {
    const result = preparation();
    return result.kind === 'ready' ? null : result.reason;
  },
  async start(input: Pick<PreviewQuickInput, 'points' | 'conditions'> = {}, rangeEndSeconds?: number) {
    const prepared = preparation();
    if (prepared.kind === 'unavailable') throw new Error(prepared.reason);
    const request: PreviewQuickRequest = {
      mode: 'quick', target: prepared.target, sourceRevision: prepared.source.revision,
      input: { scheme: prepared.scheme.index, revision: prepared.scheme.revision, ...input },
      outputDirectory: `${prepared.directory.replace(/[\\/]$/, '')}/quick-preview/${crypto.randomUUID()}`,
      ...(rangeEndSeconds === undefined ? {} : { rangeEndSeconds }),
    };
    stopPoll();
    set({ job: { kind: 'starting', request }, seconds: 0 });
    try {
      const reply = await previewApi.planQuick(request);
      if (!ownsRequest(request)) return;
      const cancelling = current().job.kind === 'starting-cancelled' || !selectedRequest(request);
      accept(request, reply, false);
      if (cancelling && current().job.kind === 'running') await quickPreview.cancel();
    } catch (error) {
      if (!ownsRequest(request)) return;
      consoleActions.error('快预览', error);
      set({ job: { kind: 'failed', request, reply: null, reason: error instanceof Error ? error.message : String(error) } });
    }
  },
  async cancel() {
    const job = current().job;
    if (job.kind === 'starting') { set({ job: { kind: 'starting-cancelled', request: job.request } }); return; }
    if (job.kind === 'running' || job.kind === 'failed') await readJob(job.request, true);
  },
  async readStatus() {
    const job = current().job;
    if (job.kind === 'failed' && job.reply?.data) await readJob(job.request, false);
  },
  seek(seconds: number) { set({ seconds }); },
  invalidate(reason: string) {
    descriptionController?.abort();
    set({ description: { kind: 'stale', reason }, seconds: 0 });
    invalidateJob();
  },
};

useWorkbench.subscribe((state, previous) => {
  if (state.previewMode !== previous.previewMode || state.contentSelection !== previous.contentSelection ||
    selectedScene(state)?.revision !== selectedScene(previous)?.revision) {
    if (state.quickPreview.target !== null) quickPreview.invalidate('预览档位或场景来源已变化，请重新读取');
  }
});

useAuthoring.subscribe(() => {
  const state = current();
  if (state.description.kind !== 'ready' || state.description.reply.data.source === null) return;
  const issue = quickDraftIssue(state.description.reply.data.source);
  if (issue !== null) quickPreview.invalidate(issue);
});
