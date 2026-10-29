import { Collapsible } from '@base-ui/react/collapsible';
import { Tabs as BaseTabs } from '@base-ui/react/tabs';
import { Fragment, type ReactNode } from 'react';
import type { Field } from '../api/types';
import styles from './blocks.module.css';

// 面板空状态：只接受一句话。
export function Empty({ text }: { text: string }) {
  return <div className={styles.empty}>{text}</div>;
}

// 折叠按 Unity 组件头画；kind 为类别色变量，如 'var(--node-anim)'。
export function Fold({ title, kind, defaultOpen = false, children }: {
  title: ReactNode;
  kind?: string;
  defaultOpen?: boolean;
  children: ReactNode;
}) {
  return (
    <Collapsible.Root defaultOpen={defaultOpen} className={styles.fold}>
      <Collapsible.Trigger className={styles.foldTrigger}>
        <span className={styles.foldArrow} />
        {kind && <span className={styles.foldKind} style={{ ['--kind' as string]: kind }} />}
        {title}
        <span className={styles.foldMore}>⋮</span>
      </Collapsible.Trigger>
      <Collapsible.Panel className={styles.foldPanel}>{children}</Collapsible.Panel>
    </Collapsible.Root>
  );
}

export function PanelBar({ children }: { children: ReactNode }) {
  return <div className={styles.panelBar}>{children}</div>;
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
