import { useState } from 'react';
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

export type SkillContentKind = 'skill' | 'timeline' | 'macro' | 'input';

// Project 新建技能及当前技能内的 Timeline、共享子图、输入方案；完成后打开对应编辑器。
export function SkillContentCreation({ kind, skill, elements, onDone }: {
  kind: SkillContentKind; skill: string | null; elements: ReturnType<typeof useSkillElements>; onDone(): void;
}) {
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState('');
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  if (kind === 'skill') return <SkillCreationForm onCancel={onDone} onCreated={() => { onDone(); openPanel('inspector'); }} />;
  if (kind === 'input') return <InputPlanCreationForm key={skill} onCancel={onDone} onCreated={() => { onDone(); openPanel('timeline'); }} />;
  return <div className={styles.facts}>
    <strong>{kind === 'timeline' ? `在 ${skill} 中新建 Timeline` : `在 ${skill} 中新建共享子图`}</strong>
    {failure !== null && <ErrorLine {...failure} />}
    <TextInput autoFocus aria-label="新内容名称" placeholder={kind === 'timeline' ? 'Timeline 名称' : '共享子图名称'} value={name} disabled={busy}
      onChange={event => setName(event.currentTarget.value)} />
    <PanelBar><Button disabled={skill === null || elements === null || !name.trim() || busy} onClick={() => {
      void (async () => {
        setBusy(true);
        setFailure(null);
        try {
          if (kind === 'timeline') {
            const id = authoringActions.createTimeline(elements!.timeline, name);
            selectionActions.showTimeline(id, null, null);
            openPanel('timeline');
          } else {
            const id = await graphAuthoring.createPage('Subgraph', 'Shared', name);
            await projectActions.openGraph({ owner: skill!, ownerName: skill!, id });
          }
          onDone();
        } catch (error) { if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error),
          entryId: consoleActions.error('Project', error) }); }
        finally { setBusy(false); }
      })();
    }}>{busy ? '正在创建' : '创建'}</Button><Button variant="ghost" disabled={busy} onClick={onDone}>取消</Button></PanelBar>
  </div>;
}
