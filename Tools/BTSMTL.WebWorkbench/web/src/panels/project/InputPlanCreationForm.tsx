import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { InputPlanContext } from '../../api/authoring';
import { useAuthoring, useAuthorTimelines, useInputSchemes } from '../../state/authoring';
import { inputAuthoring } from '../../state/authoringInputs';
import { consoleActions, selectedScene, useWorkbench } from '../../state/store';
import { Button, Select, TextInput } from '../../ui/controls';
import { PanelBar } from '../../ui/blocks';
import styles from './project.module.css';

export function InputPlanCreationForm({ onCreated, onCancel }: { onCreated(id: string): void; onCancel(): void }) {
  const target = useAuthoring(state => state.target!);
  const timelines = useAuthorTimelines();
  const inputs = useInputSchemes();
  const { scene, catalog } = useWorkbench(useShallow(state => ({ scene: selectedScene(state), catalog: state.inputCatalog })));
  const [kind, setKind] = useState<'skill' | 'timeline' | 'scene'>(target.kind === 'skill' ? 'skill' : target.kind === 'timeline' ? 'timeline' : 'scene');
  const [timeline, setTimeline] = useState<string | null>(null);
  const [actor, setActor] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setActor(null); }, [scene?.path, scene?.revision]);
  const players = scene && catalog?.revision === scene.revision ? catalog.players : [];
  const availableTimelines = timelines.filter(item => !inputs.some(input => input.documentId === item.documentId));
  const context: InputPlanContext | null = kind === 'skill' ? { kind }
    : kind === 'timeline' ? timeline === null ? null : { kind, timeline }
    : scene && players.some(player => player.actorId === actor) ? { kind, scene: scene.path, actor: actor! } : null;
  return <div className={styles.facts}>
    <TextInput autoFocus aria-label="输入方案名称" placeholder="输入方案名称" value={name} disabled={busy}
      onChange={event => setName(event.currentTarget.value)} />
    {target.kind === 'skill' ? <Select label="输入方案归属" value={kind} disabled={busy}
      options={[{ value: 'skill', label: '当前技能' }, { value: 'timeline', label: '指定 Timeline' }]}
      onChange={value => setKind(value as 'skill' | 'timeline')} /> : target.kind === 'timeline' ? <span>归属指定 Timeline</span> : <span>归属当前场景与所选角色</span>}
    {kind === 'timeline' && <Select label="所属 Timeline" placeholder="选择 Timeline" value={timeline} disabled={busy}
      options={availableTimelines.map(item => ({ value: item.documentId, label: item.displayName ?? item.documentId }))}
      onChange={setTimeline} />}
    {kind === 'scene' && <>
      <span>{scene?.path ?? '先在内容面板选择场景'}</span>
      <Select label="目标角色" placeholder="选择场景角色" value={actor} disabled={busy || players.length === 0}
        options={players.map(player => ({ value: player.actorId, label: player.actorId }))} onChange={setActor} />
    </>}
    <PanelBar>
      <Button disabled={busy || !name.trim() || context === null} onClick={() => {
        setBusy(true);
        setError(null);
        void inputAuthoring.createPlan(name.trim(), context!).then(onCreated, (reason: unknown) => {
          consoleActions.error('输入', reason);
          setError(reason instanceof Error ? reason.message : String(reason));
        }).finally(() => setBusy(false));
      }}>{busy ? '正在创建' : '创建输入方案'}</Button>
      <Button variant="ghost" disabled={busy} onClick={onCancel}>取消</Button>
    </PanelBar>
    {error && <div role="alert" className={styles.issue}>{error}</div>}
  </div>;
}
