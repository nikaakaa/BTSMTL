import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type DecisionActor } from '../../api/authoring';
import { ApiError, isAbort } from '../../api/http';
import { authoringActions, selectedPreview, useAuthoring } from '../../state/authoring';
import { useWorkbench } from '../../state/store';
import { Empty, Facts, Fold } from '../../ui/blocks';
import { Button, NumberInput, Select } from '../../ui/controls';
import styles from './inspector.module.css';

type SceneActors = { kind: 'ready'; scene: string; actors: DecisionActor[] } | { kind: 'unavailable'; reason: string };

// 常驻入口图（决策图）由所选 Player 场景里的非玩家角色执行：列出当前绑定，选角色、间隔与相位后应用到预览。
export function DecisionBindingFold({ graphId }: { graphId: string }) {
  const liveId = useWorkbench(state => state.liveId);
  const { editable, saving, lastSave } = useAuthoring(useShallow(state => {
    const document = state.target === null ? undefined : state.documents[state.target.documentPath];
    return { editable: state.author, saving: document?.saving ?? false, lastSave: document?.lastSave ?? null };
  }));
  const [scene, setScene] = useState<SceneActors | null>(null);
  const [actorId, setActorId] = useState<string | null>(null);
  const [intervalTicks, setIntervalTicks] = useState(60);
  const [phaseTicks, setPhaseTicks] = useState(0);

  // Player 切换或保存回执变化（采用新场景包）后重读场景角色。
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      const selection = await selectedPreview();
      if (controller.signal.aborted) return;
      if (selection.kind === 'unavailable') {
        setScene({ kind: 'unavailable', reason: selection.reason });
        return;
      }
      try {
        const actors = await authoringApi.decisionActors(selection.scene.path, selection.scene.revision, controller.signal);
        setScene({ kind: 'ready', scene: selection.scene.path, actors });
      } catch (error) {
        if (isAbort(error)) return;
        setScene({ kind: 'unavailable', reason: error instanceof ApiError ? error.reason : String(error) });
      }
    })();
    return () => controller.abort();
  }, [liveId, lastSave, graphId]);

  if (scene === null) return <Fold title="决策执行" defaultOpen><Empty text="正在读取所选 Player 的场景角色" /></Fold>;
  if (scene.kind === 'unavailable') return <Fold title="决策执行" defaultOpen><Empty text={scene.reason} /></Fold>;
  const bound = scene.actors.filter(actor => actor.decision?.graphId === graphId);
  const candidates = scene.actors.filter(actor => !actor.localPlayer && actor.hasInput);
  const chosen = actorId !== null && candidates.some(actor => actor.actorId === actorId) ? actorId : null;
  const valid = chosen !== null && intervalTicks > 0 && phaseTicks >= 0 && phaseTicks < intervalTicks;
  return <Fold title="决策执行" defaultOpen>
    <Facts fields={bound.length === 0 ? [{ displayName: '当前执行角色', value: '未绑定' }]
      : bound.map(actor => ({ displayName: actor.actorId, value: `每 ${actor.decision!.intervalTicks} Tick，相位 ${actor.decision!.phaseTicks}` }))} />
    {candidates.length === 0 ? <Empty text="所选场景没有可由决策图控制的非玩家角色" /> : editable && <>
      <div className={styles.field}>
        <label>执行角色</label>
        <Select label="执行角色" placeholder="选择非玩家角色" value={chosen} disabled={saving}
          options={candidates.map(actor => ({ value: actor.actorId, label: actor.decision === null || actor.decision.graphId === graphId
            ? actor.actorId : `${actor.actorId}（现为 ${actor.decision.graphId}）` }))}
          onChange={value => {
            setActorId(value);
            const current = candidates.find(actor => actor.actorId === value)?.decision;
            if (current?.graphId === graphId) {
              setIntervalTicks(current.intervalTicks);
              setPhaseTicks(current.phaseTicks);
            }
          }} />
      </div>
      <div className={styles.field}>
        <label>间隔（Tick）</label>
        <NumberInput wide label="间隔" value={intervalTicks} min={1} step={1} disabled={saving}
          onChange={next => { if (next !== null) setIntervalTicks(Math.round(next)); }} />
      </div>
      <div className={styles.field}>
        <label>相位（Tick）</label>
        <NumberInput wide label="相位" value={phaseTicks} min={0} max={intervalTicks - 1} step={1} disabled={saving}
          onChange={next => { if (next !== null) setPhaseTicks(Math.round(next)); }} />
      </div>
      <div className={styles.actions}>
        <Button disabled={!valid || saving} tip="保存并编译本图，写进所选 Player 的场景包并刷新预览"
          onClick={() => { void authoringActions.save('apply', undefined, { actorId: chosen!, intervalTicks, phaseTicks }); }}>
          绑定并应用
        </Button>
      </div>
    </>}
  </Fold>;
}
