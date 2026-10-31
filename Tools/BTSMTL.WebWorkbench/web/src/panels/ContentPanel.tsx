import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { Sources } from '../api/types';
import { ErrorLine } from '../ui/ErrorLine';
import { session } from '../state/session';
import { contentActions, contentSelectionKey, previewContentChoices, selectedScene, useWorkbench } from '../state/store';
import { authoringActions, useAuthoring } from '../state/authoring';
import { openPanel } from '../shell/layout';
import { BarSeparator, Empty, Facts, Fold, PanelBar } from '../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../ui/controls';
import styles from './panel.module.css';

// 内容面板：场景与录像列表、输入声明；选择结果写入 content 分片，工具栏新建运行使用。
export function ContentPanel() {
  const { sources, hostAvailable, contentKind } = useWorkbench(useShallow(state => ({
    sources: state.sources, hostAvailable: state.hostAvailable, contentKind: state.contentSelection?.kind,
  })));
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
            <Fold title="内容" defaultOpen><Contents sources={sources} /></Fold>
            {contentKind !== 'timeline' && <>
              <Fold title="场景方案" defaultOpen><ScenarioFolders /></Fold>
              <Fold title="运行来源" defaultOpen><RunSources /></Fold>
              <Fold title="输入声明"><InputDeclarations /></Fold>
            </>}
          </>
        )}
      </div>
    </div>
  );
}

function Contents({ sources }: { sources: Sources }) {
  const selection = useWorkbench(state => state.contentSelection);
  const choices = previewContentChoices(sources);
  if (!choices.length) return <Empty text="宿主尚未提供正式内容" />;
  return (
    <div className={styles.list} role="listbox" aria-label="内容">
      {choices.map(choice => (
        <button key={choice.value} className={styles.row} role="option" aria-selected={choice.value === contentSelectionKey(selection)}
          onClick={() => contentActions.selectContent(choice.selection)}>
          <span className={styles.rowText}>{choice.label}</span>
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
  const [creating, setCreating] = useState(false);
  const scenePath = useWorkbench(state => selectedScene(state)?.path ?? null);
  useEffect(() => { if (scenarios === null) void authoringActions.listSkills(); }, [scenarios]);
  if (scenarios === null) return <Empty text="正在读取场景方案" />;
  return <>
    <Button onClick={() => setCreating(!creating)}>新建场景方案</Button>
    {creating && <NewScenario key={scenePath} onCreated={() => setCreating(false)} />}
    <div className={styles.list} role="listbox" aria-label="场景方案">
    {scenarios.map(name => <button key={name} className={styles.row} role="option"
      aria-selected={target?.kind === 'scenario' && target.name === name}
      onClick={() => void authoringActions.open({ kind: 'scenario', name })}>
      <span className={styles.rowText}>{name}</span>
    </button>)}
    </div>
  </>;
}

function NewScenario({ onCreated }: { onCreated(): void }) {
  const { scene, catalog, failure } = useWorkbench(useShallow(state => ({
    scene: selectedScene(state), catalog: state.inputCatalog,
    failure: state.inputCatalogFailure,
  })));
  const [name, setName] = useState('');
  const [actorId, setActorId] = useState<string | null>(null);
  const [duration, setDuration] = useState<number | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  if (!scene) return <Empty text="先选择场景" />;
  if (!catalog || catalog.revision !== scene.revision) return <Empty text={failure !== null ? '场景角色读取失败，见输入声明' : '正在读取场景角色'} />;
  const selectedActor = catalog.players.find(player => player.actorId === actorId);
  return <div className={styles.list}>
    <TextInput aria-label="场景方案名称" placeholder="方案名称" value={name} onChange={event => setName(event.currentTarget.value)} />
    <Select label="输入角色" placeholder="选择角色" value={actorId}
      options={catalog.players.map(player => ({ value: player.actorId, label: player.actorId }))} onChange={setActorId} />
    <NumberInput label="方案时长（秒）" value={duration} min={0} onChange={setDuration} />
    <Button disabled={pending || !name.trim() || !selectedActor || duration === null || duration <= 0}
      onClick={() => {
        setPending(true);
        setError(null);
        void authoringActions.createScenario(name.trim(), { scene: scene.path, sceneRevision: scene.revision,
          actorId: selectedActor!.actorId, schemeName: name.trim(), rangeEndSeconds: duration!,
        }).then(() => { onCreated(); openPanel('timeline'); }, (reason: unknown) => {
          setError(reason instanceof Error ? reason.message : String(reason));
        }).finally(() => setPending(false));
      }}>创建</Button>
    {error && <div role="alert">{error}</div>}
  </div>;
}

function LocalInputChoices() {
  const { scene, catalog, choices, lastSdkFrame, error } = useWorkbench(useShallow(state => ({
    scene: selectedScene(state), catalog: state.inputCatalog,
    choices: state.inputChoices, lastSdkFrame: state.localLastSdkFrame, error: state.inputCatalogFailure,
  })));
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
              `inputs/${plan.id}.json`, choice.schemeIndex, choice.actorId).then(() => openPanel('timeline'));
          }}>编辑所选方案</Button>
      </div>;
    })}
    <NumberInput label="结束 SDK 帧" value={lastSdkFrame} min={1} step={1} onChange={contentActions.setLocalLastSdkFrame} />
  </>;
}

// 输入声明在选定场景后自动读取；这里显示结果，失败后可重新读取。
function InputDeclarations() {
  const { catalog, scene, reading, failure } = useWorkbench(useShallow(state => ({
    catalog: state.inputCatalog, scene: selectedScene(state), reading: state.inputCatalogReading, failure: state.inputCatalogFailure,
  })));
  return (
    <>
      {!scene && <Empty text="先选择内容" />}
      {reading && <Empty text="正在读取输入声明" />}
      {failure !== null && !catalog && <>
        <ErrorLine text="输入声明读取失败" entryId={failure} />
        <Button onClick={() => void session.loadInputs()}>重新读取</Button>
      </>}
      {catalog && (
        <>
          <Facts fields={[
            { displayName: 'Tick／秒', value: String(catalog.tickRate) },
            { displayName: '本地角色', value: String(catalog.players.length) },
            { displayName: '目标角色', value: catalog.actorTargets.join('、') || '无' },
          ]} />
          {catalog.players.map(player => (
            <Fold key={player.actorId} title={player.actorId} defaultOpen>
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
