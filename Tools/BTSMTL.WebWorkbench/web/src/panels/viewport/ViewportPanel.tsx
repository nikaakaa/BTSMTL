import { useEffect, useRef, useState } from 'react';
import { samePreviewLoad, type PlayerJobId } from '../../api/playerRecords';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, reportRendered } from '../../shell/readiness';
import { consoleActions, getState, useWorkbench, type WorkbenchState } from '../../state/store';
import { BarLabel, BarSeparator, Empty, PanelBar } from '../../ui/blocks';
import { Button, Tip } from '../../ui/controls';
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
  return <LogicView key={runId} runId={runId} />;
}

function presentationForLogic(state: Pick<WorkbenchState, 'frame' | 'archiveFrame'>) {
  const source = state.frame?.source;
  if (source === undefined || source === null || source.content === null || source.execution.kind === null || source.execution.identity === null) return null;
  return state.archiveFrame?.presentations.filter(item => samePreviewLoad(item.source, source) &&
    item.source.clock.position.hostFrame === source.clock.position.hostFrame).at(-1) ?? null;
}

function LogicView({ runId }: { runId: PlayerJobId }) {
  const [params, setParams] = usePanelParams<Params>({ camera: null });
  const host = useRef<HTMLDivElement>(null);
  const scene = useRef<LogicScene | null>(null);
  const panelWindow = usePanelWindow();
  const visible = usePanelVisible();
  const panelId = usePanelId();
  const frame = useWorkbench(state => state.frame);
  const presentation = useWorkbench(presentationForLogic);
  const archiveFrame = useWorkbench(state => state.archiveFrame);
  const layout = useWorkbench(state => state.archiveLayout);
  const runFailure = useWorkbench(state => state.failure);
  const bones = archiveFrame?.availability.find(item => (item.channel === 'bones' || item.channel === 'presentation') && !item.available);
  const boneReason = !presentation ? bones?.reason ?? '当前帧没有与逻辑记录同源的骨骼记录；根、碰撞与命中仍按逻辑记录显示'
    : layout === null ? '当前档案没有骨架布局'
    : presentation.actors.every(actor => actor.localBones.length === 0) ? '当前表现记录没有角色骨骼数据'
    : presentation.actors.every(actor => !layout.actors.some(rig => rig.actorId === actor.actorId && rig.rosterIndex === actor.rosterIndex))
      ? '当前表现记录没有匹配的角色骨架布局' : null;
  const contacts = frame?.availability.find(item => item.channel === 'physics-contacts');
  const actualContacts = frame?.geometry.contacts.some(hit => hit.kind === 'kcc-contact');
  const queryCandidates = frame?.geometry.contacts.some(hit => hit.kind === 'query-candidate');
  const objectQueries = frame?.geometry.sweeps.filter(query => query.actorId === null).length;
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
        if (!state.frame || !state.frameKey) {
          created.clearFrame(); drawnKey = null; clearRendered(panelId); return;
        }
        created.draw(state.frame);
        created.drawPresentation(state.archiveLayout, presentationForLogic(state));
        drawnKey = state.frameKey;
        if (!fitted) { fitted = true; created.fit(); }
      } catch (error) {
        setFailure(consoleActions.error('viewport', error));
      }
    };
    apply();
    const unsubscribe = useWorkbench.subscribe((state, previous) => {
      if (state.frame !== previous.frame || state.frameKey !== previous.frameKey || state.archiveFrame !== previous.archiveFrame ||
        state.archiveLayout !== previous.archiveLayout || state.geometry !== previous.geometry) apply();
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
        <Tip content={`逻辑几何与骨骼记录；不是播放器原始画面\nRun：${runId}`}><span><BarLabel>逻辑视图</BarLabel></span></Tip>
        {frame && <>
          <BarLabel>记录帧 {frame.hostFrame}</BarLabel>
          <Tip content={frame.source?.content === null || frame.source?.content === undefined ? '原记录没有实际采用内容身份，不能确认与画面同源'
            : `内容类型：${frame.source.content.kind}\n实际采用修订：${frame.source.content.revision}\n来源时钟：${frame.source.clock.seconds} 秒\n${frame.source.clock.positionDisplayName}：${frame.source.clock.position.hostFrame}`}>
            <span><BarLabel>{frame.source?.content ? `${frame.source.content.kind} · ${frame.source.content.revision}` : '采用内容未记录'}</BarLabel></span>
          </Tip>
          {boneReason !== null && <Tip content={boneReason}>
            <span><BarLabel>骨骼不可用</BarLabel></span>
          </Tip>}
          {!contacts?.available && <Tip content={contacts?.reason ?? '当前响应未声明实际KCC接触可用性'}><span>
            <BarLabel>{contacts ? '实际接触不可用' : '实际接触未声明'}</BarLabel>
          </span></Tip>}
          {actualContacts && <span className={styles.legend} data-kind="kcc-contact"><i aria-hidden="true" />实际接触</span>}
          {queryCandidates && <span className={styles.legend} data-kind="query-candidate"><i aria-hidden="true" />查询候选</span>}
          {objectQueries !== undefined && objectQueries > 0 && <BarLabel>{objectQueries} 个查询未关联角色</BarLabel>}
          {unknownContacts && <span className={styles.legend}><i aria-hidden="true" />来源未记录</span>}
        </>}
      </PanelBar>
      {failure !== null && <ErrorLine text="逻辑视图绘制失败" entryId={failure} />}
      {runFailure !== null && <ErrorLine text={runFailure.reason} entryId={runFailure.entryId} />}
      <div ref={host} className={styles.canvas}>
        {!frame && <div className={styles.message}><Empty text={runFailure?.reason ?? '正在读取当前逻辑帧'} /></div>}
      </div>
    </div>
  );
}
