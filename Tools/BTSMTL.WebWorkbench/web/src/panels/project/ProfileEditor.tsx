import { useEffect, useState } from 'react';
import type { BusinessKeyMetadata, ReferenceChoice } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { profileQueryKey, type ProfileQuery, type ResourceRegistrationInput } from '../../api/resourceRegistration';
import { authoringActions, profileDirty, profileDocument, useAuthoring } from '../../state/authoring';
import type { ResourceAssetSelection } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { ConfigurationFields } from '../inspector/FieldEditors';
import { ReferencePicker } from '../inspector/ReferencePicker';
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

export function ProfileEditor({ query, location, businessKeys }: { query: ProfileQuery; location: ProfileLocation | undefined; businessKeys: BusinessKeyMetadata[] }) {
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
  const dirty = profileDirty(document);
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
      <fieldset disabled={document.writing || !supports('configure-profile')} className={styles.profileFields}>
        <ConfigurationFields value={document.configuration} onSet={configuration => authoringActions.editProfile(query, configuration)} />
      </fieldset>
      <PanelBar wrap><Button disabled={document.writing || !dirty || !supports('configure-profile')} onClick={() => void authoringActions.saveProfile(query)}>保存 Profile 配置</Button>
        <Button disabled={document.writing || !dirty} onClick={() => authoringActions.resetProfile(query)}>载入已保存配置</Button></PanelBar>
    </Fold>
    {supports('register-profile-resource') && <ProfileResourceRegistration query={query} businessKeys={businessKeys} />}
    {supports('set-resource-capacity') && <ProfileResourceCapacity query={query} businessKeys={businessKeys} />}
    {document.message && <div role="status" className={styles.note}>{document.message}</div>}
    <Fold title="Profile 身份"><div className={styles.facts}><small>资产令牌：{source.assetToken}</small><small>作者修订：{source.revision}</small></div></Fold>
  </div>;
}

function ProfileResourceRegistration({ query, businessKeys }: { query: ProfileQuery; businessKeys: BusinessKeyMetadata[] }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const source = document.source;
  const [keyKind, setKeyKind] = useState<string | null>(null);
  const [inputKind, setInputKind] = useState<ResourceRegistrationInput['kind'] | null>(null);
  const [asset, setAsset] = useState<ResourceAssetSelection | null>(null);
  const [key, setKey] = useState('');
  const [capacity, setCapacity] = useState<number | null>(null);
  const [effectKind, setEffectKind] = useState('');
  const kinds = [...new Set(businessKeys.filter(item => item.target.package === source.package).map(item => item.target.keyKind))];
  const matching = asset === null || keyKind === null ? [] : businessKeys.filter(item => item.target.package === source.package && item.target.keyKind === keyKind &&
    item.bindings.some(binding => binding.adapter === source.adapter && binding.context === source.context && binding.token === asset.token));
  const actualKey = matching.length === 1 ? matching[0].target.key : key;
  const choices: ReferenceChoice[] = businessKeys.filter(item => item.target.keyKind === keyKind).flatMap(item => item.bindings
    .filter(binding => binding.adapter === source.adapter && binding.token !== null).map(binding => ({
      value: binding.token, name: item.target.key, detail: `${item.target.package} · ${binding.context || '公共'}`, source: binding.adapter,
      conflict: null, targets: [item.target], businessKeys: [item], associated: [],
    })));
  const register = async () => {
    let input: ResourceRegistrationInput;
    switch (inputKind) {
      case 'asset': input = { kind: 'asset', token: asset!.token, key: actualKey }; break;
      case 'instance-resource': input = { kind: 'instance-resource', token: asset!.token, key: actualKey, instanceCapacity: capacity! }; break;
      case 'camera-effect': input = { kind: 'camera-effect', token: asset!.token, key: actualKey, effectKind }; break;
      case null: return;
    }
    const result = await authoringActions.registerProfileResource(query, keyKind!, input);
    if (result !== null) setAsset(null);
  };
  const complete = asset !== null && keyKind !== null && actualKey.trim() !== '' && inputKind !== null &&
    (matching.length < 2 || matching.some(item => item.target.key === key)) &&
    (inputKind !== 'instance-resource' || capacity !== null && Number.isInteger(capacity) && capacity > 0) &&
    (inputKind !== 'camera-effect' || effectKind.trim() !== '');
  return <Fold title="登记 Profile 资源">
    <fieldset disabled={document.writing} className={styles.profileFields}>
      <Select label="正式资源键类别" value={keyKind} placeholder="选择正式资源类别" options={kinds.map(value => ({ value, label: value }))}
        onChange={value => { setKeyKind(value); setAsset(null); setKey(''); setInputKind(null); }} />
      <Select<ResourceRegistrationInput['kind']> label="登记资源的数据类型" value={inputKind} placeholder="选择登记资源的正式数据类型" onChange={setInputKind}
        options={[{ value: 'asset', label: '普通资源' }, { value: 'instance-resource', label: '有实例容量的资源' }, { value: 'camera-effect', label: '相机效果' }]} />
      {keyKind !== null && <ReferencePicker id={`${profileQueryKey(query)}/resource/${keyKind}`} label={`${source.domain} Profile 资源`}
        value={asset?.token} configuration={{}} catalog={{ kind: 'ready', referenceKind: keyKind, items: choices,
          businessKeysAvailability: null, invalidReason: '所选素材来自真实对象候选，登记时由正式领域合同确认。' }}
        onChoose={choice => { setAsset({ token: String(choice.value), source: `${choice.name} · ${choice.detail}`, adapter: source.adapter }); setKey(''); }}
        resourceRegistration={{ keyKind, objects: { kind: 'available', scope: { package: source.package, keyKind, domain: source.domain, objectType: null } },
          choose: asset => { setAsset(asset); setKey(''); } }} />}
      {asset !== null && <div className={styles.facts}><span>{asset.source}</span>
        {matching.length > 1 ? <Select label="明确选择实际资源键" value={matching.some(item => item.target.key === key) ? key : null} placeholder="存在多个键，请明确选择"
          options={matching.map(item => ({ value: item.target.key, label: item.target.key }))} onChange={setKey} />
          : matching.length === 1 ? <span>复用 {actualKey}</span> : <TextInput aria-label="新资源业务键" placeholder="填写新的业务键" value={key} onChange={event => setKey(event.target.value)} />}
        {inputKind === 'instance-resource' && <NumberInput label="实例容量" min={1} step={1} value={capacity} onChange={setCapacity} />}
        {inputKind === 'camera-effect' && <TextInput aria-label="正式相机效果类别" placeholder="填写正式效果类别" value={effectKind} onChange={event => setEffectKind(event.target.value)} />}
        <Button disabled={!complete || document.writing} onClick={() => void register()}>登记到实际 Profile</Button>
      </div>}
    </fieldset>
  </Fold>;
}

function ProfileResourceCapacity({ query, businessKeys }: { query: ProfileQuery; businessKeys: BusinessKeyMetadata[] }) {
  const document = useAuthoring(state => profileDocument(state, query))!;
  const source = document.source;
  const [selected, setSelected] = useState<string | null>(null);
  const [capacity, setCapacity] = useState<number | null>(null);
  const resources = businessKeys.filter(item => item.target.package === source.package && item.bindings.some(binding => binding.adapter === source.adapter &&
    (binding.context === source.context || binding.context === '') && binding.state === 'bound'));
  const identity = (item: BusinessKeyMetadata) => JSON.stringify([item.target.keyKind, item.target.key]);
  const resource = resources.find(item => identity(item) === selected);
  return <Fold title="修改实例资源容量"><fieldset disabled={document.writing} className={styles.profileFields}>
    <Select label="实际资源键" value={resource === undefined ? null : identity(resource)} placeholder="选择已有实例资源"
      options={resources.map(item => ({ value: identity(item), label: `${item.target.keyKind} · ${item.target.key}` }))} onChange={setSelected} />
    <NumberInput label="新实例容量" min={1} step={1} value={capacity} onChange={setCapacity} />
    <Button disabled={resource === undefined || capacity === null || !Number.isInteger(capacity) || capacity <= 0 || document.writing}
      onClick={() => void authoringActions.setProfileCapacity(query, resource!.target.keyKind, resource!.target.key, capacity!)}>保存实例容量</Button>
  </fieldset></Fold>;
}
