import { Popover } from '@base-ui/react/popover';
import { useEffect, useMemo, useState } from 'react';
import type { ReferenceChoice } from '../../api/authoring';
import {
  acceptsReferenceDrag, matchesReferenceChoice, readReferenceDrop, referenceValueKey, useReferenceSelection, writeReferenceDrag,
  type ReferenceCatalog, type ResourceObjectFieldSelection,
} from '../../state/referenceSelection';
import type { JsonObject, JsonValue } from '../../state/skillJson';
import { Button, TextInput, Tip } from '../../ui/controls';
import { usePortalContainer } from '../../ui/portal';
import { VirtualList } from '../../ui/VirtualList';
import { openPanel } from '../../shell/layout';
import field from '../../ui/controls.module.css';
import styles from './referencePicker.module.css';

// 引用的可读文字：字符串去掉 JSON 引号，其它值保留原 JSON。
export const referenceText = (value: JsonValue | undefined) =>
  typeof value === 'string' ? value : referenceValueKey(value);

const folderOf = (text: string) => text.slice(0, Math.max(text.lastIndexOf('/'), 0));
const sharedLength = (a: string, b: string) => {
  let index = 0;
  while (index < a.length && index < b.length && a[index] === b[index]) index++;
  return index;
};

// 照 Unity 对象框：一行写出所引用对象的名字，点开在字段旁选择；资源字段可到资源面板浏览与定位。
export function ReferencePicker({ id, label, value, configuration, catalog, onChoose, resourceObjects, domain = null, selection = null, resourceContext = null }: {
  id: string; label: string; value: JsonValue | undefined; configuration: JsonObject; catalog: ReferenceCatalog;
  onChoose(choice: ReferenceChoice): void;
  resourceObjects?: ResourceObjectFieldSelection;
  domain?: string | null;
  selection?: 'asset' | 'mapped' | null;
  resourceContext?: { adapter: string; context: string } | null;
}) {
  const container = usePortalContainer();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [rejection, setRejection] = useState<string | null>(null);
  const current = { value, configuration };
  const selected = catalog.kind === 'ready'
    ? catalog.items.filter(item => matchesReferenceChoice(item, current)) : [];
  const unset = value === undefined || value === null || value === '';
  const invalid = catalog.kind === 'ready' && selected.length === 0 && !unset;
  const currentText = unset ? '' : referenceText(value) ?? '';
  const title = selected.length ? [...new Set(selected.map(item => item.name))].join(' / ') : unset ? '未设置' : currentText;
  // 当前值排第一，其余按与当前值共享的目录长度排在前面，同一角色、同一目录的候选靠前。
  const ordered = useMemo(() => {
    if (catalog.kind !== 'ready') return [];
    const folder = folderOf(currentText);
    return catalog.items.map((item, index) => ({ item, index, selected: matchesReferenceChoice(item, current),
      near: folder === '' ? 0 : sharedLength(folder, folderOf(referenceText(item.value) ?? '')) }))
      .sort((a, b) => Number(b.selected) - Number(a.selected) || b.near - a.near || a.index - b.index)
      .map(entry => entry.item);
  }, [catalog, currentText, referenceValueKey(value), configuration]);
  const query = search.trim().toLocaleLowerCase();
  const items = query === '' ? ordered : ordered.filter(item =>
    [item.name, item.detail, referenceText(item.value)].some(text => text?.toLocaleLowerCase().includes(query)));
  const focus = () => useReferenceSelection.setState(state => ({ browsing: state.browsing && state.field?.id === id,
    field: { id, name: label, domain, selection, resourceContext, catalog, value, configuration, choose: onChoose, resourceObjects } }));
  useEffect(() => {
    const current = useReferenceSelection.getState().field;
    if (current?.id === id) useReferenceSelection.setState({ field: { id, name: label, domain, selection, resourceContext, catalog, value, configuration, choose: onChoose, resourceObjects } });
  }, [id, label, domain, selection, resourceContext, catalog, value, configuration, onChoose, resourceObjects]);
  useEffect(() => () => {
    if (useReferenceSelection.getState().field?.id === id) useReferenceSelection.setState({ field: null, browsing: false });
  }, [id]);
  const choose = (choice: ReferenceChoice) => {
    onChoose(choice);
    setRejection(null);
    setOpen(false);
    setSearch('');
  };
  const hint = selection === 'mapped' ? '只列出当前角色已配置的映射；新增映射在该角色的 Profile 中添加。'
    : selection === 'asset' ? '可从列表选择，也可从资源面板拖入。' : null;
  const browse = () => { focus(); useReferenceSelection.setState({ browsing: true }); openPanel('resources'); };
  return <div className={styles.root} onFocusCapture={focus}
    onDragOver={event => {
      if (catalog.kind === 'ready' && acceptsReferenceDrag(event.dataTransfer, catalog.referenceKind)) {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
      }
    }}
    onDrop={event => {
      event.preventDefault();
      if (selection === 'mapped' && event.dataTransfer.files.length !== 0) {
        setRejection('这个字段只能选当前角色已配置的映射，不能直接拖入文件；原值没有改动。');
        return;
      }
      const result = readReferenceDrop(event.dataTransfer, catalog);
      if (result.kind === 'selected') choose(result.choice);
      else setRejection(result.reason);
    }}>
    <div className={styles.line}>
      <Popover.Root open={open} onOpenChange={next => { if (next) focus(); setOpen(next); if (!next) setSearch(''); }}>
        <Tip name={title} content={catalog.kind === 'unavailable' ? catalog.reason : !unset && currentText !== title ? currentText : undefined}>
          <Popover.Trigger aria-label={label} className={`${field.objectField} ${field.objectLink} ${styles.trigger}`}
            data-state={invalid ? 'invalid' : unset ? 'unset' : undefined}>
            <i className={field.objectIcon} />
            <span className={field.objectText} data-name>{title}</span>
            <i className={field.objectPick} />
          </Popover.Trigger>
        </Tip>
        <Popover.Portal container={container}>
          <Popover.Positioner className={styles.positioner} side="bottom" align="end" sideOffset={2} collisionPadding={8}>
            <Popover.Popup className={styles.popup} aria-label={`选择${label}`}>
              <div className={styles.head}><strong>选择 {label}</strong>{hint && <span>{hint}</span>}</div>
              {catalog.kind === 'unavailable' ? <div className={styles.note}>{catalog.reason}</div> : <>
                <TextInput autoFocus aria-label={`搜索${label}`} placeholder="搜索名称或路径" value={search}
                  onChange={event => setSearch(event.currentTarget.value)} />
                {items.length === 0 ? <div className={styles.note}>{catalog.items.length === 0 ? '没有可选的内容' : '没有匹配的结果'}</div>
                  : <VirtualList className={styles.list} count={items.length} rowHeight={40} role="listbox" label={label}
                    revealIndex={0} revealKey={query} render={index => {
                      const item = items[index];
                      const path = item.detail || referenceText(item.value);
                      return <button type="button" className={styles.item} role="option" aria-selected={matchesReferenceChoice(item, current)}
                        aria-posinset={index + 1} aria-setsize={items.length} disabled={item.conflict !== null} draggable={item.conflict === null}
                        onDragStart={event => writeReferenceDrag(event.dataTransfer, catalog.referenceKind, item)}
                        onClick={() => choose(item)}>
                        <strong>{item.name}</strong>
                        {item.conflict ? <small className={styles.issue}>{item.conflict}</small> : <small><bdi>{path}</bdi></small>}
                      </button>;
                    }} />}
                <div className={styles.count}>{query === '' ? `共 ${ordered.length} 项` : `找到 ${items.length} 项，共 ${ordered.length} 项`}</div>
              </>}
            </Popover.Popup>
          </Popover.Positioner>
        </Popover.Portal>
      </Popover.Root>
      {domain !== null && <Button variant="ghost" tip="在资源面板中浏览，并查看还有哪些地方引用它" onClick={browse}>浏览</Button>}
      {resourceObjects && <Button variant="ghost" disabled={resourceObjects.kind === 'unavailable'}
        tip={resourceObjects.kind === 'unavailable' ? resourceObjects.reason : '选择文件里的某个具体对象，例如 FBX 中的一段动画'}
        onClick={browse}>选文件内对象</Button>}
    </div>
    {invalid && <div className={styles.issue}>{catalog.invalidReason}</div>}
    {selected.map((item, index) => item.conflict && <div key={index} className={styles.issue}>{item.conflict}</div>)}
    {rejection && <div role="alert" className={styles.issue}>{rejection}</div>}
  </div>;
}
