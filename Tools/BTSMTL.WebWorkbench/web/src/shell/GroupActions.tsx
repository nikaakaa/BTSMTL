import type { IDockviewHeaderActionsProps, IWatermarkPanelProps } from 'dockview-react';
import { useEffect, useState } from 'react';
import { Button, MenuButton, type MenuEntry } from '../ui/controls';
import { PortalContainerProvider } from '../ui/portal';
import { openNewInstance, openPanel, popoutGroup, resetLayout } from './layout';
import { panelInfo, panelTypes } from './panelTypes';
import styles from './shell.module.css';

function panelMenu(): MenuEntry[] {
  return [
    { kind: 'label', label: '打开面板' },
    ...panelTypes.map(type => ({ label: panelInfo[type].title, onSelect: () => openPanel(type) })),
    { kind: 'separator' },
    { kind: 'label', label: '新开一个' },
    ...panelTypes.filter(type => panelInfo[type].multi)
      .map(type => ({ label: panelInfo[type].title, onSelect: () => openNewInstance(type) })),
    { kind: 'separator' },
    { label: '恢复默认布局', onSelect: resetLayout },
  ];
}

// 组头右侧：拆到独立窗口、面板菜单。浮层挂到组所在窗口。
export function GroupActions({ group, api }: IDockviewHeaderActionsProps) {
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
          <Button variant="icon" tip="拆到独立窗口" aria-label="拆到独立窗口" onClick={() => void popoutGroup(group)}>⧉</Button>
        )}
        <MenuButton label="面板菜单" variant="icon" entries={panelMenu()}>⋯</MenuButton>
      </div>
    </PortalContainerProvider>
  );
}

export function Watermark(_: IWatermarkPanelProps) {
  return (
    <div className={styles.watermark}>
      <MenuButton label="打开面板" entries={panelMenu()}>打开面板</MenuButton>
    </div>
  );
}
