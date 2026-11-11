import { useMemo } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { useDocumentObservationBinding } from '../../state/authoring';
import { documentObservationNotice, isStateMachine, matchesDocumentCall, routeObservation } from '../../state/graphModel';
import { useInputObservation } from '../../state/inputModel';
import { quickInputSourceKey, quickRouteStale } from '../../state/quickPreview';
import { isRealArchive, useWorkbench } from '../../state/store';
import { Fold } from '../../ui/blocks';
import { useQuickName } from './QuickPreview';
import styles from './quickPreview.module.css';

export function QuickRouteComparison() {
  const { plan, scheme, stale, real, frames, graphs } = useWorkbench(useShallow(state => {
    const plan = state.quickPreview.route?.plan ?? null;
    const description = state.quickPreview.description;
    return { plan, stale: quickRouteStale(), real: isRealArchive(state), frames: state.routeFrames, graphs: state.graphs,
      scheme: description.kind === 'ready' ? description.reply.data?.root?.inputSchemes.find(item =>
        quickInputSourceKey(item.source) === quickInputSourceKey(plan?.input ?? null)) ?? null : null };
  }));
  const observation = useWorkbench(useShallow(routeObservation));
  const input = useInputObservation(scheme?.inputTimeline ?? null);
  const binding = useDocumentObservationBinding();
  const name = useQuickName();
  const history = useMemo(() => {
    const result = new Map<string, { enter: number[]; exit: number[] }>();
    if (!real || plan === null || input.kind !== 'ready') return result;
    for (const frame of frames)
      for (const node of frame.nodes) {
        if (node.call === null || node.call.actorId !== input.input.actorId || !matchesDocumentCall(node.call, binding) ||
            node.kind !== 'StateScopeEntered' && node.kind !== 'StateScopeExited') continue;
        const key = JSON.stringify([node.graphId, node.nodeId]);
        let times = result.get(key);
        if (times === undefined) { times = { enter: [], exit: [] }; result.set(key, times); }
        times[node.kind === 'StateScopeEntered' ? 'enter' : 'exit'].push(frame.seconds - input.originSeconds);
      }
    return result;
  }, [real, plan, frames, input.kind === 'ready' ? input.input.actorId : null, input.kind === 'ready' ? input.originSeconds : null, binding]);
  if (!real || plan === null) return null;
  if (stale) return <Fold title="快速路线与真实记录对比"><p>作者内容或输入已变化，原快速路线已过期。</p></Fold>;
  if (input.kind !== 'ready') return <Fold title="快速路线与真实记录对比"><p>{input.reason}</p></Fold>;
  if (observation.kind !== 'ready') return <Fold title="快速路线与真实记录对比"><p>{observation.reason}</p></Fold>;
  const notice = documentObservationNotice(observation.route, binding);
  if (notice !== null) return <Fold title="快速路线与真实记录对比"><p>{notice}</p></Fold>;
  const seconds = observation.route.seconds - input.originSeconds;
  const segments = plan.segments.filter(segment => segment.stateSource !== null);
  const current = segments.filter(segment => segment.start <= seconds && seconds < segment.end);
  const machineIds = new Set((graphs ?? []).filter(isStateMachine).map(graph => graph.id));
  const active = observation.route.active.filter(item => machineIds.has(item.documentId) && item.call !== null &&
    item.call.actorId === input.input.actorId && matchesDocumentCall(item.call, binding));
  const activeKeys = new Set(active.map(item => JSON.stringify([item.documentId, item.elementId])));
  const expectedKeys = new Set(current.map(segment => JSON.stringify([segment.stateSource!.graphId, segment.stateSource!.nodeId])));
  const same = activeKeys.size === expectedKeys.size && [...expectedKeys].every(key => activeKeys.has(key));
  const timeList = (values: number[] | undefined) => values?.length ? values.map(value => value.toFixed(3)).join('、') : '未在已读帧中记录';
  return <Fold title={`快速路线与真实记录 · 输入 ${seconds.toFixed(3)} 秒 · ${same ? '当前状态一致' : '当前状态分歧'}`}>
    <p>真实激活状态：{active.map(item => item.displayName).join('、') || '无'}。进入和退出时间只列已读取的档案帧。</p>
    <table className={styles.comparison}><thead><tr><th>快速路线状态与区间</th><th>实际进入 / 退出秒数</th><th>快速路线依据</th></tr></thead>
      <tbody>{segments.map((segment, index) => {
        const source = segment.stateSource!;
        const key = JSON.stringify([source.graphId, source.nodeId]);
        const times = history.get(key);
        const diverged = current.includes(segment) && !activeKeys.has(key);
        return <tr key={index} data-diverged={diverged || undefined}>
          <td>{name(source, segment.state)} · {segment.start.toFixed(3)}–{segment.end.toFixed(3)}{diverged ? ' · 当前帧分歧' : ''}</td>
          <td>进入 {timeList(times?.enter)}；退出 {timeList(times?.exit)}</td>
          <td>{segment.conditionDependencies.map(item => `${item.label}：${item.written ? '依赖条件写入，真实档按实际值' : '未写入，按默认值'}`).join('；') || '作者内容与按键日程'}</td>
        </tr>;
      })}</tbody></table>
  </Fold>;
}
