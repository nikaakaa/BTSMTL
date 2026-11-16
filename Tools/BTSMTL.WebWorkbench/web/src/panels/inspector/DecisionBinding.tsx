import { useEffect, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type ActorStart, type DecisionActor } from '../../api/authoring';
import { ApiError, isAbort } from '../../api/http';
import { authoringActions, selectedPreview, useAuthoring } from '../../state/authoring';
import { useWorkbench } from '../../state/store';
import { Empty, Facts, Fold } from '../../ui/blocks';
import { Button, NumberInput, Select, TextInput } from '../../ui/controls';
import styles from './inspector.module.css';

type SceneActors = { kind: 'ready'; scene: string; actors: DecisionActor[] } | { kind: 'unavailable'; reason: string };
type Mode = 'existing' | 'new';

const describeStart = (start: ActorStart | null) => start === null ? '原型位姿'
  : `(${start.x}, ${start.y}, ${start.z})，偏航 ${start.yaw}°`;

// 常驻入口图（决策图）由所选 Player 场景里的非玩家角色执行：列出当前绑定，选场景已有角色或照模板新增角色，
// 填间隔与相位（新增时再填开局位姿）后应用到预览。
export function DecisionBindingFold({ graphId }: { graphId: string }) {
  const liveId = useWorkbench(state => state.liveId);
  const { editable, saving, lastSave } = useAuthoring(useShallow(state => {
    const document = state.target === null ? undefined : state.documents[state.target.documentPath];
    return { editable: state.author, saving: document?.saving ?? false, lastSave: document?.lastSave ?? null };
  }));
  const [scene, setScene] = useState<SceneActors | null>(null);
  const [mode, setMode] = useState<Mode | null>(null);
  const [actorId, setActorId] = useState<string | null>(null);
  const [templateId, setTemplateId] = useState<string | null>(null);
  const [newActorId, setNewActorId] = useState('');
  const [start, setStart] = useState<ActorStart>({ x: 0, y: 0, z: 0, yaw: 0 });
  const [intervalTicks, setIntervalTicks] = useState(60);
  const [phaseTicks, setPhaseTicks] = useState(0);
  const added = useRef<string | null>(null);

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
        // 新增角色已进场景包：切回已有角色并选中它，便于继续调整间隔与相位。
        if (added.current !== null && actors.some(actor => actor.actorId === added.current)) {
          setMode('existing');
          setActorId(added.current);
          setNewActorId('');
          added.current = null;
        }
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
  const templates = scene.actors.filter(actor => actor.hasInput);
  const current: Mode = mode ?? (candidates.length === 0 ? 'new' : 'existing');
  const chosen = actorId !== null && candidates.some(actor => actor.actorId === actorId) ? actorId : null;
  const template = templates.find(actor => actor.actorId === templateId) ?? null;
  const addedId = newActorId.trim();
  const duplicate = scene.actors.some(actor => actor.actorId === addedId);
  const timing = intervalTicks > 0 && phaseTicks >= 0 && phaseTicks < intervalTicks;
  const valid = timing && (current === 'existing' ? chosen !== null : template !== null && addedId !== '' && !duplicate);
  const field = (key: keyof ActorStart, label: string) => <div className={styles.field}>
    <label>{label}</label>
    <NumberInput wide label={label} value={start[key]} step={0.1} disabled={saving}
      onChange={next => { if (next !== null) setStart(value => ({ ...value, [key]: next })); }} />
  </div>;
  return <Fold title="决策执行" defaultOpen>
    <Facts fields={bound.length === 0 ? [{ displayName: '当前执行角色', value: '未绑定' }]
      : bound.map(actor => ({ displayName: actor.actorId, value: `每 ${actor.decision!.intervalTicks} Tick，相位 ${actor.decision!.phaseTicks}` }))} />
    {!editable ? null : templates.length === 0 ? <Empty text="所选场景没有带输入布局的角色，无法新增或绑定决策执行角色" /> : <>
      <div className={styles.field}>
        <label>执行方式</label>
        <Select<Mode> label="执行方式" value={current} disabled={saving}
          options={[{ value: 'existing', label: candidates.length === 0 ? '场景已有角色（无可用）' : '场景已有角色' },
            { value: 'new', label: '新增角色' }]}
          onChange={setMode} />
      </div>
      {current === 'existing' ? candidates.length === 0
        ? <Empty text="所选场景没有可由决策图控制的非玩家角色，可改为新增角色" />
        : <div className={styles.field}>
          <label>执行角色</label>
          <Select label="执行角色" placeholder="选择非玩家角色" value={chosen} disabled={saving}
            options={candidates.map(actor => ({ value: actor.actorId, label: actor.decision === null || actor.decision.graphId === graphId
              ? actor.actorId : `${actor.actorId}（现为 ${actor.decision.graphId}）` }))}
            onChange={value => {
              setActorId(value);
              const decision = candidates.find(actor => actor.actorId === value)?.decision;
              if (decision?.graphId === graphId) {
                setIntervalTicks(decision.intervalTicks);
                setPhaseTicks(decision.phaseTicks);
              }
            }} />
        </div>
        : <>
          <div className={styles.field}>
            <label>模板角色</label>
            <Select label="模板角色" placeholder="选择复制的角色" value={template?.actorId ?? null} disabled={saving}
              tip="新角色照模板复制原型、编排、输入与表现，不占玩家槽位"
              options={templates.map(actor => ({ value: actor.actorId, label: actor.localPlayer ? `${actor.actorId}（玩家）` : actor.actorId }))}
              onChange={value => {
                setTemplateId(value);
                const source = templates.find(actor => actor.actorId === value)?.start;
                if (source) setStart(source);
              }} />
          </div>
          {template && <Facts fields={[{ displayName: '模板开局位姿', value: describeStart(template.start) }]} />}
          <div className={styles.field}>
            <label>新角色 Id</label>
            <TextInput aria-label="新角色 Id" placeholder="例如 enemy-a" value={newActorId} disabled={saving}
              onChange={event => setNewActorId(event.currentTarget.value)} />
          </div>
          {duplicate && <Empty text={`场景已有角色 ${addedId}`} />}
          {field('x', '出生 X（米）')}
          {field('y', '出生 Y（米）')}
          {field('z', '出生 Z（米）')}
          {field('yaw', '偏航（度）')}
        </>}
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
        <Button disabled={!valid || saving}
          tip={current === 'existing' ? '保存并编译本图，写进所选 Player 的场景包并刷新预览'
            : '照模板把新角色加进所选 Player 的场景包并写开局位姿，再保存编译本图、绑定并刷新预览'}
          onClick={() => {
            void authoringActions.save('apply', undefined, current === 'existing'
              ? { actorId: chosen!, intervalTicks, phaseTicks }
              : { actorId: addedId, intervalTicks, phaseTicks, newActor: { templateActorId: template!.actorId, start } });
            if (current === 'new') added.current = addedId;
          }}>
          {current === 'existing' ? '绑定并应用' : '新增并绑定'}
        </Button>
      </div>
    </>}
  </Fold>;
}
