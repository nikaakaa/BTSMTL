import { useEffect, useState } from 'react';
import { authoringApi, type AuthoringTarget, type DocumentCreationCatalog } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions } from '../../state/authoring';
import type { JsonObject } from '../../state/skillJson';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, Checkbox, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { AuthoringValueContext, AuthoringValueFields } from '../inspector/FieldEditors';
import styles from './project.module.css';

export function DocumentCreationForm({ onCreated, onCancel }: { onCreated(target: AuthoringTarget): void; onCancel(): void }) {
  const [catalog, setCatalog] = useState<DocumentCreationCatalog | null>(null);
  const [configuration, setConfiguration] = useState<JsonObject>({});
  const [type, setType] = useState<string | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [busy, setBusy] = useState(false);
  const [withGraph, setWithGraph] = useState(false);
  const [graphName, setGraphName] = useState('');
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
  if (catalog === null) return <div className={styles.facts}>
    {failure ? <ErrorLine {...failure} /> : <Empty text="正在读取文档创建合同" />}
    <Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  if (!catalog.availability.available) return <div className={styles.facts}>
    <Empty text={catalog.availability.reason} /><Button variant="ghost" onClick={onCancel}>取消</Button>
  </div>;
  const selected = catalog.types.find(item => item.type === type)!;
  return <form className={styles.facts} onSubmit={event => {
    event.preventDefault();
    void (async () => {
      setBusy(true);
      setFailure(null);
      try {
        onCreated(await authoringActions.createDocument({ type: selected.type,
          parameters: { configuration, entryGraph: withGraph ? { name: graphName } : null } }));
      } catch (error) {
        setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('新建文档', error) });
      } finally { setBusy(false); }
    })();
  }}>
    <strong>新建{selected.displayName}</strong>
    <Select label="文档类型" value={type} disabled={busy} options={catalog.types.map(item => ({ value: item.type, label: item.displayName }))}
      onChange={value => { const item = catalog.types.find(item => item.type === value)!;
        setType(item.type); setConfiguration(item.configuration); setWithGraph(false); setGraphName(''); }} />
    <fieldset disabled={busy}>
      <AuthoringValueContext.Provider value={null}>
        <AuthoringValueFields label={selected.displayName} type={selected.rootType} value={configuration} graphId={null}
          identity="new-document" onSet={setConfiguration} />
      </AuthoringValueContext.Provider>
      {selected.roles.entryGraph !== undefined && <>
        <label><Checkbox label="创建默认入口图" checked={withGraph} onChange={setWithGraph} />创建默认入口图</label>
        {withGraph && <label>入口图名称<TextInput aria-label="入口图名称" required value={graphName} onChange={event => setGraphName(event.currentTarget.value)} /></label>}
      </>}
    </fieldset>
    {failure && <ErrorLine {...failure} />}
    <PanelBar><Button type="submit" disabled={busy}>{busy ? '正在创建草稿' : '创建草稿'}</Button>
      <Button type="button" variant="ghost" disabled={busy} onClick={onCancel}>取消</Button></PanelBar>
  </form>;
}
