import { useEffect, useRef, useState } from 'react';
import { consoleActions, useWorkbench, type ConsoleEntry } from '../state/store';
import { usePanelApi, usePanelVisible } from '../shell/panel';
import { panelInfo } from '../shell/panelTypes';
import { Button } from '../ui/controls';
import { Empty } from '../ui/blocks';
import styles from './console.module.css';

export const sourceNames: Record<string, string> = {
  run: '运行', connection: '连接', transport: '播放控制', layout: '布局', content: '内容', query: '记录查询',
  player: '画面', viewport: '逻辑视图', resources: '动画资源', graph: '节点图', timeline: 'Timeline',
  frames: '帧记录', inspector: '检查器',
};

const time = (value: number) => {
  const date = new Date(value);
  const pad = (number: number, width = 2) => String(number).padStart(width, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
};

function Entry({ entry, focused }: { entry: ConsoleEntry; focused: boolean }) {
  const [open, setOpen] = useState(false);
  const element = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!focused) return;
    setOpen(!!entry.raw);
    element.current?.scrollIntoView({ block: 'nearest' });
  }, [focused, entry.raw]);
  return (
    <div ref={element} className={styles.entry} data-level={entry.level} data-focused={focused || undefined}>
      <button className={styles.line} disabled={!entry.raw} onClick={() => setOpen(!open)} aria-expanded={entry.raw ? open : undefined}>
        <span className={styles.arrow}>{entry.raw ? (open ? '▾' : '▸') : ''}</span>
        <span className={styles.time}>{time(entry.time)}</span>
        <span className={styles.source}>{sourceNames[entry.source] ?? entry.source}</span>
        <span className={styles.reason}>{entry.reason}</span>
      </button>
      {open && entry.raw && <pre className={styles.raw}>{entry.raw}</pre>}
    </div>
  );
}

// 控制台：错误、警告与回执集中在这里；页签显示未读数。
export function ConsolePanel() {
  const entries = useWorkbench(state => state.entries);
  const unread = useWorkbench(state => state.unread);
  const focusedEntry = useWorkbench(state => state.focusedEntry);
  const visible = usePanelVisible();
  const api = usePanelApi();
  const list = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (visible) consoleActions.markRead();
  }, [visible, entries]);

  useEffect(() => {
    api.setTitle(unread ? `${panelInfo.console.title} ● ${unread}` : panelInfo.console.title);
  }, [api, unread]);

  useEffect(() => {
    const element = list.current;
    if (element && focusedEntry === null) element.scrollTop = element.scrollHeight;
  }, [entries, focusedEntry]);

  return (
    <div className={styles.console}>
      <div className={styles.bar}>
        <Button variant="ghost" disabled={!entries.length} onClick={consoleActions.clear}>清空</Button>
      </div>
      {entries.length ? (
        <div className={styles.list} ref={list}>
          {entries.map(entry => <Entry key={entry.id} entry={entry} focused={entry.id === focusedEntry} />)}
        </div>
      ) : <Empty text="没有记录" />}
    </div>
  );
}
