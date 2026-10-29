import { Button as BaseButton } from '@base-ui/react/button';
import { Checkbox as BaseCheckbox } from '@base-ui/react/checkbox';
import { ContextMenu as BaseContextMenu } from '@base-ui/react/context-menu';
import { Input } from '@base-ui/react/input';
import { Menu as BaseMenu } from '@base-ui/react/menu';
import { NumberField } from '@base-ui/react/number-field';
import { Select as BaseSelect } from '@base-ui/react/select';
import { Tooltip } from '@base-ui/react/tooltip';
import type { ComponentProps, ReactElement, ReactNode } from 'react';
import styles from './controls.module.css';
import { usePortalContainer } from './portal';

const join = (...names: (string | false | undefined)[]) => names.filter(Boolean).join(' ');

// ── 悬停提示：解释与不可用原因都放这里，不写进面板正文 ──

export function TipProvider({ children }: { children: ReactNode }) {
  return <Tooltip.Provider delay={500} closeDelay={0}>{children}</Tooltip.Provider>;
}

export function Tip({ content, children }: { content: ReactNode; children: ReactElement }) {
  const container = usePortalContainer();
  if (content === null || content === undefined || content === '') return children;
  return (
    <Tooltip.Root>
      <Tooltip.Trigger render={children} />
      <Tooltip.Portal container={container}>
        <Tooltip.Positioner className={styles.positioner} sideOffset={6}>
          <Tooltip.Popup className={styles.tooltip}>{content}</Tooltip.Popup>
        </Tooltip.Positioner>
      </Tooltip.Portal>
    </Tooltip.Root>
  );
}

// ── 按钮 ──

export interface ButtonProps extends Omit<ComponentProps<typeof BaseButton>, 'className'> {
  variant?: 'default' | 'icon' | 'ghost';
  pressed?: boolean;
  tip?: ReactNode;
  className?: string;
}

export function Button({ variant = 'default', pressed, tip, className, ...props }: ButtonProps) {
  const button = (
    <BaseButton
      {...props}
      focusableWhenDisabled={tip ? true : props.focusableWhenDisabled}
      aria-pressed={pressed}
      className={join(styles.button, variant === 'icon' && styles.icon, variant === 'ghost' && styles.ghost, className)}
    />
  );
  return tip ? <Tip content={tip}>{button}</Tip> : button;
}

// ── 下拉选择 ──

export interface Option<T extends string> {
  value: T;
  label: ReactNode;
  disabled?: boolean;
}

export interface SelectProps<T extends string> {
  value: T | null;
  options: Option<T>[];
  onChange(value: T): void;
  placeholder?: string;
  disabled?: boolean;
  label: string;
  tip?: ReactNode;
}

export function Select<T extends string>({ value, options, onChange, placeholder, disabled, label, tip }: SelectProps<T>) {
  const container = usePortalContainer();
  const trigger = (
    <BaseSelect.Trigger className={styles.trigger} aria-label={label}>
      <BaseSelect.Value className={styles.triggerValue} placeholder={placeholder} />
      <span className={styles.caret} aria-hidden />
    </BaseSelect.Trigger>
  );
  return (
    <BaseSelect.Root<T>
      value={value}
      items={options.map(option => ({ value: option.value, label: option.label }))}
      onValueChange={next => { if (next !== null) onChange(next); }}
      disabled={disabled}
    >
      {tip ? <Tip content={tip}>{trigger}</Tip> : trigger}
      <BaseSelect.Portal container={container}>
        <BaseSelect.Positioner className={styles.positioner} alignItemWithTrigger={false} sideOffset={2}>
          <BaseSelect.Popup className={styles.popup}>
            <BaseSelect.List>
              {options.map(option => (
                <BaseSelect.Item key={option.value} value={option.value} disabled={option.disabled} className={styles.item}>
                  <BaseSelect.ItemIndicator className={styles.indicator}>✓</BaseSelect.ItemIndicator>
                  <BaseSelect.ItemText>{option.label}</BaseSelect.ItemText>
                </BaseSelect.Item>
              ))}
            </BaseSelect.List>
          </BaseSelect.Popup>
        </BaseSelect.Positioner>
      </BaseSelect.Portal>
    </BaseSelect.Root>
  );
}

// ── 菜单与右键菜单 ──

export type MenuEntry =
  | { kind?: 'item'; label: ReactNode; onSelect(): void; disabled?: boolean; tip?: ReactNode }
  | { kind: 'check'; label: ReactNode; checked: boolean; onChange(checked: boolean): void; disabled?: boolean }
  | { kind: 'separator' }
  | { kind: 'label'; label: ReactNode };

type MenuParts = typeof BaseMenu | typeof BaseContextMenu;

function MenuEntries({ parts, entries }: { parts: MenuParts; entries: MenuEntry[] }) {
  return entries.map((entry, index) => {
    switch (entry.kind) {
      case 'separator':
        return <parts.Separator key={index} className={styles.separator} />;
      case 'label':
        return <div key={index} className={styles.groupLabel}>{entry.label}</div>;
      case 'check':
        return (
          <parts.CheckboxItem key={index} className={styles.item} checked={entry.checked}
            onCheckedChange={entry.onChange} disabled={entry.disabled}>
            <parts.CheckboxItemIndicator className={styles.indicator}>✓</parts.CheckboxItemIndicator>
            {entry.label}
          </parts.CheckboxItem>
        );
      default: {
        const item = (
          <parts.Item key={index} className={styles.item} onClick={entry.onSelect} disabled={entry.disabled}>
            {entry.label}
          </parts.Item>
        );
        return entry.tip ? <Tip key={index} content={entry.tip}>{item}</Tip> : item;
      }
    }
  });
}

export function MenuButton({ label, entries, children, variant = 'default', caret = false }: {
  label: string;
  entries: MenuEntry[];
  children: ReactNode;
  variant?: ButtonProps['variant'];
  caret?: boolean;
}) {
  const container = usePortalContainer();
  return (
    <BaseMenu.Root>
      <BaseMenu.Trigger aria-label={label}
        className={join(styles.button, variant === 'icon' && styles.icon, variant === 'ghost' && styles.ghost)}>
        {children}
        {caret && <span className={styles.caret} />}
      </BaseMenu.Trigger>
      <BaseMenu.Portal container={container}>
        <BaseMenu.Positioner className={styles.positioner} sideOffset={2} align="start">
          <BaseMenu.Popup className={styles.popup}>
            <MenuEntries parts={BaseMenu} entries={entries} />
          </BaseMenu.Popup>
        </BaseMenu.Positioner>
      </BaseMenu.Portal>
    </BaseMenu.Root>
  );
}

export function ContextMenu({ entries, children }: { entries: MenuEntry[]; children: ReactElement }) {
  const container = usePortalContainer();
  return (
    <BaseContextMenu.Root>
      <BaseContextMenu.Trigger render={children} />
      <BaseContextMenu.Portal container={container}>
        <BaseContextMenu.Positioner className={styles.positioner}>
          <BaseContextMenu.Popup className={styles.popup}>
            <MenuEntries parts={BaseContextMenu} entries={entries} />
          </BaseContextMenu.Popup>
        </BaseContextMenu.Positioner>
      </BaseContextMenu.Portal>
    </BaseContextMenu.Root>
  );
}

// ── 输入 ──

// 对象引用框：kind 为类型色变量，如 'var(--track-anim)'。给出 onOpen 时整框可点击，跳到所引用的对象。
export function ObjectField({ text, kind, className, onOpen, tip }: {
  text: ReactNode; kind?: string; className?: string; onOpen?(): void; tip?: string;
}) {
  const content = (
    <>
      <i className={styles.objectIcon} />
      <span className={styles.objectText}>{text}</span>
      <i className={styles.objectPick} />
    </>
  );
  const style = kind ? { ['--kind' as string]: kind } : undefined;
  return onOpen
    ? <button type="button" className={join(styles.objectField, styles.objectLink, className)} style={style} title={tip} onClick={onOpen}>{content}</button>
    : <span className={join(styles.objectField, className)} style={style} title={tip}>{content}</span>;
}

export function CheckMark({ checked }: { checked: boolean }) {
  return <span className={styles.check} aria-hidden>{checked ? '✓' : ''}</span>;
}

export function Checkbox({ checked, onChange, disabled, label }: { checked: boolean; onChange(checked: boolean): void; disabled?: boolean; label: string }) {
  return (
    <BaseCheckbox.Root className={styles.check} checked={checked} onCheckedChange={next => onChange(next)} disabled={disabled} aria-label={label}>
      <BaseCheckbox.Indicator>✓</BaseCheckbox.Indicator>
    </BaseCheckbox.Root>
  );
}

export function TextInput({ className, ...props }: Omit<ComponentProps<typeof Input>, 'className'> & { className?: string }) {
  return <Input {...props} className={join(styles.field, className)} />;
}

export interface NumberInputProps {
  value: number | null;
  onChange(value: number | null): void;
  min?: number;
  max?: number;
  step?: number;
  disabled?: boolean;
  label: string;
  // 占满所在格（检查器字段），否则为固定窄框。
  wide?: boolean;
}

// 按原值显示：不分组、不截断小数，避免作者数值在聚焦提交时被显示精度改写。
const exactNumber: Intl.NumberFormatOptions = { useGrouping: false, maximumFractionDigits: 12 };

export function NumberInput({ value, onChange, min, max, step, disabled, label, wide }: NumberInputProps) {
  return (
    <NumberField.Root value={value} onValueCommitted={next => onChange(next)} min={min} max={max} step={step}
      disabled={disabled} format={exactNumber}>
      <NumberField.Group className={join(styles.numberGroup, wide && styles.numberWide)}>
        <NumberField.Input className={styles.field} aria-label={label} />
      </NumberField.Group>
    </NumberField.Root>
  );
}
