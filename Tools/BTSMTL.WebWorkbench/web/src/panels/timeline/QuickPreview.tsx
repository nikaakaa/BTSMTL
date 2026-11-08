import { useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { PreviewQuickLocation } from '../../api/previewQuickRecords';
import { authoringTargetKey, type SkillDocumentGraph } from '../../api/authoring';
import { quickAuthoringKey, quickBounds, quickInputSourceKey, quickJobCancellable, quickJobProgress, quickPlan, quickPlanStale, quickPreview } from '../../state/quickPreview';
import { useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { edgeSelectId } from '../../state/graphModel';
import { consoleActions, selectionActions, useWorkbench } from '../../state/store';
import { Facts, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
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

function LocationButton({ source, label }: { source: PreviewQuickLocation; label: string }) {
  return <Button variant="ghost" tip={source.displayPath} onClick={() => locate(source)}>{label}</Button>;
}

export function QuickCursor() {
  const plan = useQuickPlan();
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  if (plan === null) return null;
  const bounds = quickBounds(plan);
  return <label className={styles.cursor}>路线时间
    <NumberInput value={seconds} min={bounds.first} max={bounds.last} label={`路线时间（${plan.clock.unit}）`}
      onChange={value => { if (value !== null) quickPreview.seek(value); }} />
    <span>{plan.clock.unit}</span>
  </label>;
}

// 快档工具条：推导根、输入方案与推导状态；内容或方案变化后自动重读来源并重推，路线和输入都画在下方同一 Timeline 里。
export function QuickBar() {
  const { state, stale } = useWorkbench(useShallow(current => ({ state: current.quickPreview, stale: quickPlanStale() })));
  const active = useAuthoring(current => current.target === null ? null : authoringTargetKey(current.target));
  const [open, setOpen] = useState(false);
  const description = state.description.kind === 'ready' ? state.description.reply.data : null;
  const job = state.job;
  const progress = quickJobProgress(job);
  const plan = state.accepted?.plan ?? null;
  const sourceReason = quickPreview.sourceUnavailableReason();
  const reason = state.description.kind === 'failed' || state.description.kind === 'stale' ? state.description.reason
    : sourceReason ?? quickPreview.unavailableReason();
  const failure = job.kind === 'failed' || job.kind === 'cancel-failed' ? job.reason : null;
  const status = state.description.kind === 'loading' ? '正在读取来源'
    : progress ?? (plan === null ? null : `${stale ? '路线已过期 · ' : ''}${plan.status} · ${plan.duration.toFixed(3)} ${plan.clock.unit}`);
  // 推导根只列当前作者文档的入口；当前文档不在目录里时列全部。
  const roots = useMemo(() => {
    const documents = description?.documents ?? [];
    const owned = documents.filter(document => document.documentPath === active);
    return (owned.length ? owned : documents).flatMap(document => document.entries.map(choice => ({
      authoring: { documentPath: document.documentPath, savedRevision: document.savedRevision, entry: choice.entry },
      name: owned.length ? choice.displayName : `${document.displayName} / ${choice.displayName}`,
    })));
  }, [description, active]);
  const busy = progress !== null || state.description.kind === 'loading';
  return <section className={open ? styles.preview : styles.bar} aria-label="快预览">
    <PanelBar wrap>
      <strong>快档</strong>
      {description && roots.length > 0 && <Select variant="ghost" label="推导根" placeholder="选择推导根"
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
      {status && <span role="status" className={styles.status} data-stale={stale || undefined}>{status}</span>}
      {(failure ?? reason) && <span role={failure ? 'alert' : 'status'} className={styles.reason} title={failure ?? reason ?? undefined}>{failure ?? reason}</span>}
      <span className={styles.grow} />
      {quickJobCancellable(job) && job.kind !== 'settled'
        ? <Button variant="ghost" onClick={() => void quickPreview.cancel()}>{job.kind === 'cancel-failed' ? '重试取消' : '取消推导'}</Button>
        : <Button variant="ghost" disabled={sourceReason !== null || busy} tip={sourceReason ?? '按当前作者内容与输入方案重新推导路线'}
          onClick={() => quickPreview.rederive()}>重新推导</Button>}
      <Button variant="ghost" pressed={open} onClick={() => setOpen(!open)}>详情</Button>
    </PanelBar>
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
  const result = state.accepted;
  const plan = result?.plan ?? null;
  const identity = useMemo(() => result === null || plan === null ? [] : [
    { displayName: '结果作业', value: result.jobId }, { displayName: '结果来源修订', value: result.request.sourceRevision },
    { displayName: 'Clock', value: `${plan.clock.unit} · origin ${plan.clock.origin} · ${plan.clock.owner} · ${plan.clock.tickRate} Hz` },
    { displayName: '输入来源', value: plan.inputSource }, { displayName: '有效输入修订', value: plan.inputRevision },
    { displayName: '实际输入', value: JSON.stringify(plan.inputsUsed) }, { displayName: '实际条件覆盖', value: JSON.stringify(plan.conditionsUsed) },
  ], [result, plan]);
  return <div className={styles.details}>
    {description && <Facts fields={[{ displayName: '场景', value: description.scenePath },
      { displayName: '推导来源', value: description.source === null ? '尚未选根' : description.source.kind === 'webEditingView' ? '当前网页编辑内容'
        : description.source.documents.some(document => document.hasUnsavedChanges) ? '包含未保存修改' : '已保存作者内容' },
      { displayName: '输入方案修订', value: scheme?.revision ?? '未选择' }]} />}
    {receipt && <div className={styles.notice} role={receipt.success ? 'status' : 'alert'}>
      {[...new Set([receipt.code, receipt.message, status?.error, ...(status?.cleanupErrors ?? []), ...(status?.logErrors ?? [])])]
        .filter(value => value !== null && value !== undefined && value !== '').map((value, index) => <p key={index}>{value}</p>)}
    </div>}
    {plan && (plan.stopReason || plan.stopDetail) && <p className={styles.notice}>{[plan.stopReason, plan.stopDetail].filter(Boolean).join(' · ')}</p>}
    {plan && plan.assumptions.length > 0 && <Fold title={`推导假设（${plan.assumptions.length}）`}>{plan.assumptions.map((assumption, index) => <p key={index}>{assumption}</p>)}</Fold>}
    {plan && <Fold title={`条件与转移建议（${plan.conditions.length} / ${plan.suggestions.length}）`}>
      {plan.conditions.map(condition => <p key={condition.key}>{condition.label} · {condition.source}{condition.undeterminable ? ' · 无法确定' : ''}</p>)}
      {plan.suggestions.map((suggestion, index) => <div key={index}>
        {suggestion.fromSource ? <LocationButton source={suggestion.fromSource} label={suggestion.fromState} /> : suggestion.fromState}
        <span> → </span>
        {suggestion.toSource ? <LocationButton source={suggestion.toSource} label={suggestion.toState} /> : suggestion.toState}
        <LocationButton source={suggestion.transitionSource} label={suggestion.transition} />
        <span>{suggestion.start}–{suggestion.end} {plan.clock.unit} · {suggestion.missing.map(condition => condition.label).join('、')}</span>
      </div>)}
    </Fold>}
    {status && status.notes.length > 0 && <Fold title="推导说明">{status.notes.map((note, index) => <p key={index}>{note}</p>)}</Fold>}
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

export function QuickGraphRoute({ graph }: { graph: SkillDocumentGraph }) {
  const plan = useQuickPlan();
  const stale = useWorkbench(quickPlanStale);
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  if (plan === null) return null;
  const segments = plan.segments.filter(segment => segment.stateSource?.documentPath === graph.owner && segment.stateSource.graphId === graph.id && segment.start <= seconds && seconds < segment.end);
  return <PanelBar wrap><strong>路线推导{stale ? '（已过期）' : ''}</strong>{segments.length ? segments.map((segment, index) =>
    <Button key={index} variant="ghost" tip={`路线区间 ${segment.start}–${segment.end} ${plan.clock.unit}；不提供端口采值`}
      onClick={() => { quickPreview.seek(segment.start); if (segment.stateSource) locate(segment.stateSource); }}>
      {segment.state} {segment.start.toFixed(3)}–{segment.end.toFixed(3)}</Button>) : <span>当前时刻该图没有推导区间</span>}
  </PanelBar>;
}
