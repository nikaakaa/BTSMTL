import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { useWorkbench } from '../../state/store';
import { Empty, Facts, Fold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { PendingWallCommand, wallContactApi, type WallField, type WallHand, type WallPlane, type WallSnapshot, type WallSource } from './protocol';
import styles from './wall-contact.module.css';

type SourceState = { kind: 'loading' } | { kind: 'failed'; error: string } | { kind: 'ready'; source: WallSource };
const reasonLabels: Record<string, string> = {
  Free: '未扶墙', Reaching: '正在伸手', Releasing: '正在松掌', Planted: '已扶稳',
  PoseResidual: '姿态还未贴合', SurfaceResidual: '掌面间隙未达标', BudgetExhausted: '本帧求解预算用尽',
  EquipmentTakeover: '装备占用此手', JointResidual: '实际关节超出约束',
};
const messageOf = (error: unknown) => error instanceof Error ? error.message : String(error);

export function WallContactPanel() {
  const state = useWorkbench(useShallow(current => ({
    liveId: current.liveId, viewerOpen: current.viewerOpen, player: current.live?.player,
    actor: current.selection?.kind === 'actor' ? current.selection.id : null,
  })));
  const scope = `${state.liveId}/${state.actor}`;
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  const [data, setData] = useState<WallSnapshot | null>(null);
  const [fields, setFields] = useState<WallField[]>([]);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [editVersion, setEditVersion] = useState('');
  const [busy, setBusy] = useState(false);
  const [pending, setPending] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [source, setSource] = useState<SourceState>({ kind: 'loading' });
  const inFlight = useRef<symbol | null>(null);
  const ready = state.viewerOpen && !!state.player && ['paused', 'playing', 'stepping', 'seeking'].includes(state.player.state);

  useEffect(() => {
    setData(null); setFields([]); setDraft({}); setPending(null); setError(''); setNotice('');
    inFlight.current = null; setBusy(false);
  }, [scope]);

  const accept = useCallback((packet: WallSnapshot) => {
    setData(current => current && current.loadCommandId === packet.loadCommandId &&
      BigInt(current.requestedVersion) > BigInt(packet.requestedVersion) ? current : packet);
    if (packet.fields.length) {
      setFields(packet.fields);
      setDraft(Object.fromEntries(packet.fields.map(field => [field.id, field.value])));
      setEditVersion(packet.requestedVersion);
    }
    setPending(null);
  }, []);

  const run = useCallback(async (operation: () => Promise<WallSnapshot>) => {
    if (inFlight.current !== null) return;
    const operationId = Symbol();
    inFlight.current = operationId;
    setBusy(true); setError(''); setNotice('');
    try {
      const packet = await operation();
      if (scopeRef.current === scope) accept(packet);
    } catch (failure) {
      if (scopeRef.current !== scope) return;
      if (failure instanceof PendingWallCommand) setPending(failure.commandId);
      else setPending(null);
      setError(messageOf(failure));
    } finally {
      if (inFlight.current === operationId) { inFlight.current = null; setBusy(false); }
    }
  }, [accept, scope]);

  const read = useCallback((selected?: WallSnapshot['targets'][number]) => {
    const { liveId, actor } = state;
    if (liveId && actor)
      void run(() => wallContactApi.command(liveId, {
        type: 'wall-contact', commandId: crypto.randomUUID(), operation: 'read', focusActorId: actor, ...selected,
      }));
  }, [run, state.liveId, state.actor]);

  useEffect(() => {
    if (!data || !ready || pending || busy || !state.liveId) return;
    const current = data;
    const liveId = state.liveId;
    let reading = false, cancelled = false;
    const timer = setInterval(async () => {
      if (reading || inFlight.current !== null) return;
      reading = true;
      try {
        const packet = await wallContactApi.command(liveId, {
          type: 'wall-contact', commandId: crypto.randomUUID(), operation: 'observe', focusActorId: current.actorId,
          loadCommandId: current.loadCommandId, ...current.targets[current.selected],
        });
        if (!cancelled) accept(packet);
      } catch (failure) {
        if (!cancelled) {
          if (failure instanceof PendingWallCommand) setPending(failure.commandId);
          setError(messageOf(failure));
        }
      } finally { reading = false; }
    }, 750);
    return () => { cancelled = true; clearInterval(timer); };
  }, [data, ready, pending, busy, state.liveId, accept]);

  const selected = data?.targets[data.selected];
  const sourceTarget = data && selected ? { sourceAssetId: data.sourceAssetId, graphId: selected.graphId, nodeId: selected.nodeId } : null;
  const sourceKey = sourceTarget ? `${sourceTarget.sourceAssetId}/${sourceTarget.graphId}/${sourceTarget.nodeId}` : '';
  useEffect(() => {
    if (!sourceTarget) return;
    let current = true;
    setSource({ kind: 'loading' });
    void wallContactApi.readSource(sourceTarget).then(
      value => { if (current) setSource({ kind: 'ready', source: value }); },
      failure => { if (current) setSource({ kind: 'failed', error: messageOf(failure) }); },
    );
    return () => { current = false; };
  }, [sourceKey]);

  const reloadSource = async () => {
    if (!sourceTarget) return;
    setSource({ kind: 'loading' });
    try {
      const value = await wallContactApi.readSource(sourceTarget);
      if (scopeRef.current === scope) setSource({ kind: 'ready', source: value });
    } catch (failure) { if (scopeRef.current === scope) setSource({ kind: 'failed', error: messageOf(failure) }); }
  };

  const apply = () => {
    const liveId = state.liveId;
    if (!data || !liveId || !selected) return;
    void run(() => wallContactApi.command(liveId, {
      type: 'wall-contact', commandId: crypto.randomUUID(), operation: 'apply', focusActorId: data.actorId,
      loadCommandId: data.loadCommandId, ...selected, expectedParameterVersion: editVersion,
      values: fields.filter(field => field.value !== draft[field.id]).map(field => ({ id: field.id, value: draft[field.id] })),
    }));
  };

  const save = async () => {
    if (!sourceTarget || source.kind !== 'ready') return;
    setBusy(true); setError(''); setNotice('');
    try {
      const saved = await wallContactApi.saveSource({
        target: sourceTarget, expectedRevision: source.source.revision, values: fields.map(field => ({ id: field.id, value: field.value })),
      });
      if (scopeRef.current !== scope) return;
      setSource({ kind: 'ready', source: saved });
      setNotice('已保存回原 PoseGraph。当前 Player 保留本次试调，下次发布载入使用保存值。');
    } catch (failure) { if (scopeRef.current === scope) setError(messageOf(failure)); }
    finally { if (scopeRef.current === scope) setBusy(false); }
  };

  const changed = fields.some(field => field.value !== draft[field.id]);
  const adopted = data !== null && data.requestedVersion === data.appliedVersion;
  const disabled = !ready || busy || pending !== null;
  const groups = new Map<string, WallField[]>();
  for (const field of fields) {
    const group = groups.get(field.group);
    if (group) group.push(field); else groups.set(field.group, [field]);
  }

  return <div className={styles.root}>
    <PanelBar>
      <span>{state.actor ?? '先在画面中点选角色'}</span>
      <Button disabled={!ready || !state.actor || busy || !!pending} onClick={() => read()}>读取扶墙参数</Button>
      {pending && <Button disabled={busy || !state.liveId} onClick={() => void run(() => wallContactApi.recheck(state.liveId!, pending))}>重查原命令</Button>}
    </PanelBar>
    {error && <div className={styles.error} role="alert">{error}</div>}
    {notice && <div className={styles.notice} role="status">{notice}</div>}
    {!data ? <Empty text={ready ? '读取所选角色后，调整墙面与扶墙参数' : '先启动并载入 Player'} /> : <div className={styles.scroll}>
      <PanelBar>
        <Select label="扶墙节点" value={String(data.selected)} disabled={disabled} options={data.targets.map((target, index) => ({
          value: String(index), label: `${target.graphId} / ${target.nodeId}`,
        }))} onChange={index => read(data.targets[Number(index)])} />
        <span className={styles.status}>{adopted ? 'Player 已采用' : '等待表现帧采用'}{changed ? ' · 有未应用改动' : ''}</span>
      </PanelBar>
      <Fold title="实际贴掌结果" defaultOpen>
        <div className={styles.hands}><Hand title="左手" hand={data.left} /><Hand title="右手" hand={data.right} /></div>
      </Fold>
      <Fold title="预览墙面" defaultOpen>
        <PlaneForm key={`${data.loadCommandId}/${data.actorId}`} plane={data.wall} disabled={disabled} onApply={wall => {
          void run(() => wallContactApi.command(state.liveId!, {
            type: 'wall-contact', commandId: crypto.randomUUID(), operation: 'wall', focusActorId: data.actorId,
            loadCommandId: data.loadCommandId, ...data.targets[data.selected], wall,
          }));
        }} />
      </Fold>
      {Array.from(groups, ([group, members]) => <Fold key={group} title={group} defaultOpen={group === '进出与曲线'}>
        <fieldset className={styles.fields} disabled={disabled}>
          {members.map(field => <label key={field.id} className={styles.field}>
            <span title={field.id}>{field.displayName}</span>
            {field.kind === 'boolean' ? <input type="checkbox" checked={draft[field.id].toLowerCase() === 'true'}
              onChange={event => setDraft(current => ({ ...current, [field.id]: String(event.target.checked) }))} />
              : <input type="number" step={field.kind === 'integer' ? '1' : 'any'} value={draft[field.id]}
                onChange={event => setDraft(current => ({ ...current, [field.id]: event.target.value }))} />}
          </label>)}
        </fieldset>
        {group === '进出与曲线' && <div className={styles.curves}>
          <Curve title="进入" start={draft.EntryStartSlope} end={draft.EntryEndSlope} />
          <Curve title="退出" start={draft.ExitStartSlope} end={draft.ExitEndSlope} />
        </div>}
      </Fold>)}
      <PanelBar>
        <Button disabled={disabled || !changed} onClick={apply}>应用到 Player</Button>
        <Button variant="ghost" disabled={disabled || !changed} onClick={() => setDraft(Object.fromEntries(fields.map(field => [field.id, field.value])))}>还原未应用改动</Button>
      </PanelBar>
      <Fold title="正式配置" defaultOpen>
        {source.kind === 'ready' ? <>
          <div className={styles.path}>{source.source.assetPath}</div>
          <PanelBar>
            <Button disabled={disabled || changed || !adopted || editVersion !== data.requestedVersion} onClick={() => void save()}
              tip={changed ? '先应用到 Player，再保存已经采用的参数' : '保存当前已采用值到原 PoseGraph'}>保存当前参数</Button>
            <Button variant="ghost" disabled={disabled} onClick={() => void reloadSource()}>重读正式配置</Button>
            <Button variant="ghost" disabled={disabled} onClick={() => setDraft(Object.fromEntries(source.source.values.map(value => [value.id, value.value])))}>取正式值试调</Button>
          </PanelBar>
        </> : <div className={styles.path}>
          {source.kind === 'loading' ? '正在读取正式配置…' : source.error}
          {source.kind === 'failed' && <Button variant="ghost" onClick={() => void reloadSource()}>重读正式配置</Button>}
        </div>}
      </Fold>
    </div>}
  </div>;
}

function Hand({ title, hand }: { title: string; hand: WallHand | null }) {
  return <section><strong>{title} · {hand ? reasonLabels[hand.result] ?? hand.result : '尚无完成帧'}</strong>
    {hand && <Facts fields={[
      { displayName: '手臂 / 手腕 / 展指', value: [hand.armWeight, hand.wristWeight, hand.fingerWeight].map(value => `${Math.round(value * 100)}%`).join(' / ') },
      { displayName: '位置差 / 掌面角', value: `${(hand.positionResidual * 1000).toFixed(1)} mm / ${hand.palmAngle.toFixed(1)}°` },
      { displayName: '最小净空', value: `${(hand.minimumClearance * 1000).toFixed(1)} mm` },
      { displayName: '肘角（动画 → 实际）', value: `${hand.animatedElbowAngle.toFixed(1)}° → ${hand.elbowAngle.toFixed(1)}°` },
      { displayName: '腕部屈伸 / 偏摆 / 轴转', value: `${hand.wristAngles.x.toFixed(1)}° / ${hand.wristAngles.y.toFixed(1)}° / ${hand.wristAngles.z.toFixed(1)}°` },
      { displayName: '关节越界', value: `${hand.jointLimitExcess.toFixed(2)}°` },
    ]} />}
  </section>;
}

function Curve({ title, start, end }: { title: string; start: string; end: string }) {
  const first = Number(start), last = Number(end);
  return <figure><figcaption>{title}变化曲线</figcaption>
    <svg viewBox="0 0 180 100" role="img" aria-label={`${title}变化曲线，横轴时间，纵轴进度`}>
      <path className={styles.axis} d="M10 10 V90 H170" />
      {Number.isFinite(first) && Number.isFinite(last) && <path className={styles.curve}
        d={`M10 90 C63.33 ${90 - first * 80 / 3},116.67 ${10 + last * 80 / 3},170 10`} />}
    </svg>
  </figure>;
}

function PlaneForm({ plane, disabled, onApply }: { plane: WallPlane; disabled: boolean; onApply(wall: WallPlane): void }) {
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const values = new FormData(event.currentTarget);
    const vector = (name: string) => ({ x: Number(values.get(`${name}.x`)), y: Number(values.get(`${name}.y`)), z: Number(values.get(`${name}.z`)) });
    onApply({ enabled: values.has('enabled'), visible: values.has('visible'), position: vector('position'), rotation: vector('rotation'), size: vector('size') });
  };
  return <form onSubmit={submit}><fieldset className={styles.fields} disabled={disabled}>
    <div className={styles.checks}>
      <label><input name="enabled" type="checkbox" defaultChecked={plane.enabled} />启用墙面</label>
      <label><input name="visible" type="checkbox" defaultChecked={plane.visible} />显示墙面</label>
    </div>
    {(['position', 'rotation', 'size'] as const).map(name => <label className={styles.vector} key={name}>
      <span>{name === 'position' ? '世界位置（米）' : name === 'rotation' ? '旋转（度）' : '尺寸（米）'}</span>
      <div>{(['x', 'y', 'z'] as const).map(axis => <input key={axis} name={`${name}.${axis}`} aria-label={`${name} ${axis}`}
        type="number" required step="any" defaultValue={plane[name][axis]} />)}</div>
    </label>)}
    <Button type="submit">应用墙面摆放</Button>
    <small>预览墙面使用真实碰撞体，摆放不写入正式场景。</small>
  </fieldset></form>;
}
