import { getState } from '../state/store';
import { transport } from '../state/transport';

// 运行快捷键：绑定到每个有面板的窗口（主窗口与拆出窗口），同一窗口只绑一次。
const bound = new Map<Window, { count: number; dispose(): void }>();

function editable(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false;
  return !!target.closest('input, textarea, select, [contenteditable="true"], [role="combobox"], [role="listbox"], [role="menu"]');
}

function onKeyDown(event: KeyboardEvent) {
  if (event.defaultPrevented || event.ctrlKey || event.altKey || event.metaKey) return;
  if (getState().capturing || editable(event.target)) return;
  switch (event.key) {
    case ' ': transport.toggle(); break;
    case 'ArrowLeft': transport.step(-1); break;
    case 'ArrowRight': transport.step(1); break;
    case 'Home': transport.home(); break;
    case 'End': transport.end(); break;
    default: return;
  }
  event.preventDefault();
}

export function bindShortcuts(target: Window): () => void {
  const existing = bound.get(target);
  if (existing) existing.count++;
  else {
    target.addEventListener('keydown', onKeyDown);
    bound.set(target, { count: 1, dispose: () => target.removeEventListener('keydown', onKeyDown) });
  }
  return () => {
    const entry = bound.get(target);
    if (!entry || --entry.count > 0) return;
    entry.dispose();
    bound.delete(target);
  };
}
