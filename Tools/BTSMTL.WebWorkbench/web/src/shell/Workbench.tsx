import { DockviewReact, type DockviewTheme } from 'dockview-react';
import 'dockview-react/dist/styles/dockview.css';
import { useEffect, useState } from 'react';
import { applyPanelAddress } from './address';
import { panelComponents } from '../panels/registry';
import { Button, TipProvider } from '../ui/controls';
import { playerService } from '../state/player';
import { quickPreview } from '../state/quickPreview';
import { session } from '../state/session';
import { connectionActions, getState } from '../state/store';
import './dockview.css';
import { AuthoringExit } from './AuthoringExit';
import { GroupActions, Watermark } from './GroupActions';
import { attachLayout } from './layout';
import { PanelTab } from './PanelTab';
import { shellBridge } from './shellBridge';
import { bindShortcuts } from './shortcuts';
import styles from './shell.module.css';
import { Toolbar } from './Toolbar';

const theme: DockviewTheme = {
  name: 'btsmtl',
  className: 'dockview-theme-btsmtl',
  colorScheme: 'dark',
  gap: 2,
  dndOverlayMounting: 'absolute',
};

export function Workbench() {
  useEffect(() => bindShortcuts(window), []);
  return (
    <TipProvider>
      <div className={styles.workbench}>
        <Toolbar />
        <DesktopHostStatus />
        <AuthoringExit />
        <DockviewReact
          className={styles.dock}
          theme={theme}
          components={panelComponents}
          defaultTabComponent={PanelTab}
          rightHeaderActionsComponent={GroupActions}
          watermarkComponent={Watermark}
          popoutUrl="/popout.html"
          onReady={event => { attachLayout(event.api); applyPanelAddress(); }}
        />
      </div>
    </TipProvider>
  );
}

function DesktopHostStatus() {
  const [notice, setNotice] = useState<{ state: 'reconnecting' | 'failed'; message: string } | null>(null);
  useEffect(() => {
    const bridge = shellBridge;
    if (bridge === null) return;
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type !== 'desktop-host') return;
      const { state, message } = event.data;
      if (state === 'ready') {
        setNotice(null);
        playerService.hostRestarted();
        void session.connect().then(() => {
          if (getState().previewMode === 'quick') void quickPreview.readSource();
        });
      } else if (state === 'reconnecting' || state === 'failed') {
        connectionActions.setConnected(false, message);
        setNotice({ state, message });
      }
    };
    bridge.addEventListener('message', onMessage);
    return () => bridge.removeEventListener('message', onMessage);
  }, []);
  return notice && <div className={styles.hostNotice} role="status">
    <span>{notice.message}</span>
    {notice.state === 'failed' && <Button onClick={() => shellBridge?.postMessage({ type: 'desktop-host-retry' })}>重连宿主</Button>}
  </div>;
}
