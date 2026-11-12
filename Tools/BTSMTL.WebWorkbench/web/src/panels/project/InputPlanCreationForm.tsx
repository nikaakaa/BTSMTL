import { useEffect, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { InputPlanContext } from '../../api/authoring';
import { authoringDocument, useAuthoring, useAuthorTimelines, useInputSchemes } from '../../state/authoring';
import { inputAuthoring } from '../../state/authoringInputs';
import { consoleActions, contentSelectionKey, previewContentChoices, selectedScene, useWorkbench } from '../../state/store';
import { Button, Select, TextInput } from '../../ui/controls';
import { PanelBar } from '../../ui/blocks';
import styles from './project.module.css';

export function InputPlanCreationForm({ onCreated, onCancel }: { onCreated(id: string): void; onCancel(): void }) {
  const target = useAuthoring(state => state.target!);
  const documentRoot = useAuthoring(state => authoringDocument(state).roles.entryGraph !== undefined || state.target?.kind === 'graph');
  const timelines = useAuthorTimelines();
  const inputs = useInputSchemes();
  const { scene, catalog, sceneName } = useWorkbench(useShallow(state => ({ scene: selectedScene(state), catalog: state.inputCatalog,
    sceneName: state.sources && previewContentChoices(state.sources).find(choice => choice.value === contentSelectionKey(state.contentSelection))?.label })));
  // 技能与图文档只有一份预览输入，所有入口共用；要比较不同起点或目标，在它里面新增方案。
  const kind = documentRoot ? 'document' : target.kind === 'timeline' ? 'timeline' : 'scene';
  const [timeline, setTimeline] = useState<string | null>(null);
  const [actor, setActor] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setActor(null); }, [scene?.path, scene?.revision]);
  const players = scene && catalog?.revision === scene.revision ? catalog.players : [];
  const availableTimelines = timelines.filter(item => !inputs.some(input => input.documentId === item.documentId));
  const context: InputPlanContext | null = kind === 'document' ? { kind }
    : kind === 'timeline' ? timeline === null ? null : { kind, timeline }
    : scene && players.some(player => player.actorId === actor) ? { kind, scene: { path: scene.path, revision: scene.revision }, actor: actor! } : null;
  if (documentRoot && inputs.some(input => input.owner !== '')) return <div className={styles.facts}>
    <span>这个文档已有预览输入，所有入口共用；在检查器的「预览输入」里新增方案，或直接在 Timeline 顶部的输入轨上加按键。</span>
    <PanelBar><Button variant="ghost" onClick={onCancel}>关闭</Button></PanelBar>
  </div>;
  return <div className={styles.facts}>
    <TextInput autoFocus aria-label="输入方案名称" placeholder="输入方案名称" value={name} disabled={busy}
      onChange={event => setName(event.currentTarget.value)} />
    <span>{documentRoot ? '归属当前文档，所有入口共用' : target.kind === 'timeline' ? '归属指定 Timeline' : '归属当前场景与所选角色'}</span>
    {kind === 'timeline' && <Select label="所属 Timeline" placeholder="选择 Timeline" value={timeline} disabled={busy}
      options={availableTimelines.map(item => ({ value: item.documentId, label: item.displayName ?? item.documentId }))}
      onChange={setTimeline} />}
    {kind === 'scene' && <>
      <span>{sceneName ?? '先在 Project 场景页选择场景'}</span>
      <Select label="目标角色" placeholder="选择场景角色" value={actor} disabled={busy || players.length === 0}
        options={players.map(player => ({ value: player.actorId, label: player.actorId }))} onChange={setActor} />
    </>}
    <PanelBar>
      <Button disabled={busy || !name.trim() || context === null} onClick={() => {
        setBusy(true);
        setError(null);
        void inputAuthoring.createPlan(name.trim(), context!).then(created => onCreated(created.id), (reason: unknown) => {
          consoleActions.error('输入', reason);
          setError(reason instanceof Error ? reason.message : String(reason));
        }).finally(() => setBusy(false));
      }}>{busy ? '正在创建' : '创建输入方案'}</Button>
      <Button variant="ghost" disabled={busy} onClick={onCancel}>取消</Button>
    </PanelBar>
    {error && <div role="alert" className={styles.issue}>{error}</div>}
  </div>;
}
