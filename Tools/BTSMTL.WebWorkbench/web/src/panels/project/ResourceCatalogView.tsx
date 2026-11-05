import { useEffect, useMemo, useState, type ReactNode } from 'react';
import type { AdapterCatalogDomain, AdapterCatalogItem } from '../../api/authoring';
import { matchesReferenceChoice, referenceValueKey, writeReferenceDrag, type ReferenceFieldSelection } from '../../state/referenceSelection';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, Select, TextInput } from '../../ui/controls';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

export const catalogItemKey = (item: Pick<AdapterCatalogItem, 'adapter' | 'domain' | 'value' | 'context'>) =>
  JSON.stringify([item.adapter, item.domain, item.value, item.context]);

export function ResourceCatalogView({ domains, revision, currentScope, field, selected, onSelect, details }: {
  domains: AdapterCatalogDomain[]; revision: string; currentScope: { adapter: string; context: string } | null;
  field: ReferenceFieldSelection | null; selected: AdapterCatalogItem | null; onSelect(item: AdapterCatalogItem | null): void; details: ReactNode;
}) {
  const [domain, setDomain] = useState<string | null>(field?.domain ?? null);
  const [all, setAll] = useState(false);
  const [issuesOnly, setIssuesOnly] = useState(false);
  const [search, setSearch] = useState('');
  useEffect(() => { if (field?.domain != null) setDomain(field.domain); }, [field?.id, field?.domain]);
  const catalogItems = useMemo(() => domains.flatMap(group => group.items.map(item => ({ ...item, adapter: group.adapter, domain: group.domain }))), [domains]);
  const conflicts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const item of catalogItems) counts.set(catalogItemKey(item), (counts.get(catalogItemKey(item)) ?? 0) + 1);
    return new Set([...counts].filter(([, count]) => count > 1).map(([key]) => key));
  }, [catalogItems]);
  const activeField = field?.domain == null ? null : field;
  const fieldItems = activeField?.catalog.kind === 'ready' ? activeField.catalog.items : [];
  const currentValue = activeField?.value;
  const inCurrentScope = (item: AdapterCatalogItem) => currentScope === null ? item.context === '' : item.adapter === currentScope.adapter &&
    (item.context === currentScope.context || item.context === '' && !catalogItems.some(other =>
      other.adapter === item.adapter && other.domain === item.domain && other.value === item.value && other.context === currentScope.context));
  useEffect(() => {
    if (field?.domain == null) return;
    const matches = catalogItems.filter(item => item.domain === field.domain && item.value === field.value && inCurrentScope(item));
    onSelect(matches.length === 1 ? matches[0] : null);
  }, [field?.id, field?.domain, field?.value, catalogItems, currentScope?.adapter, currentScope?.context, onSelect]);
  const missing = activeField !== null && typeof currentValue === 'string' && currentValue !== '' &&
    !catalogItems.some(item => item.domain === activeField.domain && item.value === currentValue && inCurrentScope(item));
  const query = search.toLocaleLowerCase();
  const shown = catalogItems.filter(item => (domain === null || item.domain === domain) && (all || currentScope !== null && inCurrentScope(item)) &&
    (!issuesOnly || conflicts.has(catalogItemKey(item))) &&
    `${item.display} ${item.value} ${item.adapter} ${item.context}`.toLocaleLowerCase().includes(query));
  const rows: ({ kind: 'domain'; domain: string } | { kind: 'item'; item: AdapterCatalogItem })[] = [];
  for (const name of [...new Set(shown.map(item => item.domain))]) {
    rows.push({ kind: 'domain', domain: name });
    rows.push(...shown.filter(item => item.domain === name).map(item => ({ kind: 'item' as const, item })));
  }
  const current = rows.findIndex(row => row.kind === 'item' && row.item.domain === activeField?.domain &&
    referenceValueKey(row.item.value) === referenceValueKey(currentValue));
  const choices = selected !== null && inCurrentScope(selected) && activeField?.domain === selected.domain && activeField.catalog.kind === 'ready'
    ? fieldItems.filter(choice => referenceValueKey(choice.value) === referenceValueKey(selected.value)) : [];
  return <div className={styles.contentGrid}>
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
      {missing && <div className={styles.issue}>当前字段引用不在所选载体的正式目录中，原值仍保留：{currentValue}</div>}
      {!all && currentScope === null ? <Empty text="当前文档尚未选择载体上下文；选择载体或切换到全部资源。" />
        : rows.length === 0 ? <Empty text={issuesOnly && !missing ? '当前筛选范围没有目录冲突' : '当前筛选范围没有资源'} />
          : <VirtualList className={styles.contentList} count={rows.length} rowHeight={40} label="适配者领域资源目录"
            revealKey={JSON.stringify([field?.id, currentValue, domain])} revealIndex={current < 0 ? undefined : current} render={index => {
              const row = rows[index];
              if (row.kind === 'domain') return <div className={styles.heading}>{row.domain}</div>;
              const item = row.item;
              const conflict = conflicts.has(catalogItemKey(item));
              const choices = inCurrentScope(item) && activeField?.domain === item.domain && activeField.catalog.kind === 'ready'
                ? fieldItems.filter(choice => referenceValueKey(choice.value) === referenceValueKey(item.value) && choice.conflict === null) : [];
              return <button className={styles.row} aria-selected={selected !== null && catalogItemKey(selected) === catalogItemKey(item)}
                draggable={!conflict && choices.length === 1} onDragStart={event => {
                  if (activeField?.catalog.kind === 'ready' && choices.length === 1)
                    writeReferenceDrag(event.dataTransfer, activeField.catalog.referenceKind, choices[0]);
                }} onClick={() => onSelect(item)}>
                <span>{item.display}{conflict && <small className={styles.issue}>目录条目冲突</small>}</span>
                <small>{item.adapter}{all ? ` · ${item.context || '公共'}` : ''}</small>
              </button>;
            }} />}
      <div className={styles.note}>{shown.length} 项 · 目录修订 {revision}</div>
    </div>
    <div className={styles.contentDetails}>
      {selected === null ? <Empty text="选择资源查看引用者及引用替换；聚焦引用字段可定位当前值。" /> : <>
        <div className={styles.facts}><strong>{selected.display}</strong><span>{selected.adapter} · {selected.domain}</span>
          <code>{selected.value}</code><small>上下文：{selected.context || '公共'}</small></div>
        {choices.map((choice, index) => <Button key={index} disabled={choice.conflict !== null || conflicts.has(catalogItemKey(selected))}
          tip={choice.conflict ?? undefined} onClick={() => field!.choose(choice)}>
          {matchesReferenceChoice(choice, field!) ? `已设置 ${field!.name}` : `填入 ${field!.name}`}
        </Button>)}
        {activeField !== null && activeField.domain !== selected.domain && <div className={styles.note}>
          当前字段只接受 {activeField.domain}，所选资源属于 {selected.domain}。
        </div>}
        {details}
      </>}
    </div>
  </div>;
}
