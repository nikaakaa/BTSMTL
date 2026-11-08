// 工作台桌面壳的原生画面区：页面在壳的 WebView2 里时，后端渲染窗口直接显示在壳摆放的子窗口里；
// 网页只报占位元素的位置（设备像素）与压在上面的浮层，壳据此摆放画面区并挖空浮层处。
import { useEffect, type RefObject } from 'react';
import controlStyles from '../ui/controls.module.css';

interface ShellBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
}

const bridge = (window as Window & { chrome?: { webview?: ShellBridge } }).chrome?.webview ?? null;
export const nativeSurfaceAvailable = bridge !== null;

const handles = new Map<string, number>();
const waiters = new Map<string, ((handle: number) => void)[]>();
bridge?.addEventListener('message', event => {
  const message = event.data as { type?: string; id?: string; handle?: number };
  if (message.type !== 'surface' || message.id === undefined || message.handle === undefined) return;
  handles.set(message.id, message.handle);
  for (const resolve of waiters.get(message.id) ?? []) resolve(message.handle);
  waiters.delete(message.id);
});

export function nativeSurfaceHandle(id: string): Promise<number> {
  const known = handles.get(id);
  if (known !== undefined) return Promise.resolve(known);
  return new Promise(resolve => {
    waiters.set(id, [...(waiters.get(id) ?? []), resolve]);
    bridge!.postMessage({ type: 'surface', id, query: true, visible: false });
  });
}

const overlaySelector = `.${controlStyles.positioner}`;
const device = (box: DOMRect, scale: number) =>
  ({ left: box.left * scale, top: box.top * scale, right: box.right * scale, bottom: box.bottom * scale });

// 每帧读占位元素与浮层位置，变化才发给壳；卸载或停用时隐藏画面区。
export function useNativeSurface(id: string, element: RefObject<HTMLElement | null>, active: boolean) {
  useEffect(() => {
    if (bridge === null || !active) return;
    let last = '', frame = 0;
    const tick = () => {
      frame = requestAnimationFrame(tick);
      const target = element.current;
      if (target === null) return;
      const scale = window.devicePixelRatio;
      const box = target.getBoundingClientRect();
      const holes = [...document.querySelectorAll(overlaySelector)].map(overlay => overlay.getBoundingClientRect())
        .filter(overlay => overlay.width > 0 && overlay.right > box.left && overlay.left < box.right
          && overlay.bottom > box.top && overlay.top < box.bottom)
        .map(overlay => device(overlay, scale));
      const message = { type: 'surface', id, visible: box.width > 0 && box.height > 0, rect: device(box, scale), holes };
      const text = JSON.stringify(message);
      if (text === last) return;
      last = text;
      bridge.postMessage(message);
    };
    tick();
    return () => {
      cancelAnimationFrame(frame);
      bridge.postMessage({ type: 'surface', id, visible: false });
    };
  }, [id, element, active]);
}
