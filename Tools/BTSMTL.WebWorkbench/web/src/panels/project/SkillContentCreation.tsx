import { useState } from 'react';
import type { WorkspaceCreationKind } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { openPanel } from '../../shell/layout';
import { authoringActions, type useSkillElements } from '../../state/authoring';
import { graphAuthoring } from '../../state/graphAuthoring';
import { projectActions } from '../../state/project';
import { consoleActions, selectionActions } from '../../state/store';
import { PanelBar } from '../../ui/blocks';
import { Button, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { SkillCreationForm } from './SkillCreationForm';
import { InputPlanCreationForm } from './InputPlanCreationForm';
import styles from './project.module.css';

export const workspaceCreations = {
  'standalone-timeline': { kind: 'timeline', title: '独立 Timeline' },
  'standalone-macro': { kind: 'macro', title: '独立共享子图' },
  'camera-curve': { kind: 'camera-curve', title: '相机曲线' },
  'camera-shake': { kind: 'camera-shake', title: '相机震动 Profile' },
} as const satisfies Record<string, { kind: WorkspaceCreationKind; title: string }>;

export type SkillContentKind = 'skill' | 'timeline' | 'macro' | 'input' | keyof typeof workspaceCreations;

// Project 新建技能及当前技能内的 Timeline、共享子图、输入方案；完成后打开对应编辑器。
export function SkillContentCreation({ kind, skill, elements, onDone }: {
  kind: SkillContentKind; skill: string | null; elements: ReturnType<typeof useSkillElements>; onDone(): void;
}) {
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState('');
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  if (kind === 'skill') return <SkillCreationForm onCancel={onDone} onCreated={() => { onDone(); openPanel('inspector'); }} />;
  if (kind === 'input') return <InputPlanCreationForm key={skill} onCancel={onDone} onCreated={() => { onDone(); openPanel('timeline'); }} />;
  const workspace = kind === 'timeline' || kind === 'macro' ? null : workspaceCreations[kind];
  const title = name.trim();
  const ready = title.length > 0 && !busy && (workspace !== null || (skill !== null && elements !== null));
  const submit = async () => {
    if (!ready) return;
    setBusy(true);
    setFailure(null);
    try {
      if (workspace !== null) {
        const target = await authoringActions.createWorkspaceDocument(workspace.kind, title);
        await projectActions.openDocument(target.documentPath);
      } else if (kind === 'timeline') {
        const id = authoringActions.createTimeline(elements!.timeline, title);
        selectionActions.showTimeline(id, null, null);
        openPanel('timeline');
      } else {
        const id = await graphAuthoring.createPage('Subgraph', 'Shared', title);
        await projectActions.openGraph({ owner: skill!, ownerName: skill!, id });
      }
      onDone();
    } catch (error) {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) });
    } finally { setBusy(false); }
  };
  return <form className={styles.facts} onSubmit={event => { event.preventDefault(); void submit(); }}>
    <strong>{workspace !== null ? `新建${workspace.title}` : kind === 'timeline' ? `在 ${skill} 中新建 Timeline` : `在 ${skill} 中新建共享子图`}</strong>
    {failure !== null && <ErrorLine {...failure} />}
    <TextInput autoFocus aria-label="新内容名称" placeholder="输入名称" value={name} disabled={busy}
      onChange={event => setName(event.currentTarget.value)} />
    <PanelBar><Button type="submit" disabled={!ready}>{busy ? '正在创建' : '创建'}</Button>
      <Button type="button" variant="ghost" disabled={busy} onClick={onDone}>取消</Button></PanelBar>
  </form>;
}
