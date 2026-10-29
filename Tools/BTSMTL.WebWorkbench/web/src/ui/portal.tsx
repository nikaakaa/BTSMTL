import { createContext, useContext } from 'react';

// 浮层挂到面板所在窗口的页面；拆出窗口中的面板由外壳提供该窗口的 body（D12）。
const PortalContainer = createContext<HTMLElement | null>(null);

export const PortalContainerProvider = PortalContainer.Provider;

export function usePortalContainer(): HTMLElement | undefined {
  return useContext(PortalContainer) ?? undefined;
}
