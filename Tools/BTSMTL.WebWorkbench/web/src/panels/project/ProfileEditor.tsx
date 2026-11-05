import { useEffect, useState } from 'react';
import type { BusinessKeyMetadata } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { profileQueryKey, resourceReferenceKey, type ProfileQuery } from '../../api/resourceRegistration';
import { authoringActions, profileConfigurationDirty, profileDocument, profileResourceDirty, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { ConfigurationFields } from '../inspector/FieldEditors';
import { ResourceRegistrationEditor } from './ResourceRegistrationEditor';
import styles from './project.module.css';

export type ProfileLocation = { package: string; assetToken: string; name: string; source: string };
const locationKey = (profile: ProfileLocation) => JSON.stringify([profile.package, profile.assetToken]);

export function ProfileSources({ profiles, businessKeys }: { profiles: ProfileLocation[]; businessKeys: BusinessKeyMetadata[] }) {
  const [selected, setSelected] = useState<string | null>(null);
  const [adapter, setAdapter] = useState<string | null>(null);
  const profile = profiles.find(item => locationKey(item) === selected);
  const adapters = [...new Set(businessKeys.filter(item => item.target.package === profile?.package)
    .flatMap(item => item.bindings.map(binding => binding.adapter)))];
  return <Fold title="独立 Profile 编辑" defaultOpen>
    <PanelBar wrap><Select label="实际 Profile 资产" value={profile === undefined ? null : locationKey(profile)} placeholder="选择实际 Profile"
      options={profiles.map(item => ({ value: locationKey(item), label: `${item.name} · ${item.package}` }))}
      onChange={value => { setSelected(value); setAdapter(null); }} />
      <Select label="Profile 绑定适配者" value={adapter} placeholder="选择正式适配者" disabled={profile === undefined}
        options={adapters.map(value => ({ value, label: value }))} onChange={setAdapter} />
    </PanelBar>
    {profile !== undefined && adapters.length === 0 && <div className={styles.note}>该作者包尚未提供 Profile 的正式适配者候选。</div>}
    <PanelBar><Button disabled={profile === undefined || adapter === null} onClick={() => authoringActions.activateProfile({
      package: profile!.package, adapter: adapter!, assetToken: profile!.assetToken,
    })}>打开实际 Profile</Button></PanelBar>
  </Fold>;
}

export function ProfileEditor({ query, location }: { query: ProfileQuery; location: ProfileLocation | undefined }) {
  const document = useAuthoring(state => profileDocument(state, query));
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const key = profileQueryKey(query);
  useEffect(() => {
    const controller = new AbortController();
    authoringActions.openProfile(query, controller.signal).catch(error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Profile', error) });
    });
    return () => { controller.abort(); authoringActions.releaseProfile(query); };
  }, [key]);
  if (document === undefined) return failure === null ? <div className={styles.note}>正在读取实际 Profile。</div> : <ErrorLine {...failure} />;
  const source = document.source;
  const supports = (command: string) => source.operations.some(operation => operation.command === command);
  const dirty = profileConfigurationDirty(document);
  return <div onFocusCapture={() => authoringActions.activateProfile(query)} onPointerDownCapture={() => authoringActions.activateProfile(query)}>
    <div className={styles.facts}><strong>{location?.name ?? 'Profile'} · {source.domain}</strong>{location && <code>{location.source}</code>}
      <small>{source.package} · {source.adapter} · 上下文 {source.context || '公共'}</small></div>
    {failure && <ErrorLine {...failure} />}{document.failure && <ErrorLine {...document.failure} />}
    <PanelBar wrap>
      <Button disabled={document.writing} onClick={async () => {
        try { await authoringActions.openProfile(query, undefined, true); setFailure(null); }
        catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Profile', error) }); }
      }}>重读正式来源</Button>
      <Button disabled={document.writing || !document.undo.length || !supports('undo-profile-data')} onClick={() => authoringActions.undo()}>撤销</Button>
      <Button disabled={document.writing || !document.redo.length || !supports('redo-profile-data')} onClick={() => authoringActions.redo()}>重做</Button>
    </PanelBar>
    <Fold title="正式配置" defaultOpen>
      <div onFocusCapture={() => authoringActions.selectProfileConfiguration(query, { kind: 'profile' })}>
        <fieldset disabled={document.writing || !supports('configure-profile')} className={styles.profileFields}>
          <ConfigurationFields value={document.configuration} onSet={configuration => authoringActions.editProfile(query, configuration)} />
        </fieldset>
        <PanelBar wrap><Button disabled={document.writing || !dirty || !supports('configure-profile')} onClick={() => void authoringActions.saveProfileConfiguration(query)}>保存 Profile 配置</Button>
          <Button disabled={document.writing || !dirty} onClick={() => authoringActions.resetProfile(query)}>载入已保存配置</Button></PanelBar>
      </div>
    </Fold>
    <ProfileResourceConfiguration query={query} />
    {supports('register-profile-resource') && <ProfileResourceRegistration query={query} />}
    {supports('set-resource-capacity') && <ProfileResourceCapacity query={query} />}
    {document.message && <div role="status" className={styles.note}>{document.message}</div>}
    <Fold title="Profile 身份"><div className={styles.facts}><small>资产令牌：{source.assetToken}</small><small>作者修订：{source.revision}</small></div></Fold>
  </div>;
}

function ProfileResourceConfiguration({ query }: { query: ProfileQuery }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const [selected, setSelected] = useState<string | null>(() => document.configurationTarget.kind === 'resource'
    ? resourceReferenceKey(document.configurationTarget.reference) : null);
  const resources = document.source.resources.filter(item => item.configurationJson !== null);
  const resource = resources.find(item => resourceReferenceKey(item.reference) === selected);
  const missing = Object.keys(document.resourceConfigurations).filter(identity =>
    !resources.some(item => resourceReferenceKey(item.reference) === identity));
  const missingNotice = missing.length > 0 && <div className={styles.issue}>
    {missing.length} 项未保存条目配置已不在当前正式来源中，编辑仍保留，请核对来源变化。
  </div>;
  if (!document.source.operations.some(operation => operation.command === 'configure-profile-resource')) return missingNotice;
  const dirty = resource !== undefined && profileResourceDirty(document, resourceReferenceKey(resource.reference));
  return <Fold title="条目配置" defaultOpen><div onFocusCapture={() => {
    if (resource !== undefined) authoringActions.selectProfileConfiguration(query, { kind: 'resource', reference: resource.reference });
  }}>
    {missingNotice}
    <fieldset disabled={document.writing} className={styles.profileFields}>
      <Select label="实际可配置条目" value={resource === undefined ? null : resourceReferenceKey(resource.reference)} placeholder="选择有正式配置的条目"
        options={resources.map(item => ({ value: resourceReferenceKey(item.reference),
          label: `${item.reference.keyKind} · ${item.reference.key} · ${item.reference.context || '公共'}` }))}
        onChange={identity => {
          setSelected(identity);
          authoringActions.selectProfileConfiguration(query, { kind: 'resource',
            reference: resources.find(item => resourceReferenceKey(item.reference) === identity)!.reference });
        }} />
      {resource !== undefined && <ConfigurationFields key={resourceReferenceKey(resource.reference)}
        value={document.resourceConfigurations[resourceReferenceKey(resource.reference)]}
        onSet={configuration => authoringActions.editProfileResource(query, resource.reference, configuration)} />}
    </fieldset>
    {resource !== undefined && <PanelBar wrap>
      <Button disabled={document.writing || !dirty} onClick={() => void authoringActions.saveProfileResource(query, resource.reference)}>保存条目配置</Button>
      <Button disabled={document.writing || !dirty} onClick={() => authoringActions.resetProfileResource(query, resource.reference)}>载入条目已保存配置</Button>
    </PanelBar>}
  </div></Fold>;
}

function ProfileResourceRegistration({ query }: { query: ProfileQuery }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const source = document.source;
  return <ResourceRegistrationEditor id={profileQueryKey(query)} label="Profile" packageId={source.package} adapter={source.adapter}
    declarations={source.resourceDeclarations} resources={source.resources} writing={document.writing}
    onRegister={(keyKind, input) => authoringActions.registerProfileResource(query, keyKind, input)} />;
}

function ProfileResourceCapacity({ query }: { query: ProfileQuery }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const source = document.source;
  const [selected, setSelected] = useState<string | null>(null);
  const [capacity, setCapacity] = useState<number | null>(null);
  const instanceKinds = new Set(source.resourceDeclarations.filter(item => item.domain === source.domain && item.registrationInput === 'instance-resource').map(item => item.keyKind));
  const resources = source.resources.filter(item => instanceKinds.has(item.reference.keyKind));
  const resource = resources.find(item => resourceReferenceKey(item.reference) === selected);
  return <Fold title="修改实例资源容量"><fieldset disabled={document.writing} className={styles.profileFields}>
    <Select label="实际资源键" value={resource === undefined ? null : resourceReferenceKey(resource.reference)} placeholder="选择本 Profile 已绑定的实例资源"
      options={resources.map(item => ({ value: resourceReferenceKey(item.reference), label: `${item.reference.keyKind} · ${item.reference.key}` }))}
      onChange={value => { setSelected(value); setCapacity(resources.find(item => resourceReferenceKey(item.reference) === value)!.instanceCapacity); }} />
    {resource !== undefined && <span>当前实际实例容量：{resource.instanceCapacity === null ? '未配置' : resource.instanceCapacity}</span>}
    {resources.length === 0 && <div className={styles.note}>本 Profile 当前没有已绑定的实例资源。</div>}
    <NumberInput label="新实例容量" min={1} step={1} value={capacity} onChange={setCapacity} />
    <Button disabled={resource === undefined || capacity === null || !Number.isInteger(capacity) || capacity <= 0 || document.writing}
      onClick={() => void authoringActions.setProfileCapacity(query, resource!.reference.keyKind, resource!.reference.key, capacity!)}>保存实例容量</Button>
  </fieldset></Fold>;
}
