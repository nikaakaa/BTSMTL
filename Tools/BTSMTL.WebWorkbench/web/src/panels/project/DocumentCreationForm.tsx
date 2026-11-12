import { useEffect, useState } from 'react';
import { authoringApi, type AuthoringTarget, type DocumentCreationCatalog } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, skillElements, useAuthoring } from '../../state/authoring';
import { inputAuthoring } from '../../state/authoringInputs';
import { quickPreview } from '../../state/quickPreview';
import type { JsonObject } from '../../state/skillJson';
import { consoleActions, selectionActions } from '../../state/store';
import { Empty, Fold, PanelBar } from '../../ui/blocks';
import { Button, Checkbox, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { AuthoringValueContext, AuthoringValueFields } from '../inspector/FieldEditors';
import styles from './project.module.css';

// 脚手架：作者只填名称并选参照文档；带入口图的文档同时建入口图、一条 Timeline 和它的输入轨，建完即可预览。
// 其余字段收在「更多设置」里，走同一个创建请求。
export function DocumentCreationForm({ onCreated, onCancel }: { onCreated(target: AuthoringTarget, timeline: string | null): void; onCancel(): void }) {
  const [catalog, setCatalog] = useState<DocumentCreationCatalog | null>(null);
  const [configuration, setConfiguration] = useState<JsonObject>({});
  const [type, setType] = useState<string | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const [scaffold, setScaffold] = useState(true);
  const [reference, setReference] = useState<string | null>(null);
  const directory = useAuthoring(state => state.directory);
  const current = useAuthoring(state => state.target);
  useEffect(() => {
    const controller = new AbortController();
    authoringApi.creationOptions(controller.signal).then(value => {
      setCatalog(value);
      setType(value.types[0].type);
      setConfiguration(value.types[0].configuration);
    }, error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('新建文档', error) });
    });
    return () => controller.abort();
  }, []);
  const references = (directory ?? []).filter(document => document.kind === type);
  useEffect(() => {
    setReference(references.find(document => document.documentPath === current?.documentPath)?.documentPath ?? references[0]?.documentPath ?? null);
  }, [type, directory === null]);
  if (catalog === null) return <div className={styles.facts}>
    {failure ? <ErrorLine {...failure} /> : <Empty text="正在读取文档创建合同" />}
    <Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  if (!catalog.availability.available) return <div className={styles.facts}>
    <Empty text={catalog.availability.reason} /><Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  const selected = catalog.types.find(item => item.type === type)!;
  const nameMember = selected.roles.displayName;
  const name = nameMember === undefined ? '' : String(configuration[nameMember] ?? '').trim();
  const withEntry = selected.roles.entryGraph !== undefined;
  const ready = !busy && (nameMember === undefined || name.length > 0);
  return <form className={styles.facts} onSubmit={event => {
    event.preventDefault();
    if (!ready) return;
    void (async () => {
      setBusy(true);
      setFailure(null);
      try {
        const target = await authoringActions.createDocument({ type: selected.type, parameters: {
          configuration, reference: selected.reference ? reference : null,
          entryGraph: withEntry && scaffold ? { name: `${name} 入口` } : null } }, selected.roles);
        let timeline: string | null = null;
        if (withEntry && scaffold) {
          const elements = await skillElements();
          timeline = authoringActions.createTimeline(elements.timeline, name);
          if (elements.previewInput.availability.available)
            await inputAuthoring.createPlan('默认', { kind: 'timeline', timeline });
          selectionActions.showTimeline(timeline, null, null);
          void quickPreview.readSource({ documentPath: target.documentPath, savedRevision: '', entry: { kind: 'timeline', timelineId: timeline } });
        }
        onCreated(target, timeline);
      } catch (error) {
        setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('新建文档', error) });
      } finally { setBusy(false); }
    })();
  }}>
    <strong>新建{selected.displayName}</strong>
    <Select label="文档类型" value={type} disabled={busy} options={catalog.types.map(item => ({ value: item.type, label: item.displayName }))}
      onChange={value => { const item = catalog.types.find(item => item.type === value)!; setType(item.type); setConfiguration(item.configuration); }} />
    {nameMember !== undefined && <TextInput autoFocus aria-label="名称" placeholder="名称" value={String(configuration[nameMember] ?? '')} disabled={busy}
      onChange={event => setConfiguration({ ...configuration, [nameMember]: event.currentTarget.value })} />}
    {selected.reference && <Select label="参照" tip="新文档沿用参照文档的准入与排序等设置" placeholder="不参照" value={reference} disabled={busy}
      options={references.map(document => ({ value: document.documentPath, label: document.name }))} onChange={setReference} />}
    {withEntry && <label><Checkbox label="同时创建入口图、Timeline 与输入轨" checked={scaffold} onChange={setScaffold} />同时创建入口图、Timeline 与输入轨</label>}
    <fieldset disabled={busy}>
      <Fold title="更多设置" defaultOpen={false}>
        <AuthoringValueContext.Provider value={null}>
          <AuthoringValueFields label={selected.displayName} type={selected.rootType} value={configuration} graphId={null}
            identity="new-document" onSet={setConfiguration} />
        </AuthoringValueContext.Provider>
      </Fold>
    </fieldset>
    {failure && <ErrorLine {...failure} />}
    <PanelBar><Button type="submit" disabled={!ready}>{busy ? '正在创建' : '创建'}</Button>
      <Button type="button" variant="ghost" disabled={busy} onClick={onCancel}>取消</Button></PanelBar>
  </form>;
}
