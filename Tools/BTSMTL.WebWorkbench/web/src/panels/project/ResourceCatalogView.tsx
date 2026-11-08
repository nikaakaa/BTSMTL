import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { AdapterCatalogDomain, AdapterCatalogItem } from '../../api/authoring';
import { matchesReferenceChoice, referenceValueKey, resourceReferenceChoice, writeReferenceDrag, type ReferenceFieldSelection } from '../../state/referenceSelection';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, Select, TextInput } from '../../ui/controls';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

export const catalogItemKey = (item: Pick<AdapterCatalogItem, 'adapter' | 'domain' | 'value' | 'context'>) =>
  JSON.stringify([item.adapter, item.domain, item.value, item.context]);
const resolutionKey = (item: Pick<AdapterCatalogItem, 'domain' | 'value' | 'context'>) =>
  JSON.stringify([item.domain, item.value, item.context]);
export type ResourceCatalogSelection = { kind: 'catalog'; item: AdapterCatalogItem }
  | { kind: 'missing'; domain: string; value: string };

export function ResourceCatalogView({ domains, revision, currentScope, field, selected, onSelect, details }: {
  domains: AdapterCatalogDomain[]; revision: string; currentScope: { adapter: string; context: string } | null;
  field: ReferenceFieldSelection | null; selected: ResourceCatalogSelection | null; onSelect(item: ResourceCatalogSelection | null): void; details: ReactNode;
}) {
  const [domain, setDomain] = useState<string | null>(field?.domain ?? null);
  const [all, setAll] = useState(false);
  const [issuesOnly, setIssuesOnly] = useState(false);
  const [search, setSearch] = useState('');
  useEffect(() => { if (field?.domain != null) setDomain(field.domain); }, [field?.id, field?.domain]);
  const catalogItems = useMemo(() => domains.flatMap(group => group.items.map(item => ({ ...item, adapter: group.adapter, domain: group.domain }))), [domains]);
  const contextualValues = useMemo(() => new Set(catalogItems.filter(item => currentScope !== null && item.adapter === currentScope.adapter && item.context === currentScope.context)
    .map(item => JSON.stringify([item.adapter, item.domain, item.value]))), [catalogItems, currentScope?.adapter, currentScope?.context]);
  const conflicts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const item of catalogItems) counts.set(resolutionKey(item), (counts.get(resolutionKey(item)) ?? 0) + 1);
    return new Set([...counts].filter(([, count]) => count > 1).map(([key]) => key));
  }, [catalogItems]);
  const activeField = field?.domain == null ? null : { ...field, domain: field.domain };
  const fieldItems = activeField?.catalog.kind === 'ready' ? activeField.catalog.items : [];
  const currentValue = activeField?.value;
  const selectedItem = selected?.kind === 'catalog' ? selected.item : null;
  const inCurrentScope = (item: AdapterCatalogItem) => currentScope === null ? item.context === '' : item.adapter === currentScope.adapter &&
    (item.context === currentScope.context || item.context === '' && !contextualValues.has(JSON.stringify([item.adapter, item.domain, item.value])));
  useEffect(() => {
    if (field?.domain == null) return;
    const matches = catalogItems.filter(item => item.domain === field.domain && item.value === field.value && inCurrentScope(item));
    onSelect(matches.length === 1 ? { kind: 'catalog', item: matches[0] }
      : matches.length === 0 && typeof field.value === 'string' && field.value !== ''
        ? { kind: 'missing', domain: field.domain, value: field.value } : null);
  }, [field?.id, field?.domain, field?.value, catalogItems, currentScope?.adapter, currentScope?.context, onSelect]);
  const missing = activeField !== null && typeof currentValue === 'string' && currentValue !== '' &&
    !catalogItems.some(item => item.domain === activeField.domain && item.value === currentValue && inCurrentScope(item))
    ? { kind: 'missing' as const, domain: activeField.domain, value: currentValue, fieldName: activeField.name } : null;
  const query = search.toLocaleLowerCase();
  const shown = catalogItems.filter(item => (domain === null || item.domain === domain) && (all || inCurrentScope(item)) &&
    (!issuesOnly || conflicts.has(resolutionKey(item))) &&
    `${item.display} ${item.value} ${item.adapter} ${item.context}`.toLocaleLowerCase().includes(query));
  const shownMissing = missing !== null && (domain === null || domain === missing.domain) &&
    `${missing.fieldName} ${missing.value}`.toLocaleLowerCase().includes(query) ? missing : null;
  const rows: ({ kind: 'domain'; domain: string } | { kind: 'item'; item: AdapterCatalogItem }
    | NonNullable<typeof missing>)[] = [];
  for (const name of [...new Set([...shown.map(item => item.domain), ...(shownMissing === null ? [] : [shownMissing.domain])])]) {
    rows.push({ kind: 'domain', domain: name });
    if (shownMissing !== null && name === shownMissing.domain) rows.push(shownMissing);
    rows.push(...shown.filter(item => item.domain === name).map(item => ({ kind: 'item' as const, item })));
  }
  const current = rows.findIndex(row => row.kind === 'missing' || row.kind === 'item' && row.item.domain === activeField?.domain &&
    referenceValueKey(row.item.value) === referenceValueKey(currentValue));
  const choices = selectedItem !== null && inCurrentScope(selectedItem) && activeField?.domain === selectedItem.domain && activeField.catalog.kind === 'ready'
    ? fieldItems.filter(choice => referenceValueKey(choice.value) === referenceValueKey(selectedItem.value)) : [];
  return <div className={styles.resourceCatalog}>
    <div className={styles.contentTree}>
      <PanelBar wrap>
        <Select label="资源领域" value={domain ?? ''} options={[{ value: '', label: '全部领域' },
          ...[...new Set(domains.map(group => group.domain))].map(value => ({ value, label: value }))]}
          onChange={value => setDomain(value === '' ? null : value)} />
        <Button variant="ghost" pressed={!all} onClick={() => setAll(false)}>当前载体</Button>
        <Button variant="ghost" pressed={all} onClick={() => setAll(true)}>全部</Button>
        <Button variant="ghost" pressed={issuesOnly} onClick={() => setIssuesOnly(!issuesOnly)}>只看问题</Button>
      </PanelBar>
      <TextInput aria-label="搜索领域资源" placeholder="搜索名称、引用值或上下文" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
      {activeField !== null && <div className={styles.note}>当前字段：{activeField.name} · {activeField.domain}
        {activeField.selection === 'mapped' ? ' · 选择当前载体的已有映射' : activeField.selection === 'asset' ? ' · 选择实际文件或子对象' : ''}</div>}
      {missing !== null && <div className={styles.issue}>当前字段引用不在所选载体的正式目录中，原值仍保留：{missing.value}</div>}
      {!all && currentScope === null && activeField?.selection === 'mapped' ? <Empty text="当前字段尚未选择载体上下文；选择载体或切换到全部资源。" />
        : rows.length === 0 ? <Empty text={issuesOnly && missing === null ? '当前筛选范围没有目录冲突' : '当前筛选范围没有资源'} />
          : <VirtualList className={styles.contentList} count={rows.length} rowHeight={40} label="适配者领域资源目录"
            revealKey={JSON.stringify([field?.id, currentValue, domain])} revealIndex={current < 0 ? undefined : current} render={index => {
              const row = rows[index];
              if (row.kind === 'domain') return <div className={styles.heading}>{row.domain}</div>;
              if (row.kind === 'missing') return <button className={styles.row}
                aria-selected={selected?.kind === 'missing' && selected.domain === row.domain && selected.value === row.value}
                onClick={() => onSelect(row)}><span>{row.value}<small className={styles.issue}>当前字段引用缺失</small></span>
                <small>{row.fieldName} · 原值已保留</small></button>;
              const item = row.item;
              const conflict = conflicts.has(resolutionKey(item));
              return <button className={styles.row} title={item.value} aria-selected={selectedItem !== null && catalogItemKey(selectedItem) === catalogItemKey(item)}
                draggable={!conflict} onDragStart={event => writeReferenceDrag(event.dataTransfer, item.domain, resourceReferenceChoice(item, null))}
                onClick={() => onSelect({ kind: 'catalog', item })}>
                <span>{item.display}{conflict && <small className={styles.issue}>目录条目冲突</small>}</span>
                <small>{item.value} · {item.adapter}{all ? ` · ${item.context || '公共'}` : ''}</small>
              </button>;
            }} />}
      <div className={styles.note}>{shown.length + (shownMissing === null ? 0 : 1)} 项 · 目录修订 {revision}</div>
    </div>
    <div className={styles.contentDetails}>
      {selected === null ? <Empty text="选择资源查看引用者及引用替换；聚焦引用字段可定位当前值。" /> : <>
        {selected.kind === 'missing' ? <div className={styles.facts}><strong className={styles.issue}>当前引用不在正式目录中</strong>
          <span>{selected.domain}</span><code>{selected.value}</code><span>原作者值仍保留，可查看引用者并替换为同领域的实际目录条目。</span></div>
          : <div className={styles.facts}><strong>{selected.item.display}</strong><span>{selected.item.adapter} · {selected.item.domain}</span>
            <code>{selected.item.value}</code><small>上下文：{selected.item.context || '公共'}</small></div>}
        {choices.map((choice, index) => <Button key={index} disabled={choice.conflict !== null || conflicts.has(resolutionKey(selectedItem!))}
          tip={choice.conflict ?? undefined} onClick={() => field!.choose(choice)}>
          {matchesReferenceChoice(choice, field!) ? `已设置 ${field!.name}` : `填入 ${field!.name}`}
        </Button>)}
        {activeField !== null && activeField.domain !== (selected.kind === 'catalog' ? selected.item.domain : selected.domain) && <div className={styles.note}>
          当前字段只接受 {activeField.domain}，所选资源属于 {selected.kind === 'catalog' ? selected.item.domain : selected.domain}。
        </div>}
        {details}
      </>}
    </div>
  </div>;
}
