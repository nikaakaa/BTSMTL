import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './shell/theme.css';
import { applyStateAddress } from './shell/address';
import { Workbench } from './shell/Workbench';
import { session } from './state/session';

const root = document.getElementById('root');
if (!root) throw new Error('缺少 #root');

createRoot(root).render(
  <StrictMode>
    <Workbench />
  </StrictMode>,
);

void session.connect().then(applyStateAddress);
