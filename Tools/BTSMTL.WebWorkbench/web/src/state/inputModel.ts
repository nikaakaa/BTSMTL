import { useMemo } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringDirectory } from '../api/authoring';
import { authoringDocument, currentDocument, timelinePath, useAuthoring, useInputSchemes } from './authoring';
import type { JsonObject } from './skillJson';
import { isRealArchive, useWorkbench } from './store';

export function useInputObservation(documentId: string | null) {
  const schemes = useInputSchemes();
  const scheme = schemes.find(item => item.documentId === documentId);
  const author = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    return { target: state.target, actorId: document.inputActorId, revision: document.inputRevision,
      edited: document.edited, files: document.files, removed: document.removed };
  }));
  const state = useWorkbench(useShallow(current => ({
    catalog: current.inputCatalog, frame: current.archiveFrame,
    runId: current.runId, hostFrame: current.hostFrame, realArchive: isRealArchive(current),
  })));
  const dirty = useMemo(() => {
    if (!scheme) return false;
    const paths = new Set([scheme.path]);
    for (const item of schemes.filter(item => item.path === scheme.path)) {
      paths.add(timelinePath(item.documentId));
      const timeline = currentDocument(timelinePath(item.documentId)) as JsonObject;
      for (const track of timeline.m_Tracks as JsonObject[])
        for (const clip of [...track.m_Clips as JsonObject[], ...track.m_Markers as JsonObject[]])
          paths.add(`graphs/${(clip.m_AssetTree ?? clip.m_Graph) as string}.json`);
    }
    return [...paths].some(path => path in author.edited || author.removed.includes(path));
  }, [scheme, schemes, author.edited, author.files, author.removed]);
  if (!scheme || !author.target) return { kind: 'unavailable', reason: '当前不是输入 Timeline' } as const;
  if (!state.runId) return { kind: 'unavailable', reason: '尚无输入档案' } as const;
  if (dirty) return { kind: 'stale', reason: '输入方案有未保存修改，旧观察层已过期' } as const;
  const actorId = scheme.actorId || author.actorId;
  if (!actorId && !state.realArchive) return { kind: 'unavailable', reason: '先明确输入角色' } as const;
  if (author.revision.kind === 'rejected') return { kind: 'stale', reason: author.revision.reason } as const;
  const frame = state.frame;
  if (!frame || frame.hostFrame !== state.hostFrame)
    return { kind: 'unavailable', reason: '正在读取当前帧输入' } as const;
  const context = frame.inputContext;
  if (context === null) return { kind: 'unavailable', reason: '当前档案没有输入方案来源信息' } as const;
  const catalog = state.catalog;
  if (catalog === null) return { kind: 'unavailable', reason: '正在读取场景输入声明' } as const;
  if (state.realArchive && catalog.revision !== frame.logic?.source.sceneRevision)
    return { kind: 'stale', reason: '当前场景声明与录制采用的场景修订不一致' } as const;
  const target = author.target, directory = authoringDirectory(target);
  const savedRevision = author.revision.kind === 'catalog' ? null : author.revision.items.find(item => item.planId === scheme.planId);
  const candidates = catalog.players.filter(actor => !actorId || actor.actorId === actorId).map(actor => {
    const plan = actor.localPlans.find(item => item.id === scheme.planId && item.name === directory &&
      (target.kind === 'scenario' ? item.kind === 'scene' : item.kind !== 'scene'));
    const revision = savedRevision === null ? plan : savedRevision;
    const recorded = context.plans.find(item => item.actorId === actor.actorId && item.plan === revision?.path &&
      item.schemeIndex === scheme.schemeIndex);
    return { actor, revision, recorded };
  });
  const matching = actorId ? candidates : candidates.filter(item => item.recorded !== undefined &&
    item.recorded.planRevision === item.revision?.revision);
  if (matching.length !== 1) return { kind: 'unavailable', reason: actorId ? '当前场景输入声明没有该角色'
    : matching.length === 0 ? '当前档案没有采用此输入方案和修订的角色' : '当前档案有多个角色采用此方案，请明确输入角色' } as const;
  const { actor, revision, recorded } = matching[0];
  if (!revision) return { kind: 'unavailable', reason: '尚未取得本方案的正式输入修订' } as const;
  if (!recorded) return { kind: 'stale', reason: '当前档案没有采用此角色的输入方案' } as const;
  if (recorded.planRevision !== revision.revision)
    return { kind: 'stale', reason: '输入修订不一致，旧观察层已过期' } as const;
  const record = frame.input, input = record?.actors.find(item => item.actorId === actor.actorId);
  if (!record || !input) return { kind: 'unavailable', reason: frame.availability.find(item => item.channel === 'input' && !item.available)?.reason
    ?? '当前帧没有该角色的原生输入记录' } as const;
  if (context.inputTimeOriginSeconds === null || context.inputTimeStepSeconds === null)
    return { kind: 'unavailable', reason: context.inputTimeUnavailableReason ?? '宿主未提供正式输入时间' } as const;
  const observation = { frame, input, plan: recorded, declarations: actor, source: record.source,
    originSeconds: context.inputTimeOriginSeconds, stepSeconds: context.inputTimeStepSeconds,
    valueEncoding: context.valueEncoding,
  };
  return record.inputTimeSeconds === null ? { ...observation, kind: 'before-start', reason: '输入方案尚未开始' } as const
    : { ...observation, kind: 'ready', seconds: record.inputTimeSeconds } as const;
}

export type InputObservation = ReturnType<typeof useInputObservation>;
