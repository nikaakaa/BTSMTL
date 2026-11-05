import { useState } from 'react';
import type { BusinessKeyMetadata, BusinessKeyTarget } from '../../api/authoring';
import { referenceTargetKey, useReferenceSelection } from '../../state/referenceSelection';
import type { ResourceDeclaration } from '../../api/resourceRegistration';
import { Empty, Facts } from '../../ui/blocks';
import { Button, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { TargetReferrers } from './TargetReferrers';
import styles from './project.module.css';

const bindingNames = { bound: '已绑定', unbound: '未绑定' } satisfies Record<BusinessKeyMetadata['bindings'][number]['state'], string>;
const projectionNames = { available: '投影可用', missing: '缺少投影', 'not-required': '不要求投影' } satisfies Record<BusinessKeyMetadata['bindings'][number]['projectionState'], string>;

export function BusinessKeyList({ items, declarations, failure, selected, onSelect }: {
  items: BusinessKeyMetadata[] | null; failure: { text: string; entryId: number | null } | null;
  declarations: ResourceDeclaration[] | null;
  selected: BusinessKeyTarget | null; onSelect(item: BusinessKeyMetadata): void;
}) {
  const [search, setSearch] = useState('');
  const [keyKind, setKeyKind] = useState<string | null>(null);
  if (failure !== null) return <ErrorLine {...failure} />;
  if (items === null) return <Empty text="正在读取业务键目录" />;
  const query = search.toLocaleLowerCase();
  const declaration = declarations?.find(item => item.keyKind === keyKind);
  const categoryItems = keyKind === null ? items : items.filter(item => item.target.keyKind === keyKind);
  const shown = categoryItems.filter(item => `${item.target.package} ${item.target.keyKind} ${item.target.key} ${item.referenceKind} ${item.domain ?? ''} ${
    item.bindings.map(binding => `${binding.adapter} ${binding.context} ${binding.token ?? ''} ${bindingNames[binding.state]} ${projectionNames[binding.projectionState]}`).join(' ')
  }`.toLocaleLowerCase().includes(query));
  return <>
    {declarations !== null && <div className={styles.declarationFilter}>
      <Select label="正式业务键类别" value={declaration?.keyKind ?? null} placeholder="全部正式类别"
        options={declarations.map(item => ({ value: item.keyKind, label: `${item.keyKind}${item.domain === null ? '' : ` · ${item.domain}`}` }))} onChange={setKeyKind} />
      {keyKind !== null && <Button variant="ghost" onClick={() => setKeyKind(null)}>全部类别</Button>}
    </div>}
    <TextInput aria-label="搜索业务键" placeholder="搜索作者包、键、绑定或投影状态" value={search} onChange={event => setSearch(event.currentTarget.value)} />
    {declaration !== undefined && categoryItems.length === 0 && <div className={styles.note}>
      该正式类别尚无业务键。{declaration.registrationInput === null ? '该类别不提供资源登记操作。' : '可在目标字段或独立 Profile 中登记新的资源键。'}
    </div>}
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

export function BusinessKeyDetails({ target, metadata: current }: {
  target: BusinessKeyTarget; metadata: BusinessKeyMetadata | null;
}) {
  const field = useReferenceSelection(state => state.field);
  return <>
    {current === null ? <Empty text={`${target.package} · ${target.keyKind} · ${target.key} 尚未从所选正式目录取得，绑定状态不可用`} /> : <>
      <Facts fields={[{ displayName: '作者包', value: current.target.package }, { displayName: '键类别', value: current.target.keyKind },
        { displayName: '业务键', value: current.target.key }, { displayName: '声明状态', value: current.declared ? '已声明' : '未声明' },
        { displayName: '引用类别', value: current.referenceKind }]} />
      {current.bindings.length === 0 && <div className={styles.note}>正式目录没有返回适配者绑定条目。</div>}
      {current.bindings.map((binding, index) => <div key={index} className={styles.facts}>
        <strong>{binding.adapter} · {binding.context === '' ? '共用上下文' : binding.context}</strong>
        <span className={binding.state === 'unbound' || binding.projectionState === 'missing' ? styles.issue : styles.note}>
          {bindingNames[binding.state]} · {projectionNames[binding.projectionState]}</span>
        <small>绑定令牌：{binding.token ?? '未提供'}</small>
        <small>实例容量：{binding.instanceCapacity === null ? current.registrationInput === 'instance-resource' ? '未配置' : '不适用' : binding.instanceCapacity}</small>
        {field?.resourceRegistration?.keyKind === current.target.keyKind && binding.token !== null &&
          (field.resourceRegistration.adapter === undefined || field.resourceRegistration.adapter === binding.adapter) && <Button onClick={() => field.resourceRegistration!.choose({ token: binding.token!,
          adapter: binding.adapter,
          source: `${current.target.package} / ${current.target.keyKind} / ${current.target.key} · ${binding.adapter} / ${binding.context || '公共'}` })}>
          将实际资源登记到 {field.name}
        </Button>}
        {binding.projectionKind !== null && <small>投影类别：{binding.projectionKind}</small>}
      </div>)}
      <TargetReferrers target={current.target} />
    </>}
  </>;
}
