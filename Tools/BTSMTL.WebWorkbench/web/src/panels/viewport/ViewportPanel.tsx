import { useEffect, useRef, useState } from 'react';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import { consoleActions, getState, useWorkbench } from '../../state/store';
import { BarLabel, BarSeparator, Empty, PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { LogicScene, type CameraView } from './logicScene';
import styles from './viewport.module.css';

// 逻辑视图：世界、根、碰撞、命中几何；按 cursor 只改对象变换。
// three 场景属于面板实例，卸载或换窗口时释放；几何数据保留在 store（D12）。每个实例保存相机视角（D3）。

interface Params {
  camera: CameraView | null;
}

export function ViewportPanel() {
  const runId = useWorkbench(state => state.runId);
  if (!runId) return <Empty text="选择一个运行" />;
  return <LogicView />;
}

function LogicView() {
  const [params, setParams] = usePanelParams<Params>({ camera: null });
  const host = useRef<HTMLDivElement>(null);
  const scene = useRef<LogicScene | null>(null);
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  const panelId = usePanelId();
  const frame = useWorkbench(state => state.frame);
  const contacts = frame?.availability.find(item => item.channel === 'physics-contacts');
  const actualContacts = frame?.geometry.contacts.some(hit => hit.kind === 'kcc-contact');
  const queryCandidates = frame?.geometry.contacts.some(hit => hit.kind === 'query-candidate');
  const unknownContacts = frame?.geometry.contacts.some(hit => hit.kind == null);
  const [failure, setFailure] = useState<number | null>(null);
  const camera = useRef(params.camera);
  camera.current = params.camera;

  useEffect(() => {
    if (!host.current || !visible) return;
    let created: LogicScene;
    try {
      created = new LogicScene(host.current, panelWindow);
    } catch (error) {
      setFailure(consoleActions.error('viewport', error));
      return;
    }
    scene.current = created;
    if (camera.current) created.setCamera(camera.current);
    created.onCameraChanged = view => setParams({ camera: view });
    let drawnKey: string | null = null;
    created.onRendered = () => {
      const state = getState();
      if (drawnKey && drawnKey === state.frameKey) reportRendered(panelId, drawnKey, created.rendered());
    };
    let fitted = !!camera.current;
    const apply = () => {
      const state = getState();
      try {
        created.setGeometry(state.geometry);
        if (!state.archiveFrame || !state.frame || !state.frameKey) { created.clearFrame(); drawnKey = null; return; }
        created.draw(state.frame);
        created.drawArchive(state.archiveLayout, state.archiveFrame);
        drawnKey = state.frameKey;
        if (!fitted) { fitted = true; created.fit(); }
      } catch (error) {
        setFailure(consoleActions.error('viewport', error));
      }
    };
    apply();
    const unsubscribe = useWorkbench.subscribe((state, previous) => {
      if (state.frame !== previous.frame || state.archiveFrame !== previous.archiveFrame || state.geometry !== previous.geometry) apply();
    });
    return () => {
      unsubscribe();
      created.dispose();
      scene.current = null;
      clearRendered(panelId);
    };
  }, [panelWindow, visible, panelId, setParams]);

  return (
    <div className={styles.root}>
      <PanelBar wrap>
        <Button variant="ghost" onClick={() => scene.current?.fit()}>适配角色</Button>
        <BarSeparator />
        {frame && <>
          {!contacts?.available && <span title={contacts?.reason ?? '当前响应未声明实际KCC接触可用性'}>
            <BarLabel>{contacts ? '实际接触不可用' : '实际接触未声明'}</BarLabel>
          </span>}
          {actualContacts && <span className={styles.legend} data-kind="kcc-contact"><i aria-hidden="true" />实际接触</span>}
          {queryCandidates && <span className={styles.legend} data-kind="query-candidate"><i aria-hidden="true" />查询候选</span>}
          {unknownContacts && <span className={styles.legend}><i aria-hidden="true" />来源未记录</span>}
        </>}
      </PanelBar>
      {failure !== null && <ErrorLine text="逻辑视图绘制失败" entryId={failure} />}
      <div ref={host} className={styles.canvas} />
    </div>
  );
}
