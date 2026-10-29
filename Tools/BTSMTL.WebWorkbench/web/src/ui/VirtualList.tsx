import { useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import styles from './virtual.module.css';

// 定高虚拟滚动列表：只渲染可见行；尺寸按列表所在窗口观察（拆出窗口同样适用）。
export function VirtualList({ count, rowHeight, render, className, label }: {
  count: number;
  rowHeight: number;
  render(index: number): ReactNode;
  className?: string;
  label?: string;
}) {
  const element = useRef<HTMLDivElement>(null);
  const [view, setView] = useState({ top: 0, height: 400 });
  useLayoutEffect(() => {
    const target = element.current;
    if (!target) return;
    const owner = target.ownerDocument.defaultView as (Window & { ResizeObserver: typeof ResizeObserver }) | null;
    if (!owner) return;
    const observer = new owner.ResizeObserver(() => setView(previous => ({ ...previous, height: target.clientHeight })));
    observer.observe(target);
    return () => observer.disconnect();
  }, []);
  const first = Math.max(0, Math.floor(view.top / rowHeight) - 4);
  const last = Math.min(count, Math.ceil((view.top + view.height) / rowHeight) + 4);
  const rows: ReactNode[] = [];
  for (let index = first; index < last; index++)
    rows.push(<div key={index} className={styles.row} style={{ top: index * rowHeight, height: rowHeight }}>{render(index)}</div>);
  return (
    <div ref={element} className={`${styles.list} ${className ?? ''}`} role="list" aria-label={label}
      onScroll={event => {
        const top = event.currentTarget.scrollTop;
        setView(previous => previous.top === top ? previous : { ...previous, top });
      }}>
      <div className={styles.space} style={{ height: count * rowHeight }}>{rows}</div>
    </div>
  );
}
