import { useShallow } from 'zustand/react/shallow';
import { session, stageNames } from '../../state/session';
import { contentActions, useWorkbench } from '../../state/store';
import { PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import styles from './quickPreview.module.css';

// 非快速预览时 Timeline 顶部的入口：说明当前看的是什么，并给出新建运行或改用快速预览。
export function RunBar() {
  const { mode, runId, job, stage } = useWorkbench(useShallow(state => ({
    mode: state.host?.modes.find(item => item.id === state.previewMode) ?? null,
    runId: state.runId,
    job: state.archiveJobs.find(item => item.jobId === state.runId) ?? null,
    stage: state.receipt?.stage ?? null,
  })));
  const startBlocked = useWorkbench(() => session.canStartNewRun());
  const quick = useWorkbench(state => state.host?.modes.find(item => item.id === 'quick') ?? null);
  const status = runId === null
    ? mode?.createsRun ? '还没有运行。新建一次运行后，这里按时间显示实际经过的状态和 Timeline。'
      : '当前不预览，只编辑作者内容。'
    : `${job === null ? '所选运行' : new Date(job.acceptedAt).toLocaleString()} · ${stageNames[stage ?? job?.stage ?? 'queued']}`;
  return <section className={styles.bar} aria-label="运行">
    <PanelBar wrap>
      <strong>{mode?.displayName ?? '预览'}</strong>
      <span role="status" className={styles.status}>{status}</span>
      <span className={styles.grow} />
      {runId !== null && <Button variant="ghost" onClick={() => session.selectRun(null)}>退出回看</Button>}
      <Button disabled={startBlocked !== null} tip={startBlocked ?? '按当前场景和输入方案运行一次并录制'}
        onClick={() => void session.startNewRun()}>新建运行</Button>
      {quick !== null && <Button variant="ghost" disabled={!quick.available} tip={quick.reason}
        onClick={() => contentActions.selectPreviewMode(quick.id)}>改用{quick.displayName}</Button>}
    </PanelBar>
  </section>;
}
