import { useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringTarget } from '../../api/authoring';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { session } from '../../state/session';
import { contentActions, selectedScene, useWorkbench } from '../../state/store';
import { openPanel } from '../../shell/layout';
import { Empty, Facts, Fold } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { InputPlanCreationForm } from '../project/InputPlanCreationForm';
import styles from '../panel.module.css';

// 场景检查器正文：当前所选场景的运行设置（输入来源、输入方案、新建运行）、场景方案与输入声明。
// 场景由 Project 场景页或顶栏选择，内容选择是唯一来源。
export function SceneDetails() {
  const kind = useWorkbench(state => state.contentSelection?.kind);
  const startBlocked = useWorkbench(() => session.canStart());
  return <>
    <Fold title="运行" defaultOpen>
      <Button tip={startBlocked} disabled={!!startBlocked} onClick={() => void session.startRun()}>新建运行</Button>
      {startBlocked && <Empty text={startBlocked} />}
      {kind === 'scene' && <RunSources />}
    </Fold>
    {kind === 'scene' && <>
      <Fold title="场景方案" defaultOpen><ScenarioFolders /></Fold>
      <Fold title="输入声明"><InputDeclarations /></Fold>
    </>}
  </>;
}

const local = 'local';

function RunSources() {
  const { replays, selected } = useWorkbench(useShallow(state => ({
    replays: state.sources?.replays ?? [], selected: state.inputSource,
  })));
  // 录像可能有几十条，收进下拉，默认的本地输入方案排第一。
  return <>
    <Select label="输入来源" value={selected?.kind === 'recorded' ? selected.id : selected?.kind === 'local' ? local : null}
      options={[{ value: local, label: '本地输入方案' }, ...replays.map(replay => ({ value: replay.id, label: `录像 · ${replay.displayName}` }))]}
      onChange={value => contentActions.selectInputSource(value === local ? { kind: 'local' } : { kind: 'recorded', id: value })} />
    {selected?.kind === 'local' && <LocalInputChoices />}
  </>;
}

function ScenarioFolders() {
  const { directory, target, editable } = useAuthoring(useShallow(state => ({ directory: state.directory, target: state.target,
    editable: state.author && state.target?.kind === 'scenario' && !authoringDocument(state).loading })));
  const [creating, setCreating] = useState(false);
  const [creatingInput, setCreatingInput] = useState(false);
  const scenePath = useWorkbench(state => selectedScene(state)?.path ?? null);
  if (directory === null) return <Empty text="正在读取场景方案" />;
  const scenarios = directory.filter(document => document.kind === 'scenario');
  return <>
    <Button onClick={() => setCreating(!creating)}>新建场景方案</Button>
    {creating && <NewScenario key={scenePath} onCreated={() => setCreating(false)} />}
    {editable && <Button onClick={() => setCreatingInput(!creatingInput)}>新建输入方案</Button>}
    {creatingInput && editable && <InputPlanCreationForm key={target!.documentPath} onCancel={() => setCreatingInput(false)}
      onCreated={() => { setCreatingInput(false); openPanel('timeline'); }} />}
    <div className={styles.list} role="listbox" aria-label="场景方案">
      {scenarios.map(document => (
        <button key={document.documentPath} className={styles.row} role="option"
          aria-selected={target?.kind === 'scenario' && target.documentPath === document.documentPath}
          onClick={() => void authoringActions.open({ kind: 'scenario', documentPath: document.documentPath })}>
          <span className={styles.rowText}>{document.documentPath.slice('Scenarios/'.length)}</span>
        </button>
      ))}
    </div>
  </>;
}

function NewScenario({ onCreated }: { onCreated(): void }) {
  const { scene, catalog, failure } = useWorkbench(useShallow(state => ({
    scene: selectedScene(state), catalog: state.inputCatalog, failure: state.inputCatalogFailure,
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
  const { scene, catalog, choices, limit, limitValue, error } = useWorkbench(useShallow(state => ({
    scene: selectedScene(state), catalog: state.inputCatalog, limit: state.host?.inputLimit,
    choices: state.inputChoices, limitValue: state.localInputLimit, error: state.inputCatalogFailure,
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
            void authoringActions.openInput(authoringTarget(plan.kind === 'scene' ? 'scenario' : 'skill', plan.name),
              `inputs/${plan.id}.json`, choice.schemeIndex, choice.actorId).then(() => openPanel('timeline'));
          }}>编辑所选方案</Button>
      </div>;
    })}
    {limit && <NumberInput label={limit.displayName} value={limitValue}
      min={Number(limit.minimum)} step={Number(limit.step)} onChange={contentActions.setLocalInputLimit} />}
  </>;
}

// 输入声明在选定场景后自动读取；这里显示结果，失败后可重新读取。
function InputDeclarations() {
  const { catalog, reading, failure } = useWorkbench(useShallow(state => ({
    catalog: state.inputCatalog, reading: state.inputCatalogReading, failure: state.inputCatalogFailure,
  })));
  return <>
    {reading && <Empty text="正在读取输入声明" />}
    {failure !== null && !catalog && <>
      <ErrorLine text="输入声明读取失败" entryId={failure} />
      <Button onClick={() => void session.loadInputs()}>重新读取</Button>
    </>}
    {catalog && <>
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
    </>}
  </>;
}
