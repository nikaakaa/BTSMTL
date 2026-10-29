import { DockviewReact, type DockviewTheme } from 'dockview-react';
import 'dockview-react/dist/styles/dockview.css';
import { useEffect } from 'react';
import { applyPanelAddress } from './address';
import { panelComponents } from '../panels/registry';
import { TipProvider } from '../ui/controls';
import './dockview.css';
import { GroupActions, Watermark } from './GroupActions';
import { attachLayout } from './layout';
import { bindShortcuts } from './shortcuts';
import styles from './shell.module.css';
import { Toolbar } from './Toolbar';

const theme: DockviewTheme = {
  name: 'btsmtl',
  className: 'dockview-theme-btsmtl',
  colorScheme: 'dark',
  gap: 0,
  dndOverlayMounting: 'absolute',
};

export function Workbench() {
  useEffect(() => bindShortcuts(window), []);
  return (
    <TipProvider>
      <div className={styles.workbench}>
        <Toolbar />
        <DockviewReact
          className={styles.dock}
          theme={theme}
          components={panelComponents}
          rightHeaderActionsComponent={GroupActions}
          watermarkComponent={Watermark}
          popoutUrl="/popout.html"
          onReady={event => { attachLayout(event.api); applyPanelAddress(); }}
        />
      </div>
    </TipProvider>
  );
}
