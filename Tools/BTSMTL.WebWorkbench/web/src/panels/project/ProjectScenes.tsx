import { useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { openPanel } from '../../shell/layout';
import { session } from '../../state/session';
import { contentActions, contentSelectionKey, previewContentChoices, selectionActions, useWorkbench, type PreviewContentChoice } from '../../state/store';
import { Empty } from '../../ui/blocks';
import styles from './project.module.css';

// Project 场景页：各 Unity 场景的当前发布按名字列出，旧修订和临时包收进“历史”。
// 点选即切换预览内容并在检查器显示运行设置；修订与路径只在检查器身份折叠里。
export function ProjectScenes() {
  const { sources, selection, hostAvailable } = useWorkbench(useShallow(state => ({
    sources: state.sources, selection: state.contentSelection, hostAvailable: state.hostAvailable,
  })));
  const [history, setHistory] = useState(false);
  if (!sources) return <Empty text={hostAvailable === false ? '未接入运行宿主' : '正在读取场景'} />;
  const choices = previewContentChoices(sources);
  if (!choices.length) return <Empty text="宿主尚未提供场景" />;
  const key = contentSelectionKey(selection);
  const older = choices.filter(choice => !choice.current);
  const row = (choice: PreviewContentChoice, depth: number) => (
    <div key={choice.value} className={styles.contentRow} style={{ paddingLeft: 8 + depth * 14 }} aria-selected={choice.value === key}>
      <button className={styles.contentName} onClick={() => {
        contentActions.selectContent(choice.selection);
        selectionActions.select({ kind: 'scene', id: choice.value });
        openPanel('inspector');
      }}>
        <span>{choice.label}</span>
        {depth === 0 && <small>{choice.scenePath}</small>}
      </button>
    </div>
  );
  return <div className={styles.contentTree}>
    {choices.filter(choice => choice.current).map(choice => row(choice, 0))}
    {older.length > 0 && <div className={styles.contentRow} style={{ paddingLeft: 8 }}>
      <button className={styles.contentName} aria-expanded={history} onClick={() => setHistory(!history)}>
        <span>{history ? '▾' : '▸'} 历史（{older.length}）</span>
        <small>旧修订、旧发布包与技能刷新包</small>
      </button>
    </div>}
    {history && older.map(choice => row(choice, 1))}
    <div className={styles.contentRow} style={{ paddingLeft: 8 }}>
      <button className={styles.contentName} disabled={!hostAvailable} onClick={() => void session.refresh()}>
        <span>刷新场景列表</span>
      </button>
    </div>
  </div>;
}
