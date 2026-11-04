import { useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { PreviewQuickConditionPoint, PreviewQuickDescription, PreviewQuickLocation, PreviewQuickPlan } from '../../api/previewQuickRecords';
import type { SkillDocumentGraph } from '../../api/authoring';
import { quickBounds, quickPlan, quickPlanStale, quickPreview, quickTargetKey, type QuickPreviewState } from '../../state/quickPreview';
import { useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { edgeSelectId } from '../../state/graphModel';
import { consoleActions, selectionActions, useWorkbench } from '../../state/store';
import { Facts, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, Checkbox, NumberInput, Select, TextInput, Tip } from '../../ui/controls';
import styles from './quickPreview.module.css';

export const useQuickPlan = () => useWorkbench(state => state.previewMode === 'quick' ? quickPlan() : null);

async function locate(location: PreviewQuickLocation) {
  const graphs = Object.values(useAuthoring.getState().documents).flatMap(document => document.documentGraphs ?? []);
  const graph = graphs.find(item => item.id === location.graphId);
  if (!graph) throw new Error(`当前打开的作者文档中不存在来源图 ${location.graphId}，请先打开该来源所属技能`);
  await projectActions.openGraph(graph, location.nodeId || undefined);
  if (location.edgeId) {
    const edge = graph.edges.find(item => item.id === location.edgeId);
    if (!edge) throw new Error(`来源图 ${location.graphId} 中不存在转移 ${location.edgeId}`);
    selectionActions.select({ kind: 'edge', id: edgeSelectId(graph, edge) });
  }
}

function LocationButton({ source, label }: { source: PreviewQuickLocation; label: string }) {
  return <Button variant="ghost" tip={source.displayPath} onClick={() => void locate(source).catch(error => consoleActions.error('快预览', error))}>{label}</Button>;
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

export function QuickPreview() {
  const { mode, state, directory } = useWorkbench(useShallow(current => ({
    mode: current.previewMode, state: current.quickPreview, directory: current.sources?.runDirectory,
  })));
  const plan = state.accepted?.plan ?? null;
  if (mode !== 'quick') return null;
  const description = state.description.kind === 'ready' ? state.description.reply.data : null;
  const job = state.job;
  const receipt = 'reply' in job ? job.reply : null;
  const status = receipt?.data;
  const inProgress = job.kind === 'starting' || job.kind === 'starting-cancelled' || job.kind === 'running' || job.kind === 'cancelling';
  const cancellable = job.kind === 'starting' || job.kind === 'running' || ((job.kind === 'failed' || job.kind === 'cancel-failed') && job.reply !== null && !job.reply.data.completed);
  const reason = quickPreview.unavailableReason();
  const sourceReason = quickPreview.sourceUnavailableReason();
  const message = state.description.kind === 'failed' || state.description.kind === 'stale' ? state.description.reason
    : sourceReason ?? reason;
  const selectedScheme = description?.root?.inputSchemes.find(item => item.index === state.scheme);
  return <section className={styles.preview} aria-label="快预览路线">
    <PanelBar wrap>
      <strong>快预览 · 路线</strong>
      {description?.root && <span>{description.root.label}</span>}
      {description?.root && (state.target?.root?.kind === 'nativeDocument' || state.target?.root?.kind === 'webEditingView') && description.root.machines.length > 1 &&
        <Select label="根状态机" value={String(state.target.root.machine)} disabled={state.description.kind === 'loading'}
          options={description.root.machines.map(machine => ({ value: String(machine.index), label: machine.entryTimeline ?? `状态机 ${machine.index}` }))}
          onChange={value => void quickPreview.readSource(Number(value))} />}
      {description?.root && <Select label="输入方案" value={state.scheme === null ? null : String(state.scheme)} placeholder="选择输入方案"
        options={description.root.inputSchemes.map(scheme => ({ value: String(scheme.index), label: scheme.name }))}
        onChange={value => quickPreview.selectScheme(Number(value))} />}
      <Button variant="ghost" disabled={sourceReason !== null || state.description.kind === 'loading' || inProgress}
        tip={sourceReason ?? '读取当前场景、作者内容与输入方案'}
        onClick={() => void quickPreview.readSource()}>读取来源</Button>
      {cancellable && <Button onClick={() => void quickPreview.cancel()}>{job.kind === 'cancel-failed' ? '重试取消' : '取消'}</Button>}
      {job.kind === 'failed' && job.reply !== null && !job.reply.data.completed && <Button onClick={() => void quickPreview.readStatus()}>重读状态</Button>}
      {inProgress && <span role="status">{job.kind === 'starting' ? '提交中' : job.kind === 'starting-cancelled' ? '等待作业身份以取消'
        : job.kind === 'cancelling' ? '取消与清理中' : status?.stage}{status ? ` ${status.samplesDone}/${status.samplesTotal}` : ''}</span>}
    </PanelBar>
    {message && <p className={styles.notice} role="status">{message}</p>}
    {description && <Facts fields={[{ displayName: '场景', value: description.scenePath },
      { displayName: '推导来源', value: description.source === null ? '尚未选根' : description.source.kind === 'webEditingView' ? '当前网页编辑内容'
        : description.source.documents.some(document => document.hasUnsavedChanges === true) ? '包含未保存修改'
        : description.source.documents.some(document => document.hasUnsavedChanges === null) ? '保存状态未提供' : '已保存作者内容' }]} />}
    <QuickInputForm key={JSON.stringify([state.target === null ? null : quickTargetKey(state.target),
      selectedScheme?.index ?? null, selectedScheme?.revision ?? null])} description={description} plan={plan} reason={reason} />
    <Fold title="来源与输出">
      <label className={styles.output}>输出目录<TextInput aria-label="快预览输出目录" value={state.outputDirectory || directory || ''}
        onChange={event => quickPreview.outputDirectory(event.currentTarget.value)} /></label>
      {description?.source && <Facts fields={[
        { displayName: '来源类型', value: description.source.kind }, { displayName: '来源路径', value: description.source.path },
        { displayName: '来源修订', value: description.source.revision }, { displayName: '场景修订', value: description.source.sceneRevision },
        { displayName: '输入方案来源', value: description.root?.inputSchemeSource ?? '未提供' },
        { displayName: '输入方案修订', value: selectedScheme?.revision ?? '未选择' },
        ...description.source.documents.map(document => ({ displayName: document.path,
          value: `${document.revision} · ${document.hasUnsavedChanges === null ? '保存状态未提供' : document.hasUnsavedChanges ? '未保存' : '已保存'} · ${document.savedRevision ?? '未提供磁盘修订'}` })),
      ]} />}
      {status && <Facts fields={[{ displayName: '作业', value: status.jobId }, { displayName: '阶段', value: status.stage },
        { displayName: '执行模式', value: status.nativeMode }, { displayName: '实际采用', value: status.adoptedMode ?? '未记录' },
        { displayName: '输出目录', value: status.outputDirectory ?? '尚未提供' }, { displayName: '摘要', value: status.summaryPath ?? '尚未提供' }]} />}
    </Fold>
    {(job.kind === 'failed' || job.kind === 'cancel-failed') && <p className={styles.notice} role="alert">{job.reason}</p>}
    {receipt && <div className={styles.notice} role={receipt.success ? 'status' : 'alert'}>
      {[...new Set([receipt.code, receipt.message, status?.error, ...(status?.cleanupErrors ?? []), ...(status?.logErrors ?? [])])]
        .filter(value => value !== null && value !== undefined && value !== '').map((value, index) => <p key={index}>{value}</p>)}
    </div>}
    {status && status.notes.length > 0 && <Fold title="推导说明">{status.notes.map((note, index) => <p key={index}>{note}</p>)}</Fold>}
    {state.accepted && <QuickRoute result={state.accepted} />}
  </section>;
}

function QuickInputForm({ description, plan, reason }: { description: PreviewQuickDescription | null; plan: PreviewQuickPlan | null; reason: string | null }) {
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  const selected = useWorkbench(state => state.quickPreview.scheme);
  const scheme = description?.root?.inputSchemes.find(item => item.index === selected);
  const [points, setPoints] = useState<{ inputId: string; time: number | null; value: string }[]>([]);
  const [conditions, setConditions] = useState<PreviewQuickConditionPoint[]>([]);
  const [inputId, setInputId] = useState<string | null>(null);
  const [conditionKey, setConditionKey] = useState<string | null>(null);
  const [rangeEnd, setRangeEnd] = useState<number | null>(null);
  const [issue, setIssue] = useState<string | null>(null);
  const editPoints = (value: typeof points) => { quickPreview.inputChanged(); setPoints(value); };
  const editConditions = (value: typeof conditions) => { quickPreview.inputChanged(); setConditions(value); };
  const editRangeEnd = (value: number | null) => { quickPreview.inputChanged(); setRangeEnd(value); };
  const generate = async () => {
    try {
      const inputPoints = points.map(point => {
        if (point.time === null) throw new Error(`填写 ${point.inputId} 的输入时间`);
        const value: unknown = JSON.parse(point.value);
        return { inputId: point.inputId, time: point.time, value };
      });
      setIssue(null);
      await quickPreview.start(inputPoints.length || conditions.length ? { points: inputPoints, conditions } : {}, rangeEnd ?? undefined);
    } catch (error) { setIssue(error instanceof Error ? error.message : String(error)); }
  };
  if (description === null || scheme === undefined) return null;
  return <>
    <PanelBar wrap><Button disabled={reason !== null} tip={reason ?? '按本次来源和输入副本推导，不保存作者输入方案'}
      onClick={() => void generate()}>推导路线</Button>
      <Tip content="留空使用原方案时长；填写只影响本次观察"><label className={styles.cursor}>结束时间
        <NumberInput value={rangeEnd} min={0} label="本次推导结束时间" onChange={editRangeEnd} /></label></Tip>
    </PanelBar>
    {issue && <p className={styles.notice} role="alert">{issue}</p>}
    <Fold title={`本次输入覆盖（${points.length + conditions.length}）`}>
      <p>这些修改只用于本次推导，原输入方案保持原样。</p>
      <PanelBar wrap>
        <Select label="输入声明" placeholder="选择输入" value={inputId}
          options={description.inputs.map(input => ({ value: input.inputId, label: `${input.inputId} · ${input.kind}${input.type ? ` (${input.type})` : ''}` }))}
          onChange={setInputId} />
        <Button disabled={inputId === null} onClick={() => { if (inputId !== null) editPoints([...points, { inputId, time: seconds, value: '' }]); }}>添加输入</Button>
      </PanelBar>
      {points.map((point, index) => <div className={styles.inputRow} key={index}>
        <span>{point.inputId}</span><NumberInput label={`${point.inputId} 时间`} value={point.time}
          onChange={time => editPoints(points.map((item, i) => i === index ? { ...item, time } : item))} />
        <TextInput aria-label={`${point.inputId} 输入值（JSON）`} placeholder="输入值（JSON）" value={point.value}
          onChange={event => editPoints(points.map((item, i) => i === index ? { ...item, value: event.currentTarget.value } : item))} />
        <Button variant="ghost" onClick={() => editPoints(points.filter((_, i) => i !== index))}>移除</Button>
      </div>)}
      <PanelBar wrap><Select label="预览条件" placeholder="先生成路线以读取条件声明" value={conditionKey}
        options={(plan?.conditions ?? []).map(condition => ({ value: condition.key, label: condition.label }))} onChange={setConditionKey} />
        <Button disabled={conditionKey === null} onClick={() => { if (conditionKey !== null) editConditions([...conditions,
          { key: conditionKey, time: seconds, satisfied: true, restoreDefault: false }]); }}>本时刻设为成立</Button>
      </PanelBar>
      {conditions.map((condition, index) => <div className={styles.inputRow} key={index}>
        <span>{plan?.conditions.find(item => item.key === condition.key)?.label ?? condition.key}</span>
        <NumberInput label="条件时间" value={condition.time} onChange={time => {
          if (time !== null) editConditions(conditions.map((item, i) => i === index ? { ...item, time } : item));
        }} />
        <label className={styles.cursor}>成立<Checkbox label="条件成立" checked={condition.satisfied}
          onChange={satisfied => editConditions(conditions.map((item, i) => i === index ? { ...item, satisfied } : item))} /></label>
        <label className={styles.cursor}>恢复默认<Checkbox label="恢复默认条件" checked={condition.restoreDefault}
          onChange={restoreDefault => editConditions(conditions.map((item, i) => i === index ? { ...item, restoreDefault } : item))} /></label>
        <Button variant="ghost" onClick={() => editConditions(conditions.filter((_, i) => i !== index))}>移除</Button>
      </div>)}
    </Fold>
  </>;
}

function QuickRoute({ result }: { result: NonNullable<QuickPreviewState['accepted']> }) {
  const plan = result.plan;
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  const stale = useWorkbench(quickPlanStale);
  const identity = useMemo(() => [
    { displayName: '结果作业', value: result.jobId }, { displayName: '结果来源修订', value: result.request.sourceRevision },
    { displayName: 'Clock', value: `${plan.clock.unit} · origin ${plan.clock.origin} · ${plan.clock.owner} · ${plan.clock.tickRate} Hz` },
    { displayName: '输入来源', value: plan.inputSource }, { displayName: '有效输入修订', value: plan.inputRevision },
    { displayName: '实际输入', value: JSON.stringify(plan.inputsUsed) }, { displayName: '实际条件覆盖', value: JSON.stringify(plan.conditionsUsed) },
  ], [result, plan]);
  const bounds = quickBounds(plan);
  const span = bounds.last - bounds.first;
  return <>
    <PanelBar wrap><strong>路线推导{stale ? '（已过期）' : ''}</strong><span>{plan.status} · {plan.duration.toFixed(3)} {plan.clock.unit}</span></PanelBar>
    {stale && <p className={styles.notice} role="status">当前内容或输入已变化，新计划采用前保留上一份路线。</p>}
    <input className={styles.scrub} type="range" aria-label={`路线游标（${plan.clock.unit}）`} min={bounds.first} max={bounds.last}
      step="any" value={seconds} onChange={event => quickPreview.seek(Number(event.currentTarget.value))} />
    <div className={styles.route}>
      {plan.segments.map((segment, index) => <div className={styles.segment} key={index}>
        <div>{segment.stateSource ? <LocationButton source={segment.stateSource} label={segment.state} /> : segment.state}</div>
        <Tip content={`${segment.timeline ?? segment.timelineId ?? segment.state}\n全局 ${segment.start}–${segment.end}\n本地 ${segment.localStart}–${segment.localEnd}\n播放 ${segment.playback} · 循环 ${segment.cycle} · 出现 ${segment.occurrence}${segment.playbackCompleted ? ' · 播放完成' : ''}\n调用方 ${segment.callerPlayback} · Clip ${segment.callerClipId}`}>
          <button className={styles.interval} aria-label={`定位路线时间 ${segment.start}`} onClick={() => quickPreview.seek(segment.start)}>
            <span style={{ left: `${span === 0 ? 0 : (segment.start - bounds.first) / span * 100}%`, width: `${span === 0 ? 0 : (segment.end - segment.start) / span * 100}%` }} />
            <b>{segment.timeline ?? segment.timelineId ?? '无 Timeline'}</b>
          </button>
        </Tip>
        <span>{segment.start.toFixed(3)}–{segment.end.toFixed(3)}</span>
      </div>)}
    </div>
    {(plan.stopReason || plan.stopDetail) && <p className={styles.notice}>{[plan.stopReason, plan.stopDetail].filter(Boolean).join(' · ')}</p>}
    {plan.assumptions.length > 0 && <Fold title="推导假设">{plan.assumptions.map((assumption, index) => <p key={index}>{assumption}</p>)}</Fold>}
    <Fold title={`条件与转移建议（${plan.suggestions.length}）`}>
      {plan.conditions.map(condition => <p key={condition.key}>{condition.label} · {condition.source}{condition.undeterminable ? ' · 无法确定' : ''}</p>)}
      {plan.suggestions.map((suggestion, index) => <div key={index}>
        {suggestion.fromSource ? <LocationButton source={suggestion.fromSource} label={suggestion.fromState} /> : suggestion.fromState}
        <span> → </span>
        {suggestion.toSource ? <LocationButton source={suggestion.toSource} label={suggestion.toState} /> : suggestion.toState}
        <LocationButton source={suggestion.transitionSource} label={suggestion.transition} />
        <span>{suggestion.start}–{suggestion.end} {plan.clock.unit} · {suggestion.missing.map(condition => condition.label).join('、')}</span>
      </div>)}
    </Fold>
    <IdentityFold fields={identity} />
  </>;
}

export function QuickGraphRoute({ graph }: { graph: SkillDocumentGraph }) {
  const plan = useQuickPlan();
  const stale = useWorkbench(quickPlanStale);
  const seconds = useWorkbench(state => state.quickPreview.seconds);
  if (plan === null) return null;
  const segments = plan.segments.filter(segment => segment.stateSource?.graphId === graph.id && segment.start <= seconds && seconds < segment.end);
  return <PanelBar wrap><strong>路线推导{stale ? '（已过期）' : ''}</strong>{segments.length ? segments.map((segment, index) =>
    <Button key={index} variant="ghost" tip={`路线区间 ${segment.start}–${segment.end} ${plan.clock.unit}；不提供端口采值`}
      onClick={() => { quickPreview.seek(segment.start); if (segment.stateSource) void locate(segment.stateSource).catch(error => consoleActions.error('快预览', error)); }}>
      {segment.state} {segment.start.toFixed(3)}–{segment.end.toFixed(3)}</Button>) : <span>当前时刻该图没有推导区间</span>}
  </PanelBar>;
}
