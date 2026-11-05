import { useEffect, useState } from 'react';
import { authoringApi, type TypeDescription } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { profileQueryKey, type ProfileQuery } from '../../api/resourceRegistration';
import { authoringActions, profileDocument, useAuthoring } from '../../state/authoring';
import { cloneJson, type JsonObject } from '../../state/skillJson';
import { consoleActions } from '../../state/store';
import { Fold } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { MetadataConfigurationFields } from '../inspector/FieldEditors';
import styles from './project.module.css';

export function ProfileMappingEditor({ query }: { query: ProfileQuery }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const [domain, setDomain] = useState<string | null>(null);
  const declarations = document.source.resourceDeclarations.filter(item => item.mappingInputType !== null);
  const declaration = declarations.find(item => item.domain === domain);
  return <Fold title="编辑适配者资源映射"><fieldset disabled={document.writing} className={styles.profileFields}>
    <Select label="资源领域" value={declaration?.domain ?? null} placeholder="选择实际 Profile 支持的领域"
      options={declarations.map(item => ({ value: item.domain, label: item.domain }))} onChange={setDomain} />
    {declaration !== undefined && (!declaration.availability.available ? <div className={styles.issue}>{declaration.availability.reason}</div>
      : <MappingInput key={JSON.stringify([profileQueryKey(query), declaration.domain, declaration.mappingInputType])}
        query={query} domain={declaration.domain} type={declaration.mappingInputType!} writing={document.writing} />)}
  </fieldset></Fold>;
}

function MappingInput({ query, domain, type, writing }: { query: ProfileQuery; domain: string; type: string; writing: boolean }) {
  const [form, setForm] = useState<{ description: TypeDescription; configuration: JsonObject } | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    authoringApi.describeAuthoringConfiguration({ type, configuration: null, domain, owner: { kind: 'profile', query } }, controller.signal)
      .then(description => setForm({ description, configuration: cloneJson(description.configuration) }), error => {
        if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('映射字段', error) });
      });
    return () => controller.abort();
  }, [profileQueryKey(query), domain, type]);
  if (failure !== null) return <ErrorLine {...failure} />;
  if (form === null) return <div className={styles.note}>正在读取正式映射输入类型及默认配置。</div>;
  return <>
    <MetadataConfigurationFields identity={JSON.stringify([profileQueryKey(query), domain])} description={form.description}
      value={form.configuration} onSet={configuration => setForm({ ...form, configuration })} />
    {form.description.inputKind === null && <div className={styles.issue}>该类型没有提供正式映射输入 kind。</div>}
    <Button disabled={writing || form.description.inputKind === null} onClick={() => {
      void authoringActions.mapProfileResource(query, domain, { ...form.configuration, kind: form.description.inputKind! }).then(result => {
        if (result !== null) setForm({ ...form, configuration: cloneJson(form.description.configuration) });
      });
    }}>保存此 Profile 映射</Button>
  </>;
}
