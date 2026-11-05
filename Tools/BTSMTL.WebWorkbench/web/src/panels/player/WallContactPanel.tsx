import { useCallback, useEffect, useRef, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { playerApi } from '../../api/player';
import { samePreviewLoad, type PreviewSampleSource, type WallContact, type WallContactSurface, type WallContactTarget } from '../../api/playerRecords';
import type { PlayerAck, WallContactRequest } from '../../api/types';
import { wallContactApi, type WallContactAuthoring } from '../../api/wallContact';
import { playerService } from '../../state/player';
import { consoleActions, getState, useWorkbench } from '../../state/store';
import { Facts, Fold } from '../../ui/blocks';
import { Button, Checkbox, FieldLabel, Select, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './wallContact.module.css';

type Failure = { text: string; entryId: number | null };
const failure = (error: unknown): Failure => ({ text: error instanceof Error ? error.message : String(error),
  entryId: consoleActions.error('扶墙', error) });
const targetKey = (target: WallContactTarget) => JSON.stringify(target);

export function WallContactPanel() {
  const { runId, live, viewerOpen } = useWorkbench(useShallow(state => ({
    runId: state.runId, live: state.live, viewerOpen: state.viewerOpen,
  })));
  const [actorId, setActorId] = useState<string | null>(null);
  if (runId !== null) return <div className={styles.notice}>当前历史档案没有扶墙最终观察通道，无法读取该帧的扶墙结果。</div>;
  const player = live?.player;
  const source = player?.source;
  const reason = !viewerOpen || !player ? '先连接播放器。'
    : player.wallContactAvailable !== true ? '当前播放器没有提供扶墙调参能力。'
    : !source?.loadCommandId ? '先载入正式内容。'
    : !player.actors?.length ? '当前内容没有实际运行的角色。' : null;
  if (reason !== null || !live || !source) return <div className={styles.notice}>{reason}</div>;
  const actualActor = player!.actors!.includes(actorId ?? '') ? actorId : null;
  const scope = JSON.stringify([live.liveId, source.loadCommandId, source.execution, source.content, source.contentRevision]);
  return <section className={styles.panel}>
    <div className={styles.row}><FieldLabel label="扶墙角色" />
      <Select value={actualActor} label="实际运行角色" placeholder="选择实际运行角色"
        options={player!.actors!.map(id => ({ value: id, label: id }))} onChange={setActorId} />
    </div>
    {actualActor !== null && <WallActor key={`${scope}/${actualActor}`} liveId={live.liveId} source={source}
      actorId={actualActor} liveSnapshot={player!.wallContact ?? null} />}
  </section>;
}

function useWallContact(liveId: string, source: PreviewSampleSource, actorId: string,
  target: WallContactTarget | null, liveSnapshot: WallContact | null) {
  const context = useRef(source).current;
  const node = useRef(target).current;
  const [data, setData] = useState<WallContact | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<Failure | null>(null);
  const pending = useRef<string | null>(null);
  const matches = useCallback((value: WallContact) => {
    if (value.loadCommandId !== context.loadCommandId || value.actorId !== actorId) return false;
    if (node === null) return true;
    const selected = value.targets[value.selected];
    return selected !== undefined && targetKey(selected) === targetKey(node);
  }, [actorId, context.loadCommandId, node]);
  const accept = useCallback((value: WallContact) => {
    if (!matches(value)) return;
    setData(previous => ({ ...value, fields: value.fields.length ? value.fields : previous?.fields ?? [] }));
  }, [matches]);
  const acceptAck = useCallback((ack: PlayerAck) => {
    if (pending.current !== ack.commandId || getState().liveId !== liveId ||
      !samePreviewLoad(context, getState().live?.player?.source ?? null)) return;
    pending.current = null;
    setBusy(false);
    if (!ack.ok) { setError({ text: ack.error ?? '播放器拒绝了扶墙命令。', entryId: null }); return; }
    if (ack.wallContact) accept(ack.wallContact);
    setStatus(`播放器已接收命令${ack.frame === undefined ? '' : `，确认帧 ${ack.frame}`}。`);
  }, [accept, liveId, context]);
  useEffect(() => playerService.subscribeAck(acceptAck), [acceptAck]);
  useEffect(() => { if (liveSnapshot !== null) accept(liveSnapshot); }, [accept, liveSnapshot]);
  useEffect(() => () => { pending.current = null; }, []);
  const send = useCallback(async (request: WallContactRequest) => {
    const commandId = crypto.randomUUID();
    pending.current = commandId;
    setBusy(true); setError(null); setStatus('正在发送…');
    const result = await playerService.wallContact({ type: 'wall-contact', commandId, focusActorId: actorId, ...request });
    if (pending.current !== commandId) return;
    if (result?.ack) acceptAck(result.ack);
    else {
      setBusy(false);
      if (result === null) setError({ text: '请求未完成，具体错误见控制台。', entryId: null });
      else setStatus(result.stage === 'submitted' || result.stage === 'sent' ? '命令已提交，等待播放器回执。' : `命令状态：${result.stage}`);
    }
  }, [acceptAck, actorId]);
  const receipt = async () => {
    const commandId = pending.current;
    if (commandId === null) return;
    try {
      const result = await playerApi.readCommand(liveId, commandId);
      if (result.ack) acceptAck(result.ack);
      else setStatus(`命令状态：${result.stage}，尚未收到播放器回执。`);
    } catch (error) { setError(failure(error)); }
  };
  return { data, busy, status, error, send, receipt, waiting: pending.current !== null && !busy };
}

function WallActor({ liveId, source, actorId, liveSnapshot }: {
  liveId: string; source: PreviewSampleSource; actorId: string; liveSnapshot: WallContact | null;
}) {
  const state = useWallContact(liveId, source, actorId, null, liveSnapshot);
  const [selected, setSelected] = useState<string | null>(null);
  const send = state.send;
  useEffect(() => { void send({ operation: 'read' }); }, [send]);
  const target = state.data?.targets.find(item => targetKey(item) === selected) ?? null;
  return <>
    <div className={styles.row}><FieldLabel label="扶墙实例" />
      <Select value={target === null ? null : targetKey(target)} label="实际扶墙节点实例" placeholder="选择节点实例"
        options={(state.data?.targets ?? []).map(item => ({ value: targetKey(item), label: `${item.nodeId} · ${item.instanceId}` }))}
        onChange={setSelected} disabled={state.busy} />
      <Button disabled={state.busy} onClick={() => void send({ operation: 'read' })}>刷新实例</Button>
    </div>
    {state.error && <ErrorLine {...state.error} />}
    {state.waiting && <Button onClick={() => void state.receipt()}>更新命令回执</Button>}
    {state.data?.targets.length === 0 && <p>当前角色没有实际扶墙节点；需要先发布并载入包含扶墙节点的内容。</p>}
    {target !== null && <WallNode key={targetKey(target)} liveId={liveId} source={source} actorId={actorId}
      target={target} liveSnapshot={liveSnapshot} />}
  </>;
}

function WallNode({ liveId, source, actorId, target, liveSnapshot }: {
  liveId: string; source: PreviewSampleSource; actorId: string; target: WallContactTarget; liveSnapshot: WallContact | null;
}) {
  const state = useWallContact(liveId, source, actorId, target, liveSnapshot);
  const [draft, setDraft] = useState<Record<string, string> | null>(null);
  const send = state.send;
  useEffect(() => { void send({ operation: 'read', ...target }); }, [send]);
  const data = state.data;
  useEffect(() => {
    if (data?.fields.length && data.fields.every(field => field.value !== null))
      setDraft(previous => previous ?? Object.fromEntries(data.fields.map(field => [field.id, field.value!])));
  }, [data]);
  const groups = [...new Set(data?.fields.map(field => field.group) ?? [])];
  const values = draft === null ? [] : Object.entries(draft).map(([id, value]) => ({ id, value }));
  return <>
    <div className={styles.actions}>
      <Button disabled={state.busy} onClick={() => void send({ operation: 'read', ...target })}>读取运行参数</Button>
      <Button disabled={state.busy || !data} onClick={() => void send({ operation: 'observe', loadCommandId: source.loadCommandId!, ...target })}>读取最终观察</Button>
      {state.waiting && <Button onClick={() => void state.receipt()}>更新命令回执</Button>}
    </div>
    {state.error && <ErrorLine {...state.error} />}
    {state.status && <p role="status">{state.status}</p>}
    {data && <>
      <Facts fields={[
        { displayName: '请求参数版本', value: data.requestedVersion },
        { displayName: '已采用参数版本', value: data.appliedVersion },
        { displayName: '最终结果标识', value: data.completionIdentity || '尚无最终结果' },
      ]} />
      {draft !== null && <>
        {groups.map(group => <Fold key={group} title={group || '参数'} defaultOpen>
          {data.fields.filter(field => field.group === group).map(field => <div key={field.id} className={styles.row}>
            <FieldLabel label={field.displayName} />
            {field.kind === 'boolean' ? <Checkbox label={field.displayName} checked={draft[field.id].toLowerCase() === 'true'}
              disabled={state.busy} onChange={value => setDraft({ ...draft, [field.id]: value ? 'True' : 'False' })} />
              : <TextInput aria-label={field.displayName} value={draft[field.id]} disabled={state.busy}
                onChange={event => setDraft({ ...draft, [field.id]: event.target.value })} />}
          </div>)}
        </Fold>)}
        <div className={styles.actions}>
          <Button disabled={state.busy} onClick={() => void send({ operation: 'apply', loadCommandId: source.loadCommandId!,
            ...target, expectedParameterVersion: data.requestedVersion, values })}>应用到运行实例</Button>
          <Button disabled={state.busy} onClick={() => setDraft(Object.fromEntries(data.fields.map(field => [field.id, field.value!])))}>还原为读取值</Button>
        </div>
        <SourceSave key={data.sourceAssetId} data={data} target={target} values={values} onLoad={setDraft} />
      </>}
      <Fold title="调试墙体"><WallSurface key={data.loadCommandId} available={data.wallAvailable} current={data.wall}
        busy={state.busy} onApply={wall => void send({ operation: 'wall', loadCommandId: source.loadCommandId!, ...target, wall })} /></Fold>
      <Fold title="最终观察" defaultOpen>
        {data.observationUnavailableReason !== null && <p>{data.observationUnavailableReason}</p>}
        <Hand title="左手" value={data.left} /><Hand title="右手" value={data.right} />
      </Fold>
    </>}
  </>;
}

function SourceSave({ data, target, values, onLoad }: {
  data: WallContact; target: WallContactTarget; values: { id: string; value: string }[];
  onLoad(value: Record<string, string>): void;
}) {
  const address = { sourceAssetId: data.sourceAssetId, graphId: target.graphId, nodeId: target.nodeId };
  const [saved, setSaved] = useState<WallContactAuthoring | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [error, setError] = useState<Failure | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    wallContactApi.read(address, controller.signal).then(setSaved).catch(error => {
      if (!controller.signal.aborted) setError(failure(error));
    });
    return () => controller.abort();
  }, [address.sourceAssetId, address.graphId, address.nodeId]);
  const save = async () => {
    setBusy(true); setError(null); setStatus('正在保存作者参数…');
    try {
      const result = await wallContactApi.save(address, saved!.revision, values);
      setSaved(result); setStatus(result.saved ? '作者参数已保存。运行实例的采用状态见上方。' : '作者参数没有变化。');
    } catch (error) { setError(failure(error)); setStatus('保存未完成，当前编辑仍保留。'); }
    finally { setBusy(false); }
  };
  return <Fold title="作者保存" defaultOpen>
    {error && <ErrorLine {...error} />}
    {saved && <p>{saved.assetPath}</p>}
    <div className={styles.actions}>
      <Button disabled={!saved || busy} onClick={() => void save()}>保存到作者资产</Button>
      <Button disabled={!saved || busy} onClick={() => onLoad(Object.fromEntries(saved!.values.map(item => [item.id, item.value])))}>载入已保存参数</Button>
      <Button disabled={busy} onClick={async () => {
        try { setSaved(await wallContactApi.read(address)); setError(null); setStatus('已重新读取作者版本，当前编辑仍保留。'); }
        catch (error) { setError(failure(error)); }
      }}>重读作者版本</Button>
    </div>
    {status && <p role="status">{status}</p>}
  </Fold>;
}

type VectorDraft = { x: string; y: string; z: string };
function WallSurface({ available, current, busy, onApply }: {
  available: boolean; current: WallContactSurface | null; busy: boolean; onApply(value: WallContactSurface): void;
}) {
  const [enabled, setEnabled] = useState(current === null ? null : String(current.enabled));
  const [visible, setVisible] = useState(current === null ? null : String(current.visible));
  const vector = (value: WallContactSurface['position'] | undefined): VectorDraft => value
    ? { x: String(value.x), y: String(value.y), z: String(value.z) } : { x: '', y: '', z: '' };
  const [vectors, setVectors] = useState({ position: vector(current?.position), rotation: vector(current?.rotation), size: vector(current?.size) });
  const [error, setError] = useState<string | null>(null);
  if (!available) return <p>当前场景没有提供调试墙体操作。</p>;
  const complete = enabled !== null && visible !== null && Object.values(vectors).every(v => Object.values(v).every(value => value.trim() !== ''));
  const parse = (v: VectorDraft) => ({ x: Number(v.x), y: Number(v.y), z: Number(v.z) });
  return <>
    <div className={styles.row}><FieldLabel label="参与扶墙" /><Select value={enabled} label="参与扶墙" placeholder="明确选择"
      options={[{ value: 'true', label: '启用' }, { value: 'false', label: '停用' }]} onChange={setEnabled} disabled={busy} /></div>
    <div className={styles.row}><FieldLabel label="显示墙体" /><Select value={visible} label="显示墙体" placeholder="明确选择"
      options={[{ value: 'true', label: '显示' }, { value: 'false', label: '隐藏' }]} onChange={setVisible} disabled={busy} /></div>
    {(['position', 'rotation', 'size'] as const).map((key, index) => <div className={styles.row} key={key}>
      <FieldLabel label={['位置（米）', '旋转（度）', '尺寸（米）'][index]} />
      {(['x', 'y', 'z'] as const).map(axis => <TextInput key={axis} aria-label={`${key} ${axis}`} placeholder={axis}
        value={vectors[key][axis]} disabled={busy} onChange={event => setVectors({ ...vectors, [key]: { ...vectors[key], [axis]: event.target.value } })} />)}
    </div>)}
    {error !== null && <p role="alert">{error}</p>}
    <Button disabled={busy || !complete} onClick={() => {
      const position = parse(vectors.position), rotation = parse(vectors.rotation), size = parse(vectors.size);
      if (![position, rotation, size].every(value => Object.values(value).every(Number.isFinite)) ||
        !Object.values(size).every(value => value > 0)) {
        setError('位置和旋转须为有限数值，三个尺寸须为有限正数。'); return;
      }
      setError(null); onApply({ enabled: enabled === 'true', visible: visible === 'true', position, rotation, size });
    }}>应用墙体</Button>
  </>;
}

function Hand({ title, value }: { title: string; value: WallContact['left'] }) {
  const labels: Record<string, string> = { phase: '阶段', result: '结果', hasSolution: '存在解', armWeight: '手臂权重',
    wristWeight: '手腕权重', fingerWeight: '手指权重', positionResidual: '位置残差', palmAngle: '掌面角度',
    minimumClearance: '最小间隙', fingerUp: '手指朝上量', elbowAngle: '肘角度', forearmTwist: '前臂扭转',
    animatedElbowAngle: '原动画肘角度', jointLimitExcess: '关节超限', jointRotationResidual: '关节旋转残差',
    shoulderAngles: '肩部角度', wristAngles: '手腕角度', animatedWristAngles: '原动画手腕角度',
    handPosition: '手部位置', elbowPosition: '肘部位置', targetPosition: '目标位置' };
  return <Fold title={title} defaultOpen>{value === null ? <p>该手尚无最终观察。</p>
    : <Facts fields={Object.entries(value).map(([id, value]) => ({ displayName: labels[id],
      value: typeof value === 'object' ? `${value.x}, ${value.y}, ${value.z}` : String(value) }))} />}</Fold>;
}
