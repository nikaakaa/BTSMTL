import { useEffect, useRef, useState } from 'react';
import type { AdapterCatalogItem, ReferenceReplacementCompleted, ReferenceReplacementInput, ReferenceReplacementPrepared, ReferenceReplacementResult } from '../../api/authoring';
import { ApiError, isAbort } from '../../api/http';
import { consoleActions } from '../../state/store';
import { PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

const catalogItemKey = (item: Pick<AdapterCatalogItem, 'adapter' | 'domain' | 'value' | 'context'>) =>
  JSON.stringify([item.adapter, item.domain, item.value, item.context]);

export type ReferenceReplacementConflict = { owner: string; path: string | null; reason: string };
type Failure = { text: string; entryId: number | null };
export type ReferenceReplacementRefresh = { conflicts: ReferenceReplacementConflict[]; failures: Failure[] };
type Stage = { kind: 'closed' | 'editing' | 'preparing' | 'committing' }
  | { kind: 'prepared'; plan: ReferenceReplacementPrepared }
  | { kind: 'refreshing'; result: ReferenceReplacementCompleted }
  | { kind: 'saved'; result: ReferenceReplacementCompleted; refresh: ReferenceReplacementRefresh }
  | { kind: 'failed'; failure: Failure; raw: string | null };

export function ResourceReferenceReplacement({ source, candidates, currentContext, conflicts, onPrepare, onCommit, onRefresh, onOpenSkill }: {
  source: Pick<AdapterCatalogItem, 'domain' | 'value' | 'display'>; candidates: AdapterCatalogItem[]; currentContext: string | null;
  conflicts(plan: ReferenceReplacementPrepared): ReferenceReplacementConflict[];
  onPrepare(input: ReferenceReplacementInput, signal: AbortSignal): Promise<ReferenceReplacementPrepared>;
  onCommit(plan: ReferenceReplacementPrepared): Promise<ReferenceReplacementCompleted>;
  onRefresh(result: ReferenceReplacementCompleted): Promise<ReferenceReplacementRefresh>;
  onOpenSkill(skill: string): void;
}) {
  const [stage, setStage] = useState<Stage>({ kind: 'closed' });
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [scope, setScope] = useState<'current' | 'all'>(currentContext === null ? 'all' : 'current');
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  useEffect(() => {
    controller.current?.abort();
    setSelectedKey(null);
    setScope(currentContext === null ? 'all' : 'current');
    setStage({ kind: 'closed' });
  }, [source.domain, source.value, currentContext]);
  const context = scope === 'current' ? currentContext : null;
  const choices = candidates.filter(item => item.domain === source.domain && item.value !== source.value &&
    (context === null || item.context === '' || item.context === context));
  const selected = choices.find(item => catalogItemKey(item) === selectedKey);
  const issues = stage.kind === 'prepared' ? conflicts(stage.plan) : [];
  const busy = stage.kind === 'committing' || stage.kind === 'refreshing';
  const fail = (error: unknown) => setStage({ kind: 'failed', failure: {
    text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('引用替换', error),
  }, raw: error instanceof ApiError ? error.raw : null });
  const edit = () => { controller.current?.abort(); setStage({ kind: 'editing' }); };
  const prepare = async (selected: AdapterCatalogItem) => {
    controller.current?.abort();
    const pending = controller.current = new AbortController();
    setStage({ kind: 'preparing' });
    try {
      const plan = await onPrepare({ domain: source.domain, oldValue: source.value, newValue: selected.value, context }, pending.signal);
      pending.signal.throwIfAborted();
      setStage({ kind: 'prepared', plan });
    } catch (error) { if (!isAbort(error)) fail(error); }
  };
  const refresh = async (result: ReferenceReplacementCompleted) => {
    setStage({ kind: 'refreshing', result });
    const refreshed = await onRefresh(result);
    setStage({ kind: 'saved', result, refresh: refreshed });
  };
  const commit = async (plan: ReferenceReplacementPrepared) => {
    setStage({ kind: 'committing' });
    let result: ReferenceReplacementCompleted;
    try { result = await onCommit(plan); }
    catch (error) { fail(error); return; }
    await refresh(result);
  };
  if (stage.kind === 'closed') return <Button variant="ghost" tip="把所有用到这个资源的地方，统一换成另一个同类资源" onClick={edit}>开始替换</Button>;
  const result = stage.kind === 'prepared' ? stage.plan : stage.kind === 'saved' || stage.kind === 'refreshing' ? stage.result : null;
  const saved = stage.kind === 'saved' || stage.kind === 'refreshing';
  return <div className={styles.facts}>
    <strong>把「{source.display}」的所有引用换成：</strong>
    {!saved && <>
      <Select<'current' | 'all'> label="替换范围" value={scope} disabled={busy} options={[
        ...(currentContext === null ? [] : [{ value: 'current' as const, label: '只替换当前角色里的引用' }]),
        { value: 'all', label: '替换所有角色里的引用' },
      ]} onChange={value => { setScope(value); setSelectedKey(null); edit(); }} />
      <Select label="替换为" value={selected === undefined ? null : selectedKey} placeholder="选择同类资源" disabled={busy}
        options={choices.map(item => ({ value: catalogItemKey(item), label: `${item.display}（${item.value}）` }))}
        onChange={value => { setSelectedKey(value); edit(); }} />
      <PanelBar><Button disabled={busy || selected === undefined || stage.kind === 'preparing'} onClick={selected === undefined ? undefined : () => void prepare(selected)}>
        {stage.kind === 'preparing' ? '正在检查影响范围' : '检查影响范围'}</Button>
        <Button variant="ghost" disabled={busy} onClick={() => { controller.current?.abort(); setStage({ kind: 'closed' }); }}>取消</Button></PanelBar>
    </>}
    {result !== null && <ReferenceReplacementImpact plan={result} onOpenSkill={onOpenSkill} />}
    {issues.map(item => <div key={JSON.stringify([item.owner, item.path])} className={styles.issue}>
      {item.owner}{item.path === null ? '' : `/${item.path}`}：{item.reason}，请先按原保存或还原流程处理。
    </div>)}
    {stage.kind === 'prepared' && <Button disabled={issues.length !== 0} onClick={() => void commit(stage.plan)}>提交此引用替换计划</Button>}
    {stage.kind === 'committing' && <span role="status">正在提交已准备的作者引用替换</span>}
    {stage.kind === 'refreshing' && <span role="status">作者引用已替换，正在读取受影响的已打开文档</span>}
    {stage.kind === 'failed' && <><ErrorLine {...stage.failure} />
      {stage.raw !== null && <details><summary>实际 Host 回执</summary><pre>{stage.raw}</pre></details>}</>}
    {stage.kind === 'saved' && <>
      {stage.refresh.conflicts.map(item => <div key={JSON.stringify([item.owner, item.path])} className={styles.issue}>
        {item.owner}{item.path === null ? '' : `/${item.path}`}：{item.reason}。网页草稿已保留，需先处理外部变化。
      </div>)}
      {stage.refresh.failures.map((failure, index) => <ErrorLine key={index} {...failure} />)}
      {(stage.refresh.conflicts.length !== 0 || stage.refresh.failures.length !== 0) &&
        <Button variant="ghost" onClick={() => void refresh(stage.result)}>重新读取已保存结果</Button>}
    </>}
  </div>;
}

export function ReferenceReplacementImpact({ plan, onOpenSkill }: { plan: ReferenceReplacementResult; onOpenSkill(skill: string): void }) {
  return <div className={styles.facts}>
    <strong>{plan.oldValue} → {plan.newValue}</strong>
    <span>{plan.saved ? '作者引用已替换。' : '引用替换计划已准备，尚未写入作者文件。'}</span>
    <span>{plan.runtimeState === 'pending' ? '运行内容待应用，请通过原发布和采用入口继续。' : '本次没有需要应用运行内容的技能。'}</span>
    <details><summary>引用替换修订</summary><code>{plan.revision}</code></details>
    <details open><summary>{plan.saved ? '已修改' : '会修改'}的文件 · {plan.files.length}</summary>
      {plan.files.map(file => <div className={styles.facts} key={file.path}>
        <code>{file.path}</code><span>字段位置：{file.members.join('、')}</span>
        <small>读取修订：{file.readRevision}</small><small>写入修订：{file.writeRevision}</small>
      </div>)}
    </details>
    <details><summary>计划读取的文件 · {plan.readFiles.length}</summary>
      {plan.readFiles.map(file => <div className={styles.facts} key={file.path}><code>{file.path}</code><small>{file.revision}</small></div>)}
    </details>
    <details open><summary>受影响技能 · {plan.affectedSkills.length}</summary>
      {plan.affectedSkills.map(skill => <Button key={skill} variant="ghost" onClick={() => onOpenSkill(skill)}>{skill}</Button>)}
    </details>
  </div>;
}
