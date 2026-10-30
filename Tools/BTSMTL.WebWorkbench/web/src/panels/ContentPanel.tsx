import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { ErrorLine } from '../ui/ErrorLine';
import { session } from '../state/session';
import { contentActions, useWorkbench } from '../state/store';
import { authoringActions, useAuthoring } from '../state/authoring';
import { openPanel } from '../shell/layout';
import { BarSeparator, Empty, Facts, Fold, PanelBar } from '../ui/blocks';
import { Button, NumberInput, Select } from '../ui/controls';
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
            <Fold title="场景方案" kind="var(--track-in)" defaultOpen><ScenarioFolders /></Fold>
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
    <>
    <div className={styles.list} role="listbox" aria-label="运行来源">
      <button className={styles.row} role="option" aria-selected={selected?.kind === 'local'}
        onClick={() => contentActions.selectInputSource({ kind: 'local' })}>
        <span className={styles.rowText}>本地输入方案</span>
      </button>
      {replays.map(replay => (
        <button key={replay.id} className={styles.row} role="option"
          aria-selected={selected?.kind === 'recorded' && replay.id === selected.id}
          onClick={() => contentActions.selectInputSource({ kind: 'recorded', id: replay.id })}>
          <span className={styles.rowText}>{replay.displayName}</span>
          <span className={styles.rowValue}>录像</span>
        </button>
      ))}
    </div>
    {selected?.kind === 'local' && <LocalInputChoices />}
    </>
  );
}

function ScenarioFolders() {
  const { scenarios, target } = useAuthoring(useShallow(state => ({ scenarios: state.scenarios, target: state.target })));
  useEffect(() => { if (scenarios === null) void authoringActions.listSkills(); }, [scenarios]);
  if (scenarios === null) return <Empty text="正在读取场景方案" />;
  if (!scenarios.length) return <Empty text="没有场景方案" />;
  return <div className={styles.list} role="listbox" aria-label="场景方案">
    {scenarios.map(name => <button key={name} className={styles.row} role="option"
      aria-selected={target?.kind === 'scenario' && target.name === name}
      onClick={() => void authoringActions.open({ kind: 'scenario', name })}>
      <span className={styles.rowText}>{name}</span>
    </button>)}
  </div>;
}

function LocalInputChoices() {
  const { scene, catalog, choices, lastSdkFrame } = useWorkbench(useShallow(state => ({
    scene: state.sources?.scenes.find(item => item.path === state.scenePath), catalog: state.inputCatalog,
    choices: state.inputChoices, lastSdkFrame: state.localLastSdkFrame,
  })));
  const [error, setError] = useState<number | null>(null);
  useEffect(() => {
    if (!scene) return;
    const controller = new AbortController();
    setError(null);
    void session.loadInputs(controller.signal).then(entryId => { if (!controller.signal.aborted) setError(entryId); });
    return () => controller.abort();
  }, [scene?.path, scene?.revision]);
  if (!scene) return <Empty text="先选择场景" />;
  if (error !== null) return <ErrorLine text="输入方案读取失败" entryId={error} />;
  if (!catalog) return <Empty text="正在读取角色和输入方案" />;
  return <>
    {catalog.players.map(player => {
      const selected = choices.find(choice => choice.actorId === player.actorId);
      const options = player.localPlans.flatMap(plan => plan.schemes.map(scheme => ({
        value: JSON.stringify([plan.path, scheme.index]), label: `${plan.name} / ${scheme.name}`,
        choice: { actorId: player.actorId, inputPlanRoot: plan.path, inputPlanRevision: plan.revision, schemeIndex: scheme.index },
        plan,
      })));
      const current = selected ? options.find(option => option.choice.inputPlanRoot === selected.inputPlanRoot &&
        option.choice.schemeIndex === selected.schemeIndex) : null;
      return <div key={player.actorId} className={styles.list}>
        <Select label={`${player.actorId} 输入方案`} placeholder={`${player.actorId} 输入方案`}
          value={current?.value ?? null} options={options} disabled={!options.length}
          onChange={value => contentActions.selectInputPlan(options.find(option => option.value === value)!.choice)} />
        <Button disabled={!current || current.plan.kind === 'timeline'}
          onClick={() => {
            const { plan, choice } = current!;
            void authoringActions.openInput({ kind: plan.kind === 'scene' ? 'scenario' : 'skill', name: plan.name },
              `inputs/${plan.id}.json`, choice.schemeIndex).then(() => openPanel('timeline'));
          }}>编辑所选方案</Button>
      </div>;
    })}
    <NumberInput label="结束 SDK 帧" value={lastSdkFrame} min={1} step={1} onChange={contentActions.setLocalLastSdkFrame} />
  </>;
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
