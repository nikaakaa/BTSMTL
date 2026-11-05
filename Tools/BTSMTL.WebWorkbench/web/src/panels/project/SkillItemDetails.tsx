import { useEffect, useState } from 'react';
import { authoringApi, authoringTargetKey, type ProjectItem, type ReferrersResult, type SkillDeleteResult } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions } from '../../state/store';
import { PanelBar } from '../../ui/blocks';
import { Button, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

export const kindNames: Record<ProjectItem['kind'], string> = { skill: '技能', graph: '节点图', timeline: 'Timeline', 'input-plan': '输入方案' };
export const keyOf = (item: ProjectItem) => JSON.stringify([item.owner, item.kind, item.identity]);

// Project 文件夹中选中技能作者文件时的详情：打开、重命名、带引用检查的删除与引用者。
export function SkillItemDetails({ item, items, stamp, onChanged }: { item: ProjectItem; items: ProjectItem[]; stamp: string; onChanged(): void }) {
  const [referrers, setReferrers] = useState<ReferrersResult | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const [deletion, setDeletion] = useState<Extract<SkillDeleteResult, { code: 'skill-referenced' }> | null>(null);
  const pendingDocument = useAuthoring(state => {
    const document = state.documents[authoringTargetKey({ kind: 'skill', name: item.owner })];
    return document?.loading === true || document?.saving === true;
  });
  useEffect(() => {
    const controller = new AbortController();
    setReferrers(null);
    setDeletion(null);
    setFailure(null);
    authoringApi.referrers({ kind: 'skill', name: item.owner }, { identity: item.identity }, authoringActions.editingView(), controller.signal).then(setReferrers, error => {
      if (!isAbort(error)) setFailure({ text: '引用者读取失败', entryId: consoleActions.error('Project', error) });
    });
    return () => controller.abort();
  }, [item.owner, item.identity, stamp]);
  const perform = async (action: () => Promise<void>, refresh = true) => {
    setBusy(true);
    setFailure(null);
    try { await action(); if (refresh) onChanged(); }
    catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) }); }
    finally { setBusy(false); }
  };
  return <div className={styles.details}>
    <PanelBar>
      <Button variant="ghost" disabled={busy} onClick={() => void perform(() => projectActions.open(item))}>打开</Button>
      <Button variant="ghost" disabled={busy} onClick={() => setRenaming(!renaming)}>重命名</Button>
      {item.kind === 'skill' ? <Button variant="ghost" disabled={busy || pendingDocument}
        tip={pendingDocument ? '等待此技能的读取或保存完成' : '删除整个技能目录；宿主会检查磁盘修订、外部引用、Definition 授予和共享内容'}
        onClick={() => void perform(async () => {
          setDeletion(null);
          const result = await projectActions.removeSkill(item);
          if (result.deleted) {
            consoleActions.log('info', 'Project', `${item.name} 技能目录已删除`, JSON.stringify(result));
            onChanged();
          }
          else {
            if (result.code === 'skill-referenced') setDeletion(result);
            setFailure({ text: result.message, entryId: consoleActions.log('error', 'Project', result.message, JSON.stringify(result)) });
          }
        }, false)}>删除技能目录</Button> : <Button variant="ghost" disabled={busy || referrers === null || !referrers.complete || referrers.items.length !== 0}
        tip={referrers !== null && !referrers.complete ? '查询尚未完整，先处理未能确认的引用' : referrers?.items.length ? '仍有引用，先在引用者中解除引用' : '删除加入所属技能的未保存修改，可撤销'}
        onClick={() => void perform(async () => { setReferrers(await projectActions.remove(item)); })}>删除</Button>}
    </PanelBar>
    {failure && <ErrorLine {...failure} />}
    <div className={styles.facts}>
      <strong>{item.name}</strong>
      <span>{kindNames[item.kind]} · {item.ownerName} · {item.ownership === 'Shared' ? '共享' : '私有'}</span>
      <code>{item.file}</code>
      <small>{item.identity}</small>
    </div>
    {renaming && <RenameItem item={item} busy={busy} onRename={(name, scheme) => void perform(async () => {
      await projectActions.rename(item, name, scheme); setRenaming(false);
    })} />}
    {item.kind === 'input-plan' && <div className={styles.facts}>
      {item.schemes.map(scheme => <Button key={scheme.index} variant="ghost" disabled={busy}
        onClick={() => void perform(() => projectActions.open(item, scheme.index))}>打开 {scheme.name}</Button>)}
    </div>}
    {deletion && <>
      <div className={styles.heading}>阻止删除的引用 · {deletion.references.length} 项</div>
      {deletion.references.map((reference, index) => {
        const source = items.find(value => value.owner === reference.owner && value.file === reference.file);
        return <div key={index} className={styles.facts}>
          <strong>{reference.ownerName}</strong>
          <span>{reference.kind === 'carrier-grant' ? 'Definition 技能授予' : '作者内容引用'} · {reference.view === 'saved' ? '已保存' : '未保存编辑'}</span>
          <code>{reference.file} · {reference.path}</code>
          {reference.kind !== 'carrier-grant' && source && <Button variant="ghost" disabled={busy}
              onClick={() => void perform(() => projectActions.open(source))}>打开引用者</Button>}
        </div>;
      })}
      {deletion.shared.length > 0 && <div className={styles.heading}>技能拥有的共享内容 · {deletion.shared.length} 项</div>}
      {deletion.shared.map(shared => <div key={keyOf(shared)} className={styles.facts}>
        <strong>{shared.name}</strong><span>{kindNames[shared.kind]} · {shared.ownerName}</span><code>{shared.file}</code>
        <Button variant="ghost" disabled={busy}
          onClick={() => void perform(() => projectActions.open(shared))}>打开共享内容</Button>
      </div>)}
    </>}
    {!deletion && <>
    <div className={styles.heading}>引用者{referrers === null ? ' · 读取中' : ` · 已确认 ${referrers.items.length} 项${referrers.complete ? '' : ' · 查询未完整'}`}</div>
    {referrers?.complete && referrers.items.length === 0 && <div className={styles.note}>没有引用者</div>}
    {referrers?.unresolved.map((issue, index) => <div key={index} className={styles.facts}>
      <span className={styles.issue}>{issue.reason}</span>
      {'owner' in issue && <small>{issue.ownerName} · {issue.file} · {issue.path}</small>}
    </div>)}
    {referrers?.items.map((reference, index) => {
      const source = items.find(value => value.owner === reference.owner && value.file === reference.file);
      return <button key={index} className={styles.referrer} disabled={busy || (!('graphId' in reference) && !source)}
        onClick={() => void perform(async () => {
          if ('graphId' in reference) {
            await projectActions.openGraph({ owner: reference.owner, ownerName: reference.ownerName, id: reference.graphId }, reference.nodeId);
          } else if (source) await projectActions.open(source);
        })}>
        <span>{reference.ownerName} · {source?.name ?? reference.file}</span><small>{reference.path}</small>
        {'contextOwnerName' in reference && <small>调用上下文：{reference.contextOwnerName} · {reference.fieldId}</small>}
      </button>;
    })}
    </>}
  </div>;
}

function RenameItem({ item, busy, onRename }: { item: ProjectItem; busy: boolean; onRename(name: string, scheme: number | null): void }) {
  const [scheme, setScheme] = useState<number | null>(item.kind === 'input-plan' && item.schemes.length === 1 ? item.schemes[0].index : null);
  const [name, setName] = useState(item.kind === 'input-plan' ? item.schemes.find(value => value.index === scheme)?.name ?? '' : item.name);
  return <div className={styles.facts}>
    {item.kind === 'input-plan' && <Select label="要重命名的输入方案" placeholder="选择方案" value={scheme === null ? null : String(scheme)}
      options={item.schemes.map(value => ({ value: String(value.index), label: value.name }))} onChange={value => {
        const index = Number(value); setScheme(index); setName(item.schemes.find(entry => entry.index === index)!.name);
      }} />}
    <TextInput autoFocus aria-label="内容名称" value={name} onChange={event => setName(event.currentTarget.value)} />
    <Button disabled={busy || !name.trim() || (item.kind === 'input-plan' && scheme === null)} onClick={() => onRename(name, scheme)}>应用名称</Button>
  </div>;
}
