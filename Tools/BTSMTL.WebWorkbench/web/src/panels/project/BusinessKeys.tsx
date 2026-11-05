import { useState } from 'react';
import type { AuthoringCarriers, BusinessKeyMetadata, BusinessKeyTarget } from '../../api/authoring';
import { referenceTargetKey, useReferenceSelection } from '../../state/referenceSelection';
import { Empty, Facts } from '../../ui/blocks';
import { Button, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { BusinessKeyRename, BusinessKeyRenameReceipt, type BusinessKeyRenameCompletion } from './BusinessKeyRename';
import { TargetReferrers } from './TargetReferrers';
import styles from './project.module.css';

const bindingNames = { bound: '已绑定', unbound: '未绑定' } satisfies Record<BusinessKeyMetadata['bindings'][number]['state'], string>;
const projectionNames = { available: '投影可用', missing: '缺少投影', 'not-required': '不要求投影' } satisfies Record<BusinessKeyMetadata['bindings'][number]['projectionState'], string>;

export function BusinessKeyList({ items, failure, selected, onSelect }: {
  items: BusinessKeyMetadata[] | null; failure: { text: string; entryId: number | null } | null;
  selected: BusinessKeyTarget | null; onSelect(item: BusinessKeyMetadata): void;
}) {
  const [search, setSearch] = useState('');
  if (failure !== null) return <ErrorLine {...failure} />;
  if (items === null) return <Empty text="正在读取业务键目录" />;
  const query = search.toLocaleLowerCase();
  const shown = items.filter(item => `${item.target.package} ${item.target.keyKind} ${item.target.key} ${item.referenceKind} ${
    item.bindings.map(binding => `${binding.adapter} ${binding.context} ${binding.token ?? ''} ${bindingNames[binding.state]} ${projectionNames[binding.projectionState]}`).join(' ')
  }`.toLocaleLowerCase().includes(query));
  return <>
    <TextInput aria-label="搜索业务键" placeholder="搜索作者包、键、绑定或投影状态" value={search} onChange={event => setSearch(event.currentTarget.value)} />
    <VirtualList className={styles.candidateList} count={shown.length} rowHeight={48} label="正式业务键" render={index => {
      const item = shown[index];
      return <button className={styles.row} aria-selected={selected !== null && referenceTargetKey(selected) === referenceTargetKey(item.target)}
        onClick={() => onSelect(item)}>
        <span>{item.target.key} · {item.target.keyKind}</span>
        <small>{item.target.package} · {item.bindings.filter(binding => binding.state === 'unbound').length} 个未绑定 · {
          item.bindings.filter(binding => binding.projectionState === 'missing').length} 个缺少投影</small>
      </button>;
    }} />
    <div className={styles.note}>{shown.length} / {items.length} 个正式业务键</div>
  </>;
}

export function BusinessKeyDetails({ target, metadata, onCatalog, onChanged }: {
  target: BusinessKeyTarget; metadata: BusinessKeyMetadata | null; onCatalog(catalog: AuthoringCarriers): void; onChanged(): void;
}) {
  const field = useReferenceSelection(state => state.field);
  const [completion, setCompletion] = useState<BusinessKeyRenameCompletion | null>(null);
  const refreshed = completion?.refresh?.catalog?.businessKeys.find(item => referenceTargetKey(item.target) === referenceTargetKey(completion.result.renamedTarget));
  const current = completion === null ? metadata : refreshed ?? null;
  const onRefreshed = (result: BusinessKeyRenameCompletion['result'], refresh: NonNullable<BusinessKeyRenameCompletion['refresh']>) => {
    setCompletion({ result, refresh });
    if (refresh.catalog !== null) onCatalog(refresh.catalog);
    onChanged();
  };
  return <>
    {completion !== null && <BusinessKeyRenameReceipt completion={completion} onRefreshed={onRefreshed} />}
    {current === null ? <Empty text={completion === null ? `${target.package} · ${target.keyKind} · ${target.key} 尚未从所选正式目录取得，绑定状态不可用`
      : '已保存的键尚未从正式目录读回，当前绑定状态不可用'} /> : <>
      <Facts fields={[{ displayName: '作者包', value: current.target.package }, { displayName: '键类别', value: current.target.keyKind },
        { displayName: '业务键', value: current.target.key }, { displayName: '声明状态', value: current.declared ? '已声明' : '未声明' },
        { displayName: '引用类别', value: current.referenceKind }]} />
      {current.bindings.length === 0 && <div className={styles.note}>正式目录没有返回适配者绑定条目。</div>}
      {current.bindings.map((binding, index) => <div key={index} className={styles.facts}>
        <strong>{binding.adapter} · {binding.context === '' ? '共用上下文' : binding.context}</strong>
        <span className={binding.state === 'unbound' || binding.projectionState === 'missing' ? styles.issue : styles.note}>
          {bindingNames[binding.state]} · {projectionNames[binding.projectionState]}</span>
        <small>绑定令牌：{binding.token ?? '未提供'}</small>
        {field?.resourceRegistration?.keyKind === current.target.keyKind && binding.token !== null && <Button onClick={() => field.resourceRegistration!.choose({ token: binding.token!,
          source: `${current.target.package} / ${current.target.keyKind} / ${current.target.key} · ${binding.adapter} / ${binding.context || '公共'}` })}>
          将实际资源登记到 {field.name}
        </Button>}
        {binding.projectionKind !== null && <small>投影类别：{binding.projectionKind}</small>}
      </div>)}
      <BusinessKeyRename key={referenceTargetKey(current.target)} target={current.target}
        onSaved={result => setCompletion({ result, refresh: null })} onRefreshed={onRefreshed} />
      <TargetReferrers target={current.target} />
    </>}
  </>;
}
