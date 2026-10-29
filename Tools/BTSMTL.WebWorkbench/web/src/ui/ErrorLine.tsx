import { openPanel } from '../shell/layout';
import { consoleActions } from '../state/store';
import styles from './blocks.module.css';

// 面板内出错位置的一行提示：点击跳到控制台对应记录。
export function ErrorLine({ text, entryId }: { text: string; entryId: number | null }) {
  return (
    <button className={styles.errorLine} onClick={() => {
      openPanel('console');
      consoleActions.focus(entryId);
    }}>{text}</button>
  );
}
