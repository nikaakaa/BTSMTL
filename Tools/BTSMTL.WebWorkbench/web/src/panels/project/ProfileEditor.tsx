import { useEffect, useState } from 'react';
import { authoringActions, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions } from '../../state/store';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

export function ProfileSources() {
  const directory = useAuthoring(state => state.directory);
  const unavailableReason = useAuthoring(state => state.unavailableReason);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  useEffect(() => {
    if (directory === null) void authoringActions.listDocuments();
  }, [directory]);
  const profiles = (directory ?? []).filter(document => document.kind === 'profile');
  const profile = profiles.find(document => document.documentPath === selected);
  return <Fold title="独立 Profile 编辑" defaultOpen>
    {failure && <ErrorLine {...failure} />}
    {unavailableReason !== null && <div className={styles.issue}>{unavailableReason}</div>}
    <PanelBar wrap><Select label="Profile 文档" value={profile?.documentPath ?? null} placeholder="选择 Profile 文档"
      options={profiles.map(document => ({ value: document.documentPath, label: document.documentPath }))} onChange={setSelected} />
    </PanelBar>
    <PanelBar><Button disabled={profile === undefined} onClick={() => {
      setFailure(null);
      void projectActions.openDocument(profile!.documentPath).catch((error: unknown) => setFailure({
        text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Profile 文档', error),
      }));
    }}>打开 Profile 文档</Button></PanelBar>
  </Fold>;
}
