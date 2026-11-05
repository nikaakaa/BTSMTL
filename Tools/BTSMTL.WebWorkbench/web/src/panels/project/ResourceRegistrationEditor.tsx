import { useState } from 'react';
import type { BusinessKeyMetadata, ReferenceChoice } from '../../api/authoring';
import type { ProfileOperationResult, ResourceDeclaration, ResourceReference, ResourceRegistrationInput } from '../../api/resourceRegistration';
import type { ResourceAssetSelection } from '../../state/referenceSelection';
import { Fold } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
import { ReferencePicker } from '../inspector/ReferencePicker';
import styles from './project.module.css';

export type RegistrationResource = { reference: ResourceReference; token: string | null; instanceCapacity: number | null };

export function ResourceRegistrationEditor({ id, label, packageId, adapter, declarations, resources, businessKeys, writing, onRegister }: {
  id: string; label: string; packageId: string; adapter: string; declarations: ResourceDeclaration[]; resources: RegistrationResource[];
  businessKeys: BusinessKeyMetadata[]; writing: boolean;
  onRegister(keyKind: string, input: ResourceRegistrationInput): Promise<ProfileOperationResult | null>;
}) {
  const [keyKind, setKeyKind] = useState<string | null>(null);
  const [asset, setAsset] = useState<ResourceAssetSelection | null>(null);
  const [key, setKey] = useState('');
  const [capacity, setCapacity] = useState<number | null>(null);
  const [effectKind, setEffectKind] = useState('');
  const supported = declarations.filter(item => item.registrationInput !== null);
  const declaration = supported.find(item => item.keyKind === keyKind);
  const inputKind = declaration?.registrationInput ?? null;
  const selectedResources = resources.filter(item => item.reference.keyKind === keyKind);
  const matching = asset === null ? [] : selectedResources.filter(item => item.token === asset.token);
  const existing = matching.length === 1 ? matching[0] : matching.find(item => item.reference.key === key);
  const actualKey = matching.length === 1 ? matching[0].reference.key : key;
  const actualCapacity = existing === undefined ? capacity : existing.instanceCapacity;
  const choices: ReferenceChoice[] = selectedResources.filter(item => item.token !== null).map(item => {
    const target = { kind: 'BusinessKey' as const, package: item.reference.package, keyKind: item.reference.keyKind, key: item.reference.key };
    const metadata = businessKeys.find(key => key.target.package === target.package && key.target.keyKind === target.keyKind && key.target.key === target.key);
    return { value: item.token, name: item.reference.key, detail: item.reference.context || '公共', source: adapter,
      conflict: null, targets: [target], businessKeys: metadata === undefined ? [] : [metadata], associated: [] };
  });
  const register = async () => {
    let input: ResourceRegistrationInput;
    switch (inputKind) {
      case 'asset': input = { kind: 'asset', token: asset!.token, key: actualKey }; break;
      case 'instance-resource': input = { kind: 'instance-resource', token: asset!.token, key: actualKey, instanceCapacity: actualCapacity! }; break;
      case 'camera-effect': input = { kind: 'camera-effect', token: asset!.token, key: actualKey, effectKind }; break;
      case null: return;
    }
    const result = await onRegister(keyKind!, input);
    if (result !== null) setAsset(null);
  };
  const complete = asset !== null && keyKind !== null && actualKey.trim() !== '' && inputKind !== null &&
    (matching.length < 2 || existing !== undefined) &&
    (inputKind !== 'instance-resource' || actualCapacity !== null && Number.isInteger(actualCapacity) && actualCapacity > 0) &&
    (inputKind !== 'camera-effect' || effectKind.trim() !== '');
  return <Fold title={`登记 ${label} 资源`}>
    <fieldset disabled={writing} className={styles.profileFields}>
      <Select label="正式资源键类别" value={declaration?.keyKind ?? null} placeholder={`选择本 ${label} 可登记的资源类别`}
        options={supported.map(item => ({ value: item.keyKind, label: item.keyKind }))}
        onChange={value => { setKeyKind(value); setAsset(null); setKey(''); setCapacity(null); setEffectKind(''); }} />
      {supported.length === 0 && <div className={styles.note}>本 {label} 没有支持资源登记的正式类别。</div>}
      {declaration !== undefined && <ReferencePicker id={`${id}/resource/${declaration.keyKind}`} label={`${label} 资源`} domain={declaration.domain}
        value={asset?.token} configuration={{}} catalog={{ kind: 'ready', referenceKind: declaration.referenceKind, items: choices,
          businessKeysAvailability: null, invalidReason: '所选素材来自真实对象候选，登记时由正式领域合同确认。' }}
        onChoose={choice => { setAsset({ token: String(choice.value), source: `${choice.name} · ${choice.detail}`, adapter }); setKey(''); }}
        resourceRegistration={{ keyKind: declaration.keyKind, adapter, objects: declaration.availability.available
          ? { kind: 'available', scope: { package: packageId, keyKind: declaration.keyKind, domain: declaration.domain, objectType: declaration.objectType } }
          : { kind: 'unavailable', reason: declaration.availability.reason },
          choose: asset => { setAsset(asset); setKey(''); } }} />}
      {asset !== null && <div className={styles.facts}><span>{asset.source}</span>
        {matching.length > 1 ? <Select label="明确选择实际资源键" value={existing?.reference.key ?? null} placeholder="存在多个键，请明确选择"
          options={matching.map(item => ({ value: item.reference.key, label: item.reference.key }))} onChange={setKey} />
          : matching.length === 1 ? <span>复用 {actualKey}</span> : <TextInput aria-label="新资源业务键" placeholder="填写新的业务键" value={key} onChange={event => setKey(event.target.value)} />}
        {inputKind === 'instance-resource' && (existing === undefined
          ? <NumberInput label="新资源实例容量" min={1} step={1} value={capacity} onChange={setCapacity} />
          : <span>实际实例容量：{existing.instanceCapacity === null ? '未配置，请先配置该资源容量' : existing.instanceCapacity}</span>)}
        {inputKind === 'camera-effect' && <TextInput aria-label="正式相机效果类别" placeholder="填写正式效果类别" value={effectKind} onChange={event => setEffectKind(event.target.value)} />}
        <Button disabled={!complete || writing} onClick={() => void register()}>登记到实际 {label}</Button>
      </div>}
    </fieldset>
  </Fold>;
}
