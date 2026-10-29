import type { DockviewPanelApi, IDockviewPanelProps } from 'dockview-react';
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { PortalContainerProvider } from '../ui/portal';
import { requestLayoutSave } from './layout';
import type { PanelType } from './panelTypes';
import { bindShortcuts } from './shortcuts';
import styles from './shell.module.css';

// 面板实例环境：所在窗口、是否可见、实例参数（D3、D12）。

interface PanelEnvironment {
  id: string;
  type: PanelType;
  api: DockviewPanelApi;
  window: Window;
  visible: boolean;
}

const PanelContext = createContext<PanelEnvironment | null>(null);

function usePanelEnvironment(): PanelEnvironment {
  const environment = useContext(PanelContext);
  if (!environment) throw new Error('面板钩子只能在面板内使用');
  return environment;
}

export function PanelFrame({ type, props, children }: { type: PanelType; props: IDockviewPanelProps; children: ReactNode }) {
  const { api } = props;
  const [panelWindow, setPanelWindow] = useState(() => api.getWindow());
  const [visible, setVisible] = useState(() => api.isVisible);

  useEffect(() => {
    const location = api.onDidLocationChange(() => setPanelWindow(api.getWindow()));
    const visibility = api.onDidVisibilityChange(event => setVisible(event.isVisible));
    setPanelWindow(api.getWindow());
    setVisible(api.isVisible);
    return () => { location.dispose(); visibility.dispose(); };
  }, [api]);

  useEffect(() => bindShortcuts(panelWindow), [panelWindow]);

  return (
    <PanelContext.Provider value={{ id: api.id, type, api, window: panelWindow, visible }}>
      <PortalContainerProvider value={panelWindow.document.body}>
        <div className={styles.panel}>{children}</div>
      </PortalContainerProvider>
    </PanelContext.Provider>
  );
}

export function usePanelId(): string {
  return usePanelEnvironment().id;
}

// 面板所在窗口：浮层、事件与绘制循环都按它绑定，窗口变化时重新绑定。
export function usePanelWindow(): Window {
  return usePanelEnvironment().window;
}

// 未激活页签被移出页面，不可见时不得绘制。
export function usePanelVisible(): boolean {
  return usePanelEnvironment().visible;
}

export function usePanelApi(): DockviewPanelApi {
  return usePanelEnvironment().api;
}

// 实例局部状态：存入面板参数，随布局保存。
export function usePanelParams<T extends object>(defaults: T): [T, (patch: Partial<T>) => void] {
  const { api } = usePanelEnvironment();
  const [params, setParams] = useState<T>(() => ({ ...defaults, ...(api.getParameters() as Partial<T>) }));
  const current = useRef(params);
  // 写入 dockview 参数是副作用，不能放进 React 的状态更新函数。
  const update = useCallback((patch: Partial<T>) => {
    const next = { ...current.current, ...patch };
    current.current = next;
    setParams(next);
    api.updateParameters(next as Record<string, unknown>);
    requestLayoutSave();
  }, [api]);
  return [params, update];
}

// 在面板所在窗口上调度绘制；不可见时停止。
export function usePanelAnimationFrame(callback: (now: number) => void, active: boolean) {
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  useEffect(() => {
    if (!active || !visible) return;
    let handle = 0;
    const loop = (now: number) => {
      callback(now);
      handle = panelWindow.requestAnimationFrame(loop);
    };
    handle = panelWindow.requestAnimationFrame(loop);
    return () => panelWindow.cancelAnimationFrame(handle);
  }, [panelWindow, visible, active, callback]);
}
