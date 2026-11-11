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
import { DocumentCreationForm } from './DocumentCreationForm';
import { InputPlanCreationForm } from './InputPlanCreationForm';
import styles from './project.module.css';

export type DocumentContentKind = 'document' | 'timeline' | 'macro' | 'input';

export function DocumentCreation({ kind, skill, elements, onDone }: {
  kind: DocumentContentKind; skill: string | null; elements: ReturnType<typeof useSkillElements>; onDone(): void;
}) {
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState('');
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  if (kind === 'document') return <DocumentCreationForm onCancel={onDone} onCreated={() => { onDone(); openPanel('inspector'); }} />;
  if (kind === 'input') return <InputPlanCreationForm key={skill} onCancel={onDone} onCreated={() => { onDone(); openPanel('timeline'); }} />;
  const title = name.trim();
  const ready = title.length > 0 && !busy && skill !== null && elements !== null;
  const submit = async () => {
    if (!ready) return;
    setBusy(true);
    setFailure(null);
    try {
      if (kind === 'timeline') {
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
    <strong>{kind === 'timeline' ? `在 ${skill} 中新建 Timeline` : `在 ${skill} 中新建共享子图`}</strong>
    {failure !== null && <ErrorLine {...failure} />}
    <TextInput autoFocus aria-label="新内容名称" placeholder="输入名称" value={name} disabled={busy}
      onChange={event => setName(event.currentTarget.value)} />
    <PanelBar><Button type="submit" disabled={!ready}>{busy ? '正在处理' : '创建'}</Button>
      <Button type="button" variant="ghost" disabled={busy} onClick={onDone}>取消</Button></PanelBar>
  </form>;
}
