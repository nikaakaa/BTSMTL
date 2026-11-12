import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './shell/theme.css';
import { applyStateAddress } from './shell/address';
import { guardNativeBehavior } from './shell/nativeBehavior';
import { Workbench } from './shell/Workbench';
import { session } from './state/session';
import { startInputAutoSave } from './state/inputAutoSave';

const root = document.getElementById('root');
if (!root) throw new Error('缺少 #root');
guardNativeBehavior(window);

createRoot(root).render(
  <StrictMode>
    <Workbench />
  </StrictMode>,
);

startInputAutoSave();
void session.connect().then(applyStateAddress);
