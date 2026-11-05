import { memo, useEffect, useMemo, useRef, useState } from 'react';
import { authoringApi, businessKeySkillTarget, type BusinessKeyRenamePrepared, type BusinessKeyRenameRejection, type BusinessKeyRenameResult, type BusinessKeyTarget } from '../../api/authoring';
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
  | { kind: 'prepared'; plan: BusinessKeyRenamePrepared }
  | { kind: 'rejected'; receipt: BusinessKeyRenameRejection; entryId: number | null }
  | ({ kind: 'failed' } & Failure);

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
  const reject = (receipt: BusinessKeyRenameRejection) => setStage({ kind: 'rejected', receipt,
    entryId: consoleActions.log('error', 'Project', receipt.message, JSON.stringify(receipt)) });
  const prepare = async () => {
    prepareController.current?.abort();
    const controller = prepareController.current = new AbortController();
    setStage({ kind: 'preparing' });
    try {
      const plan = await authoringApi.renameBusinessKey({ action: 'prepare', package: target.package,
        kind: target.keyKind, key: target.key, newKey }, controller.signal);
      controller.signal.throwIfAborted();
      if (plan.success) setStage({ kind: 'prepared', plan });
      else reject(plan);
    } catch (error) { if (!isAbort(error)) setStage({ kind: 'failed', ...failureOf(error) }); }
  };
  const commit = async (plan: BusinessKeyRenamePrepared) => {
    setStage({ kind: 'committing' });
    let result: SavedRename | BusinessKeyRenameRejection;
    try { result = await authoringActions.commitBusinessKeyRename(plan); }
    catch (error) { setStage({ kind: 'failed', ...failureOf(error) }); return; }
    if (!result.success) { reject(result); return; }
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
        {item.owner}{item.path !== null && `/${item.path}`}：{item.reason}，请先按原保存或还原流程处理。
      </div>)}
      <Button disabled={conflicts.length !== 0} onClick={() => void commit(stage.plan)}>确认提交此计划</Button>
    </>}
    {stage.kind === 'committing' && <span>正在提交已确认的计划</span>}
    {stage.kind === 'refreshing' && <span>已保存，正在重新读取受影响内容</span>}
    {stage.kind === 'failed' && <>
      <ErrorLine text={stage.text} entryId={stage.entryId} />
      {stage.raw !== null && <details><summary>宿主回执</summary><pre>{stage.raw}</pre></details>}
    </>}
    {stage.kind === 'rejected' && <>
      <ErrorLine text={stage.receipt.message} entryId={stage.entryId} />
      <span>请求结果：{stage.receipt.code}</span>
      {stage.receipt.code === 'business-key-rename-io' ? <>
        <span className={styles.issue}>{stage.receipt.authorRenameState === 'recovery-required'
          ? '作者事务需要恢复，请沿原作者事务恢复流程处理。' : '作者改名未完成。'}</span>
        <span>运行内容未应用。</span>
      </> : <span>未取得作者整体提交完成回执，请处理原因后重新准备计划。</span>}
      <details><summary>宿主回执</summary><pre>{JSON.stringify(stage.receipt, null, 2)}</pre></details>
    </>}
  </div>;
}

const RenameImpact = memo(function RenameImpact({ plan }: { plan: BusinessKeyRenameResult }) {
  return <div className={styles.facts}>
    <strong>{plan.target.package} · {plan.target.keyKind} · {plan.target.key} → {plan.renamedTarget.key}</strong>
    <span>{plan.authorRenameState === 'prepared' ? '作者改名计划已准备，尚未提交。' : '作者改名已完成。'}</span>
    <span>{plan.runtimeState === 'pending'
      ? plan.saved ? '运行内容待应用：需按下列影响清单完成发布或生成，再由正式运行入口采用。'
        : '提交后，运行内容仍需按下列影响清单完成发布或生成。'
      : '本次无需应用运行内容。'}</span>
    <details><summary>计划修订</summary><code>{plan.revision}</code></details>
    <details open><summary>{plan.saved ? '已修改' : '会修改'}的文件（{plan.files.length}）</summary>
      {plan.files.map(file => <div key={file.path} className={styles.facts}>
        <code>{file.path}</code><span>字段位置：{file.members.join('、')}</span>
        <small>读取修订：<code>{file.readRevision}</code></small>
        <small>写入修订：<code>{file.writeRevision}</code></small>
      </div>)}
    </details>
    <details><summary>本次读取的文件（{plan.readFiles.length}）</summary>
      {plan.readFiles.map(file => <div key={file.path} className={styles.facts}>
        <code>{file.path}</code><small>读取修订：<code>{file.revision}</code></small>
      </div>)}
    </details>
    <details open><summary>需发布的技能（{plan.affectedSkills.length}）</summary>
      {plan.affectedSkills.map(skill => <div key={`${skill.package}/${skill.identity}`} className={styles.facts}>
        <span>{skill.identity} · {skill.package}</span><code>{skill.directory}</code>
        <Button variant="ghost" onClick={() => void authoringActions.open(businessKeySkillTarget(skill))
          .catch(error => consoleActions.error('Project', error))}>打开技能</Button>
      </div>)}
    </details>
    <details open><summary>需生成的绑定范围（{plan.affectedProfiles.length}）</summary>
      {plan.affectedProfiles.map(profile => <div key={`${profile.adapter}/${profile.package}/${profile.domain}`} className={styles.facts}>
        <span>{profile.adapter} · {profile.package} · {profile.domain}</span>
        <span>上下文：{profile.contexts.map(context => context === '' ? '包内共用绑定' : context).join('、')}</span>
        {profile.bindingFiles.map(file => <code key={file}>{file}</code>)}
      </div>)}
    </details>
    {plan.affectedProfiles.length !== 0 && <span>生成范围由回执中的适配器、作者包、领域与上下文共同确定。</span>}
  </div>;
});

export function BusinessKeyRenameReceipt({ completion, onRefreshed }: {
  completion: BusinessKeyRenameCompletion;
  onRefreshed(result: SavedRename, refresh: RefreshResult): void;
}) {
  const [reading, setReading] = useState(false);
  const { result, refresh } = completion;
  return <div className={styles.facts}>
    <RenameImpact plan={result} />
    {refresh === null ? <span>正在重新读取载体目录与受影响文档</span> : <>
      {refresh.conflicts.map(item => <div key={`${item.owner}/${item.path}`} className={styles.issue}>
        {item.owner}{item.path !== null && `/${item.path}`}：{item.reason}。网页修改已保留，文档已标记外部变化，请先处理这些修改。
      </div>)}
      {refresh.failures.map((failure, index) => <ErrorLine key={index} {...failure} />)}
      {(refresh.failures.length > 0 || refresh.conflicts.length > 0) && <Button variant="ghost" disabled={reading} onClick={() => {
        setReading(true);
        void authoringActions.refreshBusinessKeyRename(result).then(value => { onRefreshed(result, value); setReading(false); });
      }}>{reading ? '正在重新读取' : '重新读取已保存结果'}</Button>}
    </>}
  </div>;
}
