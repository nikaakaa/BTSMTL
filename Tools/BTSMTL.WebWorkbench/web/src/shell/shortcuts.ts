import { authoringActions, useAuthoring } from '../state/authoring';
import { getState } from '../state/store';
import { transport } from '../state/transport';

// 运行快捷键：绑定到每个有面板的窗口（主窗口与拆出窗口），同一窗口只绑一次。
const bound = new Map<Window, { count: number; dispose(): void }>();

function editable(target: EventTarget | null): boolean {
  // 拆出窗口的元素属于另一个窗口的 Element，不能用 instanceof 判断。
  if (!target || (target as Node).nodeType !== Node.ELEMENT_NODE) return false;
  return !!(target as Element).closest('input, textarea, select, [contenteditable="true"], [role="combobox"], [role="listbox"], [role="menu"]');
}

function onKeyDown(event: KeyboardEvent) {
  if (event.defaultPrevented || event.altKey) return;
  // 作者模式：Ctrl+Z 撤销，Ctrl+Y／Ctrl+Shift+Z 重做；输入框内保留文本自身的撤销。
  if (event.ctrlKey || event.metaKey) {
    const key = event.key.toLowerCase();
    if (!useAuthoring.getState().author || editable(event.target) || (key !== 'z' && key !== 'y')) return;
    if (key === 'y' || event.shiftKey) authoringActions.redo(); else authoringActions.undo();
    event.preventDefault();
    return;
  }
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
