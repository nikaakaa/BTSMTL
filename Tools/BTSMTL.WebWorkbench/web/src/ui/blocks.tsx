import { Collapsible } from '@base-ui/react/collapsible';
import { Tabs as BaseTabs } from '@base-ui/react/tabs';
import { createContext, Fragment, useContext, useReducer, useState, type ReactNode } from 'react';
import type { Field } from '../api/types';
import styles from './blocks.module.css';

// 面板空状态：只接受一句话。
export function Empty({ text }: { text: string }) {
  return <div className={styles.empty}>{text}</div>;
}

// 开合按所在位置记住（外层折叠的名字链 + 自身名字）：换选同类对象后保持上次开合，照 Unity 的 foldout。
const foldMemory = new Map<string, boolean>();
const FoldPath = createContext('');

// 折叠：顶层按 Unity 组件头画，nested 为字段里的小三角折叠行（结构体、列表、列表元素、曲线）。
// summary 写在标题行右侧，收起时也能看出内容；actions 是标题行上的按钮（添加、删除），不随点击开合。
// name 用于记住开合，缺省取字符串标题；标题不是字符串又没给 name 时只在本次显示内记住。
export function Fold({ title, name, summary, actions, defaultOpen = false, nested = false, children }: {
  title: ReactNode;
  name?: string;
  summary?: ReactNode;
  actions?: ReactNode;
  defaultOpen?: boolean;
  nested?: boolean;
  children: ReactNode;
}) {
  const parent = useContext(FoldPath);
  const own = name ?? (typeof title === 'string' ? title : null);
  const key = own === null ? null : `${parent}/${own}`;
  const [local, setLocal] = useState(defaultOpen);
  const [, remembered] = useReducer((count: number) => count + 1, 0);
  const open = key === null ? local : foldMemory.get(key) ?? defaultOpen;
  const toggle = (next: boolean) => {
    if (key === null) setLocal(next);
    else { foldMemory.set(key, next); remembered(); }
  };
  return (
    <Collapsible.Root open={open} onOpenChange={toggle} className={nested ? styles.foldout : styles.fold}>
      <div className={nested ? styles.foldoutHead : styles.foldHead}>
        <Collapsible.Trigger className={nested ? styles.foldoutTrigger : styles.foldTrigger}>
          <span className={styles.foldArrow} />
          <span className={styles.foldTitle}>{title}</span>
          {summary != null && summary !== '' && <span className={styles.foldSummary}>{summary}</span>}
        </Collapsible.Trigger>
        {actions}
      </div>
      <Collapsible.Panel className={nested ? styles.foldoutPanel : styles.foldPanel}>
        <FoldPath.Provider value={key ?? parent}>{children}</FoldPath.Provider>
      </Collapsible.Panel>
    </Collapsible.Root>
  );
}

export function PanelBar({ children, wrap }: { children: ReactNode; wrap?: boolean }) {
  return <div className={styles.panelBar} data-wrap={wrap}>{children}</div>;
}

export function BarSeparator() {
  return <span className={styles.barSeparator} />;
}

export function BarLabel({ children }: { children: ReactNode }) {
  return <span className={styles.barLabel}>{children}</span>;
}

export function Facts({ fields }: { fields: Field[] }) {
  return (
    <dl className={styles.facts}>
      {fields.map((field, index) => (
        <Fragment key={index}>
          <dt>{field.displayName}</dt>
          <dd>{field.value}</dd>
        </Fragment>
      ))}
    </dl>
  );
}

// 身份、修订、哈希只在这里显示（检查器底部）。
export function IdentityFold({ fields }: { fields: Field[] }) {
  if (!fields.length) return null;
  return (
    <Fold title="身份">
      <Facts fields={fields} />
    </Fold>
  );
}

export interface TabItem<T extends string> {
  value: T;
  label: ReactNode;
  content: ReactNode;
}

export function Tabs<T extends string>({ items, value, onChange }: { items: TabItem<T>[]; value: T; onChange(value: T): void }) {
  return (
    <BaseTabs.Root value={value} onValueChange={next => onChange(next as T)} className={styles.tabs}>
      <BaseTabs.List className={styles.tabList}>
        {items.map(item => <BaseTabs.Tab key={item.value} value={item.value} className={styles.tab}>{item.label}</BaseTabs.Tab>)}
      </BaseTabs.List>
      {items.map(item => (
        <BaseTabs.Panel key={item.value} value={item.value} className={styles.tabPanel}>{item.content}</BaseTabs.Panel>
      ))}
    </BaseTabs.Root>
  );
}
