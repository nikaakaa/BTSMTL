import { useEffect, useState } from 'react';
import { useReferenceSelection } from '../../state/referenceSelection';
import { usePanelApi } from '../../shell/panel';
import { panelInfo } from '../../shell/panelTypes';
import { PanelBar } from '../../ui/blocks';
import { Button, MenuButton } from '../../ui/controls';
import { ContentProject } from './ContentProject';
import { ProjectFiles } from './ProjectFiles';
import { ProjectScenes } from './ProjectScenes';
import { ResourceProject } from './ResourceProject';
import styles from './project.module.css';

export function ProjectPanel() {
  const panel = usePanelApi();
  const [view, setView] = useState<'content' | 'scenes' | 'resources' | 'files'>('content');
  const field = useReferenceSelection(state => state.field);
  useEffect(() => panel.setTitle(panelInfo.resources.title), [panel]);
  useEffect(() => {
    if (field !== null && (field.domain !== null || field.resourceObjects !== undefined)) setView('resources');
  }, [field?.id, field?.domain, field?.resourceObjects]);
  return <div className={styles.root}>
    <PanelBar><Button variant="ghost" pressed={view === 'content'} onClick={() => setView('content')}>内容</Button>
      <Button variant="ghost" pressed={view === 'scenes'} onClick={() => setView('scenes')}>场景</Button>
      <Button variant="ghost" pressed={view === 'resources'} onClick={() => setView('resources')}>资源</Button>
      <MenuButton label="Project 更多操作" variant="ghost" entries={[
        { label: '文件视图（排查）', onSelect: () => setView('files') },
      ]}>更多</MenuButton>{view === 'files' && <span className={styles.note}>文件排查</span>}
    </PanelBar>
    <div className={styles.body}>{view === 'content' ? <ContentProject /> : view === 'scenes' ? <ProjectScenes /> : view === 'resources' ? <ResourceProject /> : <ProjectFiles />}</div>
  </div>;
}
