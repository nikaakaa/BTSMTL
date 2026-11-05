import { useEffect, useRef, useState } from 'react';
import type { ReferenceChoice } from '../../api/authoring';
import {
  acceptsReferenceDrag, matchesReferenceChoice, readReferenceDrop, referenceValueKey, useReferenceSelection,
  type ReferenceCatalog, type ResourceRegistrationSelection,
} from '../../state/referenceSelection';
import type { JsonObject, JsonValue } from '../../state/skillJson';
import { Button, TextInput } from '../../ui/controls';
import { openPanel } from '../../shell/layout';
import styles from './referencePicker.module.css';

export function ReferencePicker({ id, label, value, configuration, catalog, onChoose, resourceRegistration, domain = null }: {
  id: string; label: string; value: JsonValue | undefined; configuration: JsonObject; catalog: ReferenceCatalog;
  onChoose(choice: ReferenceChoice): void;
  resourceRegistration?: ResourceRegistrationSelection;
  domain?: string | null;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [rejection, setRejection] = useState<string | null>(null);
  const host = useRef<HTMLDivElement>(null);
  const selection = { value, configuration };
  const selected = catalog.kind === 'ready'
    ? catalog.items.filter(item => matchesReferenceChoice(item, selection)) : [];
  const invalid = catalog.kind === 'ready' && selected.length === 0 && value !== undefined;
  const query = search.toLocaleLowerCase();
  const items = catalog.kind === 'ready' ? catalog.items.filter(item =>
    [item.name, item.detail, item.source, referenceValueKey(item.value)].some(text => text?.toLocaleLowerCase().includes(query))) : [];
  const title = selected.length ? [...new Set(selected.map(item => item.name))].join(' / ') : referenceValueKey(value) ?? '未设置';
  const focus = () => useReferenceSelection.setState({ field: { id, name: label, domain, catalog, value, configuration, choose: onChoose, resourceRegistration } });
  useEffect(() => {
    const current = useReferenceSelection.getState().field;
    if (current?.id === id) useReferenceSelection.setState({ field: { id, name: label, domain, catalog, value, configuration, choose: onChoose, resourceRegistration } });
  }, [id, label, domain, catalog, value, configuration, onChoose, resourceRegistration]);
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
      const result = readReferenceDrop(event.dataTransfer, catalog);
      if (result.kind === 'selected') choose(result.choice);
      else setRejection(result.reason);
    }}>
    <Button className={invalid ? styles.invalid : styles.trigger} aria-label={label} aria-expanded={open}
      disabled={catalog.kind === 'unavailable' || catalog.items.length === 0}
      onClick={() => { focus(); setOpen(!open); }}>
      {title}
    </Button>
    {resourceRegistration && <Button onClick={() => { focus(); openPanel('resources'); }}>从项目素材登记</Button>}
    {catalog.kind === 'unavailable' ? <div className={styles.note}>上下文尚未取得：{catalog.reason}</div>
      : <>{catalog.items.length === 0 && <div className={styles.note}>没有候选</div>}
        {invalid && <div className={styles.issue}>{catalog.invalidReason}</div>}</>}
    {selected.map((item, index) => item.conflict && <div key={index} className={styles.issue}>{item.conflict}</div>)}
    {rejection && <div role="alert" className={styles.issue}>{rejection}</div>}
    {open && <div className={styles.popup} onKeyDown={event => { if (event.key === 'Escape') setOpen(false); }}>
      <TextInput autoFocus aria-label={`搜索${label}`} placeholder="搜索名称、说明或来源" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
      <div className={styles.list} role="listbox" aria-label={label}>
        {items.map((item, index) => <button type="button" key={`${index}:${referenceValueKey(item.value)}`}
          className={styles.item} role="option" aria-selected={matchesReferenceChoice(item, selection)}
          onClick={() => choose(item)}>
          <strong>{item.name}</strong>
          <span>{item.detail}</span>
          <small>{item.source}</small>
          {item.conflict && <span className={styles.issue}>{item.conflict}</span>}
        </button>)}
        {items.length === 0 && <div className={styles.note}>没有匹配的候选</div>}
      </div>
      <Button variant="ghost" onClick={() => setOpen(false)}>关闭</Button>
    </div>}
  </div>;
}
