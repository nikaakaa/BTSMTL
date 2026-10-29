import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Comparison } from '../../api/types';
import { queryService } from '../../state/query';
import { runLabel } from '../../state/session';
import { consoleActions, useWorkbench } from '../../state/store';
import { Empty, Facts, Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { JsonLines } from './records';
import styles from './records.module.css';

// 运行比较面板（承接原设置弹窗的“运行比较”）：结果在后台线程解析，原始结果用虚拟列表显示。

export function ComparePanel() {
  const { runId, runs, sealed, frameName } = useWorkbench(useShallow(state => ({
    runId: state.runId, runs: state.runs, sealed: state.sealed, frameName: state.summary?.frameDisplayName ?? '帧',
  })));
  const [right, setRight] = useState<string | null>(null);
  const [result, setResult] = useState<Comparison | null>(null);
  const [failure, setFailure] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => { setResult(null); setFailure(null); }, [runId, right]);

  if (!runId) return <Empty text="选择一个运行" />;
  const others = runs.filter(run => run.jobId !== runId && run.stage === 'completed');
  const blocked = !sealed ? '当前运行尚未封存' : !right ? '选择对照运行' : null;
  const compare = async () => {
    setBusy(true);
    setFailure(null);
    try {
      setResult(await queryService.compare(runId, right!));
    } catch (error) {
      setFailure(consoleActions.error('compare', error));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className={styles.root}>
      <PanelBar>
        <Select label="对照运行" placeholder="对照运行" value={right} disabled={!others.length}
          tip={others.length ? undefined : '没有其它已封存运行'}
          options={others.map(run => ({ value: run.jobId, label: runLabel(run) }))} onChange={setRight} />
        <Button variant="ghost" disabled={!!blocked || busy} tip={blocked} onClick={() => void compare()}>比较</Button>
      </PanelBar>
      {failure !== null && <ErrorLine text="比较失败" entryId={failure} />}
      {!result ? <Empty text={busy ? '比较中' : '选择对照运行后比较'} /> : (
        <div className={styles.body}>
          <div className={styles.result}>
            <div className={!result.comparable ? undefined : result.equal ? styles.equal : styles.different}>
              {!result.comparable ? '运行条件不同，未比较一致性值'
                : result.equal ? `已比较 ${result.comparedFrames} 帧，记录范围内一致`
                  : `首个差异在${frameName} ${result.firstDifference?.hostFrame ?? ''}`}
            </div>
            <Facts fields={[
              { displayName: '比较帧数', value: String(result.comparedFrames) },
              { displayName: '通道', value: result.channels.join('、') || '无' },
              ...(result.firstDifference ? [{ displayName: '差异原因', value: result.firstDifference.reason }] : []),
            ]} />
          </div>
          <Fold title="原始结果" kind="var(--node-flow)" defaultOpen>
            <div className={styles.rawBox}><JsonLines text={result.rawJson} /></div>
          </Fold>
        </div>
      )}
    </div>
  );
}
