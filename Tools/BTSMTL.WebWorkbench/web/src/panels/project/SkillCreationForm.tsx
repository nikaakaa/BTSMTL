import { useEffect, useState } from 'react';
import { authoringApi, type AuthoringTarget, type SkillCreationCatalog } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, Checkbox, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

export function SkillCreationForm({ onCreated, onCancel }: { onCreated(target: AuthoringTarget): void; onCancel(): void }) {
  const [catalog, setCatalog] = useState<SkillCreationCatalog | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const [container, setContainer] = useState<string | null>(null);
  const [profile, setProfile] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [abilityId, setAbilityId] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [candidateOrder, setCandidateOrder] = useState('');
  const [sourceOrder, setSourceOrder] = useState('');
  const [withGraph, setWithGraph] = useState(false);
  const [graphName, setGraphName] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    authoringApi.skillCreationCatalog(controller.signal).then(setCatalog, error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) });
    });
    return () => controller.abort();
  }, []);
  if (catalog === null) return <div className={styles.facts}>
    {failure ? <ErrorLine {...failure} /> : <Empty text="正在读取可用技能容器与准入配置" />}
    <Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  if (!catalog.availability.available) return <div className={styles.facts}>
    <Empty text={catalog.availability.reason} />
    <Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  return <form className={styles.facts} onSubmit={event => {
    event.preventDefault();
    void (async () => {
      setBusy(true);
      setFailure(null);
      try {
        const target = await authoringActions.createSkill({
          parameters: {
            containerIdentity: container!, name, abilityId, displayName, admissionProfile: profile!,
            candidateOrder: Number(candidateOrder), sourceOrder: Number(sourceOrder),
            entryGraph: withGraph ? { name: graphName } : null, startupInputs: [], trajectory: null,
          },
          references: catalog.references,
        });
        onCreated(target);
      } catch (error) {
        setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) });
      } finally { setBusy(false); }
    })();
  }}>
    <strong>新建技能</strong>
    <Select label="技能容器" placeholder="选择未绑定作者来源的容器" value={container} disabled={busy}
      options={catalog.containers.map(value => ({ value: value.identity, label: `${value.name} · ${value.assetPath}` }))} onChange={setContainer} />
    {catalog.containers.length === 0 && <small>项目中没有尚未绑定作者来源的技能容器。</small>}
    <label>目录与技能名称<TextInput aria-label="目录与技能名称" required value={name} disabled={busy} onChange={event => setName(event.currentTarget.value)} /></label>
    <label>技能 ID<TextInput aria-label="技能 ID" required value={abilityId} disabled={busy} onChange={event => setAbilityId(event.currentTarget.value)} /></label>
    <label>显示名称<TextInput aria-label="显示名称" required value={displayName} disabled={busy} onChange={event => setDisplayName(event.currentTarget.value)} /></label>
    <Select label="准入配置" placeholder="选择准入配置" value={profile} disabled={busy}
      options={catalog.admissionProfiles.map(value => ({ value: value.identity, label: `${value.name} · ${value.assetPath}` }))} onChange={setProfile} />
    {catalog.admissionProfiles.length === 0 && <small>项目中没有可选的准入配置。</small>}
    <label>候选排序<TextInput aria-label="候选排序" type="number" step="1" required value={candidateOrder} disabled={busy} onChange={event => setCandidateOrder(event.currentTarget.value)} /></label>
    <label>来源排序<TextInput aria-label="来源排序" type="number" step="1" required value={sourceOrder} disabled={busy} onChange={event => setSourceOrder(event.currentTarget.value)} /></label>
    <label><Checkbox label="创建入口图" checked={withGraph} disabled={busy} onChange={setWithGraph} />创建入口图</label>
    {withGraph && <label>入口图名称<TextInput aria-label="入口图名称" required value={graphName} disabled={busy} onChange={event => setGraphName(event.currentTarget.value)} /></label>}
    <small>先创建未保存草稿；启动输入为 0 项，轨迹模块未声明。</small>
    {failure && <ErrorLine {...failure} />}
    <PanelBar>
      <Button type="submit" disabled={busy || container === null || profile === null}>{busy ? '正在创建草稿' : '创建草稿'}</Button>
      <Button type="button" variant="ghost" disabled={busy} onClick={onCancel}>取消</Button>
    </PanelBar>
  </form>;
}
