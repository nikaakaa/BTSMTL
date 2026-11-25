import type { IDockviewHeaderActionsProps, IWatermarkPanelProps } from 'dockview-react';
import { useEffect, useState } from 'react';
import { useAuthoring } from '../state/authoring';
import { useWorkbench } from '../state/store';
import { Button, MenuButton, type MenuEntry } from '../ui/controls';
import { OptionsIcon, PopoutIcon } from '../ui/icons';
import { PortalContainerProvider } from '../ui/portal';
import { openNewInstance, openPanel, popoutGroup, resetLayout } from './layout';
import { panelInfo, panelTypes } from './panelTypes';
import styles from './shell.module.css';

export function usePanelMenu(): MenuEntry[] {
  const playerAvailable = useWorkbench(state => state.host?.player.available === true);
  const projectTools = useAuthoring(state => state.workspaceFeatures?.projectTools === true);
  const available = panelTypes.filter(type => (type !== 'player' || playerAvailable) && (type !== 'export' || projectTools));
  return [
    { kind: 'label', label: '打开面板' },
    ...available.map(type => ({ label: panelInfo[type].title, onSelect: () => openPanel(type) })),
    { kind: 'separator' },
    { kind: 'label', label: '新开一个' },
    ...available.filter(type => panelInfo[type].multi)
      .map(type => ({ label: panelInfo[type].title, onSelect: () => openNewInstance(type) })),
    { kind: 'separator' },
    { label: '恢复默认布局', onSelect: resetLayout },
  ];
}

// 组头右侧：拆到独立窗口、面板菜单。浮层挂到组所在窗口。
export function GroupActions({ group, api }: IDockviewHeaderActionsProps) {
  const menu = usePanelMenu();
  const [location, setLocation] = useState(() => api.location.type);
  const [groupWindow, setGroupWindow] = useState(() => api.getWindow());
  useEffect(() => {
    const subscription = api.onDidLocationChange(event => {
      setLocation(event.location.type);
      setGroupWindow(api.getWindow());
    });
    return () => subscription.dispose();
  }, [api]);

  return (
    <PortalContainerProvider value={groupWindow.document.body}>
      <div className={styles.groupActions}>
        {location !== 'popout' && (
          <Button variant="ghost" className={styles.groupButton} tip="拆到独立窗口" aria-label="拆到独立窗口"
            onClick={() => void popoutGroup(group)}><PopoutIcon /></Button>
        )}
        <MenuButton label="面板菜单" variant="ghost" className={styles.groupButton} iconOnly entries={menu}><OptionsIcon /></MenuButton>
      </div>
    </PortalContainerProvider>
  );
}

export function Watermark(_: IWatermarkPanelProps) {
  const menu = usePanelMenu();
  return (
    <div className={styles.watermark}>
      <MenuButton label="打开面板" entries={menu}>打开面板</MenuButton>
    </div>
  );
}
