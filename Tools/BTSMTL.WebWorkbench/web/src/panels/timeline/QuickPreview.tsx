import { useCallback, useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { PreviewQuickEntry, PreviewQuickInputSource, PreviewQuickLocation } from '../../api/previewQuickRecords';
import { previewApi } from '../../api/preview';
import { authoringOwner, authoringTargetKey, type AuthoringGraph } from '../../api/authoring';
import {
  quickAuthoringKey, quickBounds, quickInputSourceKey, quickJobCancellable, quickJobFailure, quickJobProgress, quickPlan, quickPlanStale, quickPreview, quickTargetKey,
} from '../../state/quickPreview';
import { authoringActions, authoringDocument, currentDocument, isDocumentDirty, useAuthoring } from '../../state/authoring';
import type { JsonObject } from '../../state/skillJson';
import { projectActions } from '../../state/project';
import { edgeSelectId } from '../../state/graphModel';
import { consoleActions, contentActions, selectionActions, useWorkbench } from '../../state/store';
import { quickRunInputChoice } from '../../state/quickInput';
import { session } from '../../state/session';
import { Facts, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
import { finishAuthoringInputs, openPanel } from '../../shell/layout';
import styles from './quickPreview.module.css';

export const useQuickPlan = () => useWorkbench(state => state.previewMode === 'quick' && !quickPlanStale() ? quickPlan() : null);

// 快档路线与转移建议的来源定位：打开所属图，有转移时选中该连线。
export async function locateQuickSource(location: PreviewQuickLocation) {
  await projectActions.openGraph({ id: location.graphId, owner: location.documentPath, ownerName: location.documentPath }, location.nodeId || undefined);
  if (location.edgeId) {
    const graph = useAuthoring.getState().documents[location.documentPath].documentGraphs!.find(item => item.id === location.graphId)!;
    const edge = graph.edges.find(item => item.id === location.edgeId);
    if (!edge) throw new Error(`来源图 ${location.graphId} 中不存在转移 ${location.edgeId}`);
    selectionActions.select({ kind: 'edge', id: edgeSelectId(graph, edge) });
  }
}

const locate = (source: PreviewQuickLocation) => void locateQuickSource(source).catch(error => consoleActions.error('快预览', error));

// 推导结果只带状态身份路径；作者看到的名字取当前编辑文档里同一图节点的名称，节点未命名时保留身份路径。
export type QuickName = (source: PreviewQuickLocation | null, identity: string) => string;
export function useQuickName(): QuickName {
  const documents = useAuthoring(state => state.documents);
  return useCallback((source, identity) => {
    if (source === null || !source.nodeId) return identity;
    const graph = documents[source.documentPath]?.documentGraphs?.find(item => item.id === source.graphId);
    return graph?.nodes.find(node => node.id === source.nodeId)?.displayName || identity;
  }, [documents]);
}

function LocationButton({ source, label }: { source: PreviewQuickLocation; label: string }) {
  return <Button variant="ghost" tip={source.displayPath} onClick={() => locate(source)}>{label}</Button>;
}

export function QuickCursor() {
  const plan = useQuickPlan();
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  if (plan === null) return null;
  const bounds = quickBounds(plan);
  return <label className={styles.cursor}>时间
    <NumberInput value={seconds} min={bounds.first} max={bounds.last} label={`预览时间（${plan.clock.unit}）`}
      onChange={value => { if (value !== null) quickPreview.seek(value); }} />
    <span>{plan.clock.unit}</span>
  </label>;
}

export function QuickEntryButton({ documentPath, entry }: { documentPath: string; entry: PreviewQuickEntry }) {
  const { description: source, quick, busy } = useWorkbench(useShallow(current => ({
    description: current.quickPreview.description, quick: current.previewMode === 'quick',
    busy: current.quickPreview.description.kind === 'loading' || quickJobProgress(current.quickPreview.job) !== null,
  })));
  if (!quick) return null;
  const description = source.kind === 'ready' ? source.reply.data : null;
  const document = description?.documents.find(item => item.documentPath === documentPath);
  const choice = document?.entries.find(item => entry.kind === 'graph'
    ? item.entry.kind === 'graph' && item.entry.graphId === entry.graphId && item.entry.machine === entry.machine
    : item.entry.kind === 'timeline' && item.entry.timelineId === entry.timelineId);
  return <Button variant="ghost" disabled={choice === undefined || busy}
    tip={choice === undefined ? '当前来源目录尚未提供这个预览入口' : '以当前内容为入口，使用所选场景和输入方案生成预览'}
    onClick={() => void quickPreview.readSource({ documentPath, savedRevision: document!.savedRevision, entry: choice!.entry })}>
    {entry.kind === 'graph' ? '预览此图' : '预览此 Timeline'}
  </Button>;
}

async function editInput(source: Extract<PreviewQuickInputSource, { kind: 'documentInput' }>) {
  const owner = authoringOwner(source.documentPath);
  if (await authoringActions.open(owner) !== 'opened') return;
  const path = `inputs/${source.inputId}.json`;
  const plan = currentDocument(path) as JsonObject;
  const index = (plan.Schemes as JsonObject[]).findIndex(scheme => scheme.AuthoringId === source.schemeId);
  if (index === -1) throw new Error('所选输入方案已不在当前作者文档中，请重新选择输入方案');
  await authoringActions.openInput(owner, path, index, plan.Actor as string);
  openPanel('timeline');
}

async function runInput(selection: PreviewQuickInputSource) {
  const targetKey = quickTargetKey(useWorkbench.getState().quickPreview.target);
  const schemeKey = quickInputSourceKey(selection);
  finishAuthoringInputs();
  if (Object.values(useAuthoring.getState().documents).some(isDocumentDirty)) {
    await authoringActions.save();
    const state = useAuthoring.getState();
    if (Object.values(state.documents).some(isDocumentDirty))
      throw new Error(authoringDocument(state).lastSave?.message ?? '作者修改尚未保存，请处理后再真实运行');
  }
  if (await session.loadInputs() !== null) throw new Error('读取当前场景的输入方案失败，具体原因见控制台');
  const state = useWorkbench.getState();
  if (state.previewMode !== 'quick' || quickTargetKey(state.quickPreview.target) !== targetKey ||
    quickInputSourceKey(state.quickPreview.scheme) !== schemeKey)
    throw new Error('场景、入口或输入选择已变化，本次真实运行准备已取消');
  const reply = await previewApi.describeQuick(state.quickPreview.target!);
  if (!reply.success || reply.data === null || reply.data.root === null)
    throw new Error(reply.message ?? '当前预览入口尚未读取成功');
  const source = selection.kind === 'documentInput' ? selection : reply.data.root.defaultInput;
  if (source?.kind !== 'documentInput') throw new Error('当前根默认输入没有作者方案，请先创建并选择输入方案');
  const current = useWorkbench.getState();
  if (quickTargetKey(current.quickPreview.target) !== targetKey || quickInputSourceKey(current.quickPreview.scheme) !== schemeKey)
    throw new Error('场景、入口或输入选择已变化，本次真实运行准备已取消');
  const choice = quickRunInputChoice(current.inputCatalog!, reply.data.actorId, source);
  contentActions.selectInputSource({ kind: 'local' });
  contentActions.selectInputPlan(choice);
  const blocked = session.canStartNewRun();
  if (blocked !== null) throw new Error(blocked);
  const root = state.quickPreview.target!.root!;
  const owner = authoringOwner(root.authoring.documentPath);
  if (await authoringActions.open(owner) !== 'opened') throw new Error('预览入口文档尚未打开，未提交真实运行');
  const mode = current.host!.modes.find(item => item.createsRun)!;
  contentActions.selectPreviewMode(mode.id);
  const prepared = useWorkbench.getState();
  await authoringActions.save('apply', { kind: 'local', [prepared.host!.inputLimit.name]: prepared.localInputLimit,
    players: prepared.inputChoices });
  const outcome = authoringDocument(useAuthoring.getState(), owner).lastSave;
  if (outcome?.preview === null || outcome?.preview === undefined || !outcome.ok)
    throw new Error(outcome?.message ?? '入口内容尚未应用，未提交真实运行');
}

// 快档工具条：入口、输入方案与生成状态；内容或方案变化后自动重读来源并重新生成，所播 Timeline 按时间摆在输入轨下方。
export function QuickBar() {
  const { state, stale } = useWorkbench(useShallow(current => ({ state: current.quickPreview, stale: quickPlanStale() })));
  const active = useAuthoring(current => current.target === null ? null : authoringTargetKey(current.target));
  const [open, setOpen] = useState(false);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const playerAvailable = useWorkbench(current => current.host?.player.available === true);
  const description = state.description.kind === 'ready' ? state.description.reply.data : null;
  const input = description?.root?.inputSchemes.find(scheme => quickInputSourceKey(scheme.source) === quickInputSourceKey(state.scheme))?.source;
  const editableInput = input?.kind === 'documentInput' ? input : input?.kind === 'rootDefaults' ? description?.root?.defaultInput : null;
  const sceneApplyReason = description?.root?.sceneApplyUnavailableReason ?? null;
  const job = state.job;
  const progress = quickJobProgress(job);
  const plan = state.route?.plan ?? null;
  const sourceReason = quickPreview.sourceUnavailableReason();
  const reason = state.description.kind === 'failed' || state.description.kind === 'stale' ? state.description.reason
    : sourceReason ?? (progress === null ? quickPreview.unavailableReason() : null);
  const failure = quickJobFailure(job);
  const status = state.description.kind === 'loading' ? '正在读取来源'
    : progress ?? (plan === null ? null : `${!stale ? '' : state.description.kind === 'failed' ? '内容已变化，未能重新生成 · '
      : '内容已变化，正在重新生成 · '}共 ${plan.duration.toFixed(2)} ${plan.clock.unit}`);
  // 推导根只列当前作者文档的入口；当前文档不在目录里时列全部。
  const roots = useMemo(() => {
    const documents = description?.documents ?? [];
    const owned = documents.filter(document => document.documentPath === active);
    return (owned.length ? owned : documents).flatMap(document => document.entries.map(choice => ({
      authoring: { documentPath: document.documentPath, savedRevision: document.savedRevision, entry: choice.entry },
      name: owned.length ? choice.displayName : `${document.displayName} / ${choice.displayName}`,
    })));
  }, [description, active]);
  const busy = running || progress !== null || state.description.kind === 'loading';
  return <section className={open ? styles.preview : styles.bar} aria-label="快预览">
    <PanelBar wrap>
      <strong>快速预览</strong>
      {description && roots.length > 0 && <Select variant="ghost" label="入口" placeholder="选择入口图"
        value={state.target?.root == null ? null : quickAuthoringKey(state.target.root.authoring)} disabled={busy}
        options={roots.map(root => ({ value: quickAuthoringKey(root.authoring), label: root.name }))}
        onChange={value => {
          const root = roots.find(item => quickAuthoringKey(item.authoring) === value);
          if (root !== undefined) void quickPreview.readSource(root.authoring);
        }} />}
      {description?.root && state.target?.root != null && description.root.machines.length > 1 &&
        <Select variant="ghost" label="根状态机" value={quickAuthoringKey(state.target.root.authoring)} disabled={busy}
          options={description.root.machines.map(machine => ({ value: quickAuthoringKey(machine.authoring), label: machine.entryTimeline ?? `操作 ${machine.operation}` }))}
          onChange={value => {
            const machine = description.root?.machines.find(item => quickAuthoringKey(item.authoring) === value);
            if (machine !== undefined) void quickPreview.readSource(machine.authoring);
          }} />}
      {description?.root && <Select variant="ghost" label="输入方案" placeholder="选择输入方案"
        value={state.scheme === null ? null : quickInputSourceKey(state.scheme)}
        options={description.root.inputSchemes.map(scheme => ({ value: quickInputSourceKey(scheme.source), label: `方案：${scheme.name}` }))}
        onChange={value => {
          const scheme = description.root?.inputSchemes.find(item => quickInputSourceKey(item.source) === value);
          if (scheme !== undefined) quickPreview.selectScheme(scheme.source);
        }} />}
      {description?.root && <Button variant="ghost" disabled={editableInput?.kind !== 'documentInput' || busy}
        tip={editableInput?.kind === 'documentInput' ? '打开所选方案的输入 Timeline，继续编辑输入轨' : '当前默认输入没有作者方案，请先创建输入方案'}
        onClick={() => {
          if (editableInput?.kind === 'documentInput') void editInput(editableInput).catch(error => consoleActions.error('输入方案', error));
        }}>编辑所选输入</Button>}
      {status && <span role="status" className={styles.status} data-stale={stale || undefined}>{status}</span>}
      {(failure ?? reason) && <span role={failure ? 'alert' : 'status'} className={styles.reason} title={failure ?? reason ?? undefined}>{failure ?? reason}</span>}
      <span className={styles.grow} />
      {quickJobCancellable(job) && job.kind !== 'settled'
        ? <Button variant="ghost" onClick={() => void quickPreview.cancel()}>{job.kind === 'cancel-failed' ? '重试取消' : '取消'}</Button>
        : <Button variant="ghost" disabled={sourceReason !== null || busy} tip={sourceReason ?? '按当前作者内容与输入轨重新生成预览'}
          onClick={() => quickPreview.rederive()}>重新生成</Button>}
      <Button variant="ghost" pressed={open} onClick={() => setOpen(!open)}>详情</Button>
      {playerAvailable && <Button variant="ghost" disabled={busy || sceneApplyReason !== null || editableInput?.kind !== 'documentInput' || description === null}
        tip={sceneApplyReason ?? (editableInput?.kind === 'documentInput' ? '保存并应用入口内容，使用所选方案的按键日程在场景中真实运行并录制' : '当前默认输入没有作者方案，请先创建输入方案')}
        onClick={() => {
          setRunning(true);
          setRunError(null);
          void runInput(input!).catch(error => {
            consoleActions.error('真实运行', error);
            setRunError(error instanceof Error ? error.message : String(error));
          }).finally(() => setRunning(false));
        }}>{running ? '正在准备真实运行' : '真实运行一次'}</Button>}
    </PanelBar>
    {playerAvailable && sceneApplyReason !== null && <p role="status" className={styles.reason}>{sceneApplyReason}</p>}
    {runError !== null && <p role="alert" className={styles.reason}>{runError}</p>}
    {open && <QuickDetails />}
  </section>;
}

function QuickDetails() {
  const { state, directory } = useWorkbench(useShallow(current => ({ state: current.quickPreview, directory: current.sources?.runDirectory })));
  const description = state.description.kind === 'ready' ? state.description.reply.data : null;
  const job = state.job;
  const receipt = 'reply' in job ? job.reply : null;
  const status = receipt?.data;
  const scheme = description?.root?.inputSchemes.find(item => quickInputSourceKey(item.source) === quickInputSourceKey(state.scheme));
  const route = state.route;
  const plan = route?.plan ?? null;
  const name = useQuickName();
  const identity = useMemo(() => route === null || plan === null ? [] : [
    { displayName: '结果作业', value: status?.jobId ?? '' }, { displayName: '结果来源修订', value: route.request.sourceRevision ?? '跟随当前内容' },
    { displayName: 'Clock', value: `${plan.clock.unit} · origin ${plan.clock.origin} · ${plan.clock.owner} · ${plan.clock.tickRate} Hz` },
    { displayName: '输入来源', value: plan.inputSource }, { displayName: '有效输入修订', value: plan.inputRevision },
    { displayName: '实际输入', value: JSON.stringify(plan.inputsUsed) }, { displayName: '实际条件覆盖', value: JSON.stringify(plan.conditionsUsed) },
  ], [route, plan, status?.jobId]);
  return <div className={styles.details}>
    {state.description.kind === 'failed' && <p className={styles.notice} role="alert">{state.description.reason}</p>}
    {description && <Facts fields={[{ displayName: '场景', value: description.scenePath },
      { displayName: '内容来源', value: description.source === null ? '尚未选入口' : description.source.kind === 'webEditingView' ? '当前网页编辑内容'
        : description.source.documents.some(document => document.hasUnsavedChanges) ? '包含未保存修改' : '已保存作者内容' },
      { displayName: '输入方案修订', value: scheme?.revision ?? '未选择' }]} />}
    {receipt && <div className={styles.notice} role={receipt.success ? 'status' : 'alert'}>
      {[...new Set([receipt.code, receipt.message, status?.error, ...(status?.cleanupErrors ?? []), ...(status?.logErrors ?? [])])]
        .filter(value => value !== null && value !== undefined && value !== '').map((value, index) => <p key={index}>{value}</p>)}
    </div>}
    {plan && (plan.stopReason || plan.stopDetail) && <p className={styles.notice}>{[plan.stopReason, plan.stopDetail].filter(Boolean).join(' · ')}</p>}
    {plan && plan.assumptions.length > 0 && <Fold title={`计算时的假设（${plan.assumptions.length}）`}>{plan.assumptions.map((assumption, index) => <p key={index}>{assumption}</p>)}</Fold>}
    {plan && plan.segments.some(segment => segment.conditionDependencies.length > 0) && <Fold title="依赖条件的路线区段">
      <p>真实运行只使用相同的按键日程。以下区段的条件写入和默认假设以真实记录为准，状态进入或退出时间不同时，即为路线分歧。</p>
      {plan.segments.filter(segment => segment.conditionDependencies.length > 0).map((segment, index) => <p key={index}>
        {name(segment.stateSource, segment.state)} · {segment.start.toFixed(3)}–{segment.end.toFixed(3)} {plan.clock.unit} ·
        {segment.conditionDependencies.map(item => `${item.label}（${item.written ? '依赖条件写入' : '未写入，按默认值'}）`).join('、')}
      </p>)}
    </Fold>}
    {plan && <Fold title={`用到的条件 / 缺条件没走成的转移（${plan.conditions.length} / ${plan.suggestions.length}）`}>
      {plan.conditions.map(condition => <p key={condition.key}>{condition.label} · {condition.source}{condition.undeterminable ? ' · 无法确定' : ''}</p>)}
      {plan.suggestions.map((suggestion, index) => <div key={index}>
        {suggestion.fromSource ? <LocationButton source={suggestion.fromSource} label={name(suggestion.fromSource, suggestion.fromState)} /> : suggestion.fromState}
        <span> → </span>
        {suggestion.toSource ? <LocationButton source={suggestion.toSource} label={name(suggestion.toSource, suggestion.toState)} /> : suggestion.toState}
        <LocationButton source={suggestion.transitionSource} label="打开转移" />
        <span>{suggestion.start}–{suggestion.end} {plan.clock.unit} · {suggestion.missing.map(condition => condition.label).join('、')}</span>
      </div>)}
    </Fold>}
    {status && status.notes.length > 0 && <Fold title="计算说明">{status.notes.map((note, index) => <p key={index}>{note}</p>)}</Fold>}
    <Fold title="来源与输出">
      <label className={styles.output}>输出目录<TextInput aria-label="快预览输出目录" value={state.outputDirectory || directory || ''}
        onChange={event => quickPreview.outputDirectory(event.currentTarget.value)} /></label>
      {description?.source && <Facts fields={[
        { displayName: '来源类型', value: description.source.kind }, { displayName: '来源路径', value: description.source.authoring.documentPath },
        { displayName: '来源修订', value: description.source.revision }, { displayName: '场景修订', value: description.source.sceneRevision },
        { displayName: '方案身份', value: scheme?.source.kind === 'documentInput' ? `${scheme.source.documentPath} / ${scheme.source.inputId} / ${scheme.source.schemeId}`
          : scheme === undefined ? '未选择' : '当前根默认输入' },
        ...description.source.documents.map(document => ({ displayName: document.documentPath,
          value: `${document.revision} · ${document.hasUnsavedChanges ? '未保存' : '已保存'} · ${document.savedRevision === '' ? '新文档' : document.savedRevision}` })),
      ]} />}
      {status && <Facts fields={[{ displayName: '作业', value: status.jobId }, { displayName: '阶段', value: status.stage },
        { displayName: '执行模式', value: status.nativeMode }, { displayName: '实际采用', value: status.adoptedMode ?? '未记录' },
        { displayName: '输出目录', value: status.outputDirectory ?? '尚未提供' }, { displayName: '摘要', value: status.summaryPath ?? '尚未提供' }]} />}
    </Fold>
    {identity.length > 0 && <IdentityFold fields={identity} />}
  </div>;
}

export function QuickGraphRoute({ graph }: { graph: AuthoringGraph }) {
  const plan = useQuickPlan();
  const stale = useWorkbench(quickPlanStale);
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  const name = useQuickName();
  if (plan === null) return null;
  const segments = plan.segments.filter(segment => segment.stateSource?.documentPath === graph.owner && segment.stateSource.graphId === graph.id && segment.start <= seconds && seconds < segment.end);
  return <PanelBar wrap><strong>快速预览{stale ? '（内容已变化）' : ''}</strong>{segments.length ? segments.map((segment, index) =>
    <Button key={index} variant="ghost" tip={`播放区间 ${segment.start}–${segment.end} ${plan.clock.unit}；${segment.conditionDependencies.map(item => `${item.label}：${item.written ? '依赖条件写入' : '未写入，按默认值'}`).join('；')}`}
      onClick={() => { quickPreview.seek(segment.start); if (segment.stateSource) locate(segment.stateSource); }}>
      {name(segment.stateSource, segment.state)} {segment.start.toFixed(3)}–{segment.end.toFixed(3)}{segment.conditionDependencies.some(item => item.written) ? ' · 条件写入' : ''}</Button>) : <span>当前时刻没有播放这张图</span>}
  </PanelBar>;
}
