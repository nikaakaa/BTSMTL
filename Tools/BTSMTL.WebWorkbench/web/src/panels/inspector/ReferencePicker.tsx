import { useEffect, useRef, useState } from 'react';
import type { ReferenceChoice } from '../../api/authoring';
import {
  acceptsReferenceDrag, matchesReferenceChoice, readReferenceDrop, referenceValueKey, useReferenceSelection, writeReferenceDrag,
  type ReferenceCatalog, type ResourceObjectFieldSelection,
} from '../../state/referenceSelection';
import type { JsonObject, JsonValue } from '../../state/skillJson';
import { Button, TextInput } from '../../ui/controls';
import { VirtualList } from '../../ui/VirtualList';
import { openPanel } from '../../shell/layout';
import styles from './referencePicker.module.css';

export function ReferencePicker({ id, label, value, configuration, catalog, onChoose, resourceObjects, domain = null, selection = null, resourceContext = null }: {
  id: string; label: string; value: JsonValue | undefined; configuration: JsonObject; catalog: ReferenceCatalog;
  onChoose(choice: ReferenceChoice): void;
  resourceObjects?: ResourceObjectFieldSelection;
  domain?: string | null;
  selection?: 'asset' | 'mapped' | null;
  resourceContext?: { adapter: string; context: string } | null;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [rejection, setRejection] = useState<string | null>(null);
  const host = useRef<HTMLDivElement>(null);
  const current = { value, configuration };
  const selected = catalog.kind === 'ready'
    ? catalog.items.filter(item => matchesReferenceChoice(item, current)) : [];
  const unset = value === undefined || value === null || value === '';
  const invalid = catalog.kind === 'ready' && selected.length === 0 && !unset;
  const query = search.toLocaleLowerCase();
  const items = catalog.kind === 'ready' ? catalog.items.filter(item =>
    [item.name, item.detail, item.source, referenceValueKey(item.value)].some(text => text?.toLocaleLowerCase().includes(query))) : [];
  const title = selected.length ? [...new Set(selected.map(item => item.name))].join(' / ') : unset ? '未设置' : referenceValueKey(value) ?? '未设置';
  const focus = () => useReferenceSelection.setState({ field: { id, name: label, domain, selection, resourceContext, catalog, value, configuration, choose: onChoose, resourceObjects } });
  useEffect(() => {
    const current = useReferenceSelection.getState().field;
    if (current?.id === id) useReferenceSelection.setState({ field: { id, name: label, domain, selection, resourceContext, catalog, value, configuration, choose: onChoose, resourceObjects } });
  }, [id, label, domain, selection, resourceContext, catalog, value, configuration, onChoose, resourceObjects]);
  useEffect(() => () => {
    if (useReferenceSelection.getState().field?.id === id) useReferenceSelection.setState({ field: null });
  }, [id]);
  const choose = (choice: ReferenceChoice) => {
    onChoose(choice);
    setRejection(null);
    setOpen(false);
    setSearch('');
  };
  return <div ref={host} className={styles.root} onFocusCapture={focus}
    onDragOver={event => {
      if (catalog.kind === 'ready' && acceptsReferenceDrag(event.dataTransfer, catalog.referenceKind)) {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
      }
    }}
    onDrop={event => {
      event.preventDefault();
      if (selection === 'mapped' && event.dataTransfer.files.length !== 0) {
        setRejection('该字段按当前载体选择已有映射；请从正式目录选择映射名称，原值已保留。');
        return;
      }
      const result = readReferenceDrop(event.dataTransfer, catalog);
      if (result.kind === 'selected') choose(result.choice);
      else setRejection(result.reason);
    }}>
    <Button className={invalid ? styles.invalid : styles.trigger} aria-label={label} aria-expanded={open}
      title={title} onClick={() => { focus(); setOpen(!open); }}>
      <span className={styles.title}>{title}</span>
    </Button>
    {domain !== null && <Button variant="ghost" onClick={() => { focus(); openPanel('resources'); }}>在资源视图中选择</Button>}
    {selection === 'asset' && <div className={styles.note}>选择适配者提供的实际文件或子对象；保存所选候选的原字符串。</div>}
    {selection === 'mapped' && <div className={styles.note}>按当前载体选择已有映射；新增映射使用该适配者的 Profile 编辑入口。</div>}
    {resourceObjects && <Button disabled={resourceObjects.kind === 'unavailable'} onClick={() => { focus(); openPanel('resources'); }}>选择文件中的实际对象</Button>}
    {catalog.kind === 'unavailable' ? <div className={styles.note}>{catalog.reason}</div>
      : <>{catalog.items.length === 0 && <div className={styles.note}>没有候选</div>}
        {invalid && <div className={styles.issue}>{catalog.invalidReason}</div>}</>}
    {selected.map((item, index) => item.conflict && <div key={index} className={styles.issue}>{item.conflict}</div>)}
    {rejection && <div role="alert" className={styles.issue}>{rejection}</div>}
    {open && <div className={styles.popup} onKeyDown={event => { if (event.key === 'Escape') setOpen(false); }}>
      <TextInput autoFocus aria-label={`搜索${label}`} placeholder="搜索名称、说明或来源" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
      {items.length === 0 ? <div className={styles.note}>没有匹配的候选</div>
        : <VirtualList className={styles.list} count={items.length} rowHeight={98} role="listbox" label={label}
          revealIndex={0} revealKey={query} render={index => {
            const item = items[index];
            const address = referenceValueKey(item.value);
            return <button type="button" className={styles.item} role="option" aria-selected={matchesReferenceChoice(item, current)}
              aria-posinset={index + 1} aria-setsize={items.length} disabled={item.conflict !== null} draggable={item.conflict === null}
              onDragStart={event => { if (catalog.kind === 'ready') writeReferenceDrag(event.dataTransfer, catalog.referenceKind, item); }}
              onClick={() => choose(item)}>
              <strong title={item.name}>{item.name}</strong>
              <span title={item.detail}>{item.detail}</span>
              <small title={address}>{address}</small>
              <small title={item.source}>{item.source}</small>
              {item.conflict && <span className={styles.issue} title={item.conflict}>{item.conflict}</span>}
            </button>;
          }} />}
      <Button variant="ghost" onClick={() => setOpen(false)}>关闭</Button>
    </div>}
  </div>;
}
