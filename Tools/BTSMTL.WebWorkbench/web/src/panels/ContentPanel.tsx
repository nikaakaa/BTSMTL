import { useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { ErrorLine } from '../ui/ErrorLine';
import { session } from '../state/session';
import { contentActions, useWorkbench } from '../state/store';
import { BarSeparator, Empty, Facts, Fold, PanelBar } from '../ui/blocks';
import { Button } from '../ui/controls';
import styles from './panel.module.css';

// 内容面板：场景与录像列表、输入声明；选择结果写入 content 分片，工具栏新建运行使用。
export function ContentPanel() {
  const { sources, hostAvailable } = useWorkbench(useShallow(state => ({ sources: state.sources, hostAvailable: state.hostAvailable })));
  const startBlocked = useWorkbench(() => session.canStart());
  return (
    <div className={styles.root}>
      <PanelBar>
        <Button variant="ghost" tip={startBlocked} disabled={!!startBlocked} onClick={() => void session.startRun()}>新建运行</Button>
        <BarSeparator />
        <Button variant="ghost" disabled={!hostAvailable} onClick={() => void session.refresh()}>刷新</Button>
      </PanelBar>
      <div className={styles.scroll}>
        {!sources ? <Empty text={hostAvailable === false ? '未接入运行宿主' : '读取中'} /> : (
          <>
            <Fold title="内容" kind="var(--node-anim)" defaultOpen><Scenes /></Fold>
            <Fold title="运行来源" kind="var(--track-in)" defaultOpen><RunSources /></Fold>
            <Fold title="输入声明" kind="var(--node-event)"><InputDeclarations /></Fold>
          </>
        )}
      </div>
    </div>
  );
}

function Scenes() {
  const { scenes, scenePath } = useWorkbench(useShallow(state => ({ scenes: state.sources?.scenes ?? [], scenePath: state.scenePath })));
  if (!scenes.length) return <Empty text="宿主尚未提供正式内容" />;
  return (
    <div className={styles.list} role="listbox" aria-label="内容">
      {scenes.map(scene => (
        <button key={scene.path} className={styles.row} role="option" aria-selected={scene.path === scenePath}
          onClick={() => contentActions.selectScene(scene.path)}>
          <span className={styles.rowText}>{scene.path}</span>
        </button>
      ))}
    </div>
  );
}

function RunSources() {
  const { replays, selected } = useWorkbench(useShallow(state => ({
    replays: state.sources?.replays ?? [], selected: state.inputSource,
  })));
  return (
    <div className={styles.list} role="listbox" aria-label="运行来源">
      {replays.map(replay => (
        <button key={replay.id} className={styles.row} role="option"
          aria-selected={selected?.kind === 'recorded' && replay.id === selected.id}
          onClick={() => contentActions.selectInputSource({ kind: 'recorded', id: replay.id })}>
          <span className={styles.rowText}>{replay.displayName}</span>
          <span className={styles.rowValue}>录像</span>
        </button>
      ))}
    </div>
  );
}

function InputDeclarations() {
  const { catalog, scenePath } = useWorkbench(useShallow(state => ({ catalog: state.inputCatalog, scenePath: state.scenePath })));
  const [failure, setFailure] = useState<number | null>(null);
  return (
    <>
      <Button disabled={!scenePath} tip={scenePath ? undefined : '先选择内容'}
        onClick={() => { setFailure(null); void session.loadInputs().then(setFailure); }}>
        读取输入声明
      </Button>
      {failure !== null && !catalog && <ErrorLine text="输入声明读取失败" entryId={failure} />}
      {catalog && (
        <>
          <Facts fields={[
            { displayName: 'Tick／秒', value: String(catalog.tickRate) },
            { displayName: '本地角色', value: String(catalog.players.length) },
            { displayName: '目标角色', value: catalog.actorTargets.join('、') || '无' },
          ]} />
          {catalog.players.map(player => (
            <Fold key={player.actorId} title={player.actorId} kind="var(--track-in)" defaultOpen>
              <Facts fields={[
                ...player.values.map(value => ({ displayName: value.inputId, value: `${value.kind} · ${value.encoding}` })),
                ...player.requests.map(request => ({
                  displayName: request.inputId,
                  value: `请求 · 缓冲 ${request.bufferTicks} Tick · 优先级 ${request.priority}`,
                })),
              ]} />
            </Fold>
          ))}
        </>
      )}
    </>
  );
}
