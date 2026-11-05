import { memo, useEffect, useMemo, useRef, useState } from 'react';
import { authoringApi, type BusinessKeyRenameResult, type BusinessKeyTarget } from '../../api/authoring';
import { ApiError, isAbort } from '../../api/http';
import { authoringActions, businessKeyMigrationConflicts, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { PanelBar } from '../../ui/blocks';
import { Button, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

type SavedRename = Extract<BusinessKeyRenameResult, { saved: true }>;
type RefreshResult = Awaited<ReturnType<typeof authoringActions.refreshBusinessKeyRename>>;
export type BusinessKeyRenameCompletion = { result: SavedRename; refresh: RefreshResult | null };
type Failure = { text: string; entryId: number | null; raw: string | null };
type Stage = { kind: 'closed' | 'editing' | 'preparing' | 'committing' | 'refreshing' }
  | { kind: 'prepared'; plan: BusinessKeyRenameResult } | ({ kind: 'failed' } & Failure);

const failureOf = (error: unknown): Failure => ({
  text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error),
  raw: error instanceof ApiError ? error.raw : null,
});

export function BusinessKeyRename({ target, onSaved, onRefreshed }: {
  target: BusinessKeyTarget;
  onSaved(result: SavedRename): void;
  onRefreshed(result: SavedRename, refresh: RefreshResult): void;
}) {
  const [newKey, setNewKey] = useState(target.key);
  const [stage, setStage] = useState<Stage>({ kind: 'closed' });
  const prepareController = useRef<AbortController | null>(null);
  const authoring = useAuthoring();
  const conflicts = useMemo(() => stage.kind === 'prepared' ? businessKeyMigrationConflicts(stage.plan, authoring) : [], [stage, authoring]);
  useEffect(() => () => prepareController.current?.abort(), []);
  const prepare = async () => {
    prepareController.current?.abort();
    const controller = prepareController.current = new AbortController();
    setStage({ kind: 'preparing' });
    try {
      const plan = await authoringApi.renameBusinessKey({ action: 'prepare', package: target.package,
        kind: target.keyKind, key: target.key, newKey }, controller.signal);
      controller.signal.throwIfAborted();
      setStage({ kind: 'prepared', plan });
    } catch (error) { if (!isAbort(error)) setStage({ kind: 'failed', ...failureOf(error) }); }
  };
  const commit = async (plan: BusinessKeyRenameResult) => {
    setStage({ kind: 'committing' });
    let result: BusinessKeyRenameResult;
    try { result = await authoringActions.commitBusinessKeyRename(plan); }
    catch (error) { setStage({ kind: 'failed', ...failureOf(error) }); return; }
    if (!result.saved) {
      setStage({ kind: 'failed', text: '宿主未确认保存，请重新准备计划。', raw: JSON.stringify(result, null, 2),
        entryId: consoleActions.log('error', 'Project', '业务键改名未保存', JSON.stringify(result)) });
      return;
    }
    setStage({ kind: 'refreshing' });
    onSaved(result);
    const refresh = await authoringActions.refreshBusinessKeyRename(result);
    onRefreshed(result, refresh);
    setStage({ kind: 'closed' });
  };
  if (stage.kind === 'closed') return <Button variant="ghost" onClick={() => setStage({ kind: 'editing' })}>改名业务键</Button>;
  const busy = stage.kind === 'committing' || stage.kind === 'refreshing';
  return <div className={styles.facts}>
    <strong>改名业务键</strong>
    <span>{target.package} · {target.keyKind} · 原键：{target.key}</span>
    <TextInput aria-label="新业务键" value={newKey} disabled={busy} onChange={event => {
      prepareController.current?.abort();
      setNewKey(event.currentTarget.value);
      setStage({ kind: 'editing' });
    }} />
    <PanelBar>
      <Button disabled={busy || stage.kind === 'preparing'} onClick={() => void prepare()}>
        {stage.kind === 'preparing' ? '正在准备计划' : '准备改名计划'}
      </Button>
      <Button variant="ghost" disabled={busy} onClick={() => { prepareController.current?.abort(); setStage({ kind: 'closed' }); }}>取消</Button>
    </PanelBar>
    {stage.kind === 'prepared' && <>
      <RenameImpact plan={stage.plan} />
      {conflicts.map(item => <div key={`${item.owner}/${item.path}`} className={styles.issue}>
        {item.owner}/{item.path}：{item.reason}，请先按原保存或还原流程处理。
      </div>)}
      <Button disabled={conflicts.length !== 0} onClick={() => void commit(stage.plan)}>确认提交此计划</Button>
    </>}
    {stage.kind === 'committing' && <span>正在提交已确认的计划</span>}
    {stage.kind === 'refreshing' && <span>已保存，正在重新读取受影响内容</span>}
    {stage.kind === 'failed' && <>
      <ErrorLine text={stage.text} entryId={stage.entryId} />
      {stage.raw !== null && <details><summary>宿主回执</summary><pre>{stage.raw}</pre></details>}
    </>}
  </div>;
}

const RenameImpact = memo(function RenameImpact({ plan }: { plan: BusinessKeyRenameResult }) {
  return <div className={styles.facts}>
    <strong>{plan.key} → {plan.newKey}</strong>
    <span>{plan.files.length} 个作者文件</span>
    <details open><summary>会修改的文件（{plan.files.length}）</summary>
      {plan.files.map(file => <div key={file.path} className={styles.facts}>
        <code>{file.path}</code><span>{file.members.join('、')}</span>
      </div>)}
    </details>
  </div>;
});

export function BusinessKeyRenameReceipt({ completion, onRefreshed }: {
  completion: BusinessKeyRenameCompletion;
  onRefreshed(result: SavedRename, refresh: RefreshResult): void;
}) {
  const [reading, setReading] = useState(false);
  const { result, refresh } = completion;
  return <div className={styles.facts}>
    <strong>已保存：{result.key} → {result.newKey}</strong>
    <span>尚未取得运行内容的发布、生成或采用回执。</span>
    {refresh === null ? <span>正在重新读取载体目录与受影响文档</span> : <>
      {refresh.conflicts.map(item => <div key={`${item.owner}/${item.path}`} className={styles.issue}>
        {item.owner}/{item.path}：{item.reason}。网页修改已保留，文档已标记外部变化，请先处理这些修改。
      </div>)}
      {refresh.failures.map((failure, index) => <ErrorLine key={index} {...failure} />)}
      {(refresh.failures.length > 0 || refresh.conflicts.length > 0) && <Button variant="ghost" disabled={reading} onClick={() => {
        setReading(true);
        void authoringActions.refreshBusinessKeyRename(result).then(value => { onRefreshed(result, value); setReading(false); });
      }}>{reading ? '正在重新读取' : '重新读取已保存结果'}</Button>}
    </>}
  </div>;
}
