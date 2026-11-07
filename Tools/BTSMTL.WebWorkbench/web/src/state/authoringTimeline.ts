// Timeline 作者查询：轨道可放片段、曲线通道、吸附与参考曲线、换轨与标记占用；只读当前（可能未保存的）文档。
import { create } from 'zustand';
import { authoringApi, type ClipCurveChannel, type ClipTemplate, type ReferenceCurves, type SkillElements, type TrackOverlap } from '../api/authoring';
import { authoringActions, currentDocument, findTimelineClip, get, markersCollide, timelinePath, tracksOf } from './authoring';
import { JsonFloat, fpRaw, fpSeconds, getIn, numberOf, type JsonObject } from './skillJson';
import { consoleActions } from './store';

export function markersTaken(documentId: string, markerIds: ReadonlySet<string>, deltaSeconds: number): boolean {
  const document = currentDocument(timelinePath(documentId));
  return !!document && markersCollide(document, markerIds, fpRaw(deltaSeconds));
}

// 轨道的正式合同与当前（可能未保存的）执行域；轨道种类不在元素查询里时为 null。
function trackPlacement(elements: SkillElements, documentId: string, trackId: string) {
  const document = currentDocument(timelinePath(documentId));
  const track = document ? tracksOf(document).find(item => item.m_AuthoringId === trackId) : undefined;
  const template = track && elements.tracks.find(item => item.kind === track.ContractKind);
  return template ? { template, domain: track!.m_ExecutionDomain as string } : null;
}

export const trackOverlap = (elements: SkillElements, documentId: string, trackId: string): TrackOverlap | null =>
  trackPlacement(elements, documentId, trackId)?.template.contract.overlap ?? null;

// 轨道可放的片段种类（照 TimelineDefinitionCatalog.RequireClipPlacement）：轨道合同允许、片段属于这种轨道，
// 且轨道与片段都支持轨道的执行域。
export function clipTemplatesFor(elements: SkillElements, documentId: string, trackId: string): ClipTemplate[] {
  const placement = trackPlacement(elements, documentId, trackId);
  if (!placement) return [];
  const { template, domain } = placement;
  return elements.clips.filter(clip => template.contract.clipKinds.includes(clip.kind) && clip.contract.trackKind === template.kind &&
    template.contract.domains.includes(domain) && clip.contract.domains.includes(domain));
}

// 轨道能放带曲线通道或参考曲线的片段时可展开曲线。
export const trackHasCurves = (elements: SkillElements, documentId: string, trackId: string) =>
  clipTemplatesFor(elements, documentId, trackId).some(template => template.curves.length > 0 || template.referenceCurves !== null);

// 片段各曲线通道的当前曲线；字段未设置（null）时 curve 为 null，运行时该通道不起作用。
export function clipCurves(elements: SkillElements, documentId: string, clipId: string): { channel: ClipCurveChannel; curve: JsonObject | null }[] {
  const document = currentDocument(timelinePath(documentId));
  const clip = document ? findTimelineClip(document as JsonObject, clipId) : null;
  const template = clip && elements.clips.find(item => item.kind === clip.ContractKind);
  if (!clip || !template) return [];
  return template.curves.map(channel => {
    const value = clip[channel.member];
    return { channel, curve: value && typeof value === 'object' && Array.isArray((value as JsonObject).keys) ? value as JsonObject : null };
  });
}

// 片段吸附所需的作者数据：合同模板、起点与 ClipIn 秒数、片段直接引用的资源（"guid:localFileId" 字符串字段）。
export function clipSnapSource(elements: SkillElements, documentId: string, clipId: string) {
  const document = currentDocument(timelinePath(documentId));
  const clip = document ? findTimelineClip(document as JsonObject, clipId) : null;
  const template = clip && elements.clips.find(item => item.kind === clip.ContractKind);
  if (!clip || !template) return null;
  const source = template.sourceAsset ? { ...template.sourceAsset, value: getIn(clip, template.sourceAsset.serializedMemberPath) } : null;
  const references = source?.choiceKind === 'animation-clip' && typeof source.value === 'string' ? [source.value] : [];
  return {
    template, source, references, start: fpSeconds(numberOf(clip.m_StartTimeFPRawValue)), clipIn: fpSeconds(numberOf(clip.m_ClipInTimeFPRawValue ?? 0)),
  };
}

// 片段的参考曲线源：模板 referenceCurves 成员引用的资源身份与片段的源区间（SampledClip 源起止秒数）。
export function clipReferenceSource(elements: SkillElements, documentId: string, clipId: string) {
  const document = currentDocument(timelinePath(documentId));
  const clip = document ? findTimelineClip(document as JsonObject, clipId) : null;
  const member = clip && elements.clips.find(item => item.kind === clip.ContractKind)?.referenceCurves;
  const identity = member ? clip[member] : null;
  return typeof identity === 'string'
    ? { identity, sourceStart: numberOf(clip!.m_SourceStartTime), sourceEnd: numberOf(clip!.m_SourceEndTime) } : null;
}

// 参考曲线按资源身份缓存（源资产只在 Unity 里改，刷新网页重读）；读失败记一次控制台错误。
export type ReferenceEntry = ReferenceCurves | { failed: string };
export const useReferenceCurves = create<Record<string, ReferenceEntry>>(() => ({}));
const referenceRequests = new Set<string>();
export function requestReferenceCurves(identity: string) {
  const target = get().target;
  if (!target || referenceRequests.has(identity)) return;
  referenceRequests.add(identity);
  authoringApi.referenceCurves(target, identity, authoringActions.editingView()).then(result => useReferenceCurves.setState({ [identity]: result }), (error: unknown) => {
    consoleActions.error('作者', error);
    useReferenceCurves.setState({ [identity]: { failed: error instanceof Error ? error.message : String(error) } });
  });
}

// 通道的默认曲线（两端线性切线，非权重），与 Unity 目录的 AnimationCurve.Linear 相同。
export function defaultChannelCurve(channel: ClipCurveChannel): JsonObject {
  const tangent = channel.defaultEnd - channel.defaultStart;
  const key = (time: number, value: number): JsonObject => ({
    inTangent: new JsonFloat(tangent), inWeight: new JsonFloat(0), outTangent: new JsonFloat(tangent), outWeight: new JsonFloat(0),
    tangentMode: 0, time: new JsonFloat(time), value: new JsonFloat(value), weightedMode: 0,
  });
  return { keys: [key(0, channel.defaultStart), key(1, channel.defaultEnd)], postWrapMode: 8, preWrapMode: 8 };
}

// 片段换轨（照 TimelineData.MoveClip）：目标轨道能放这种片段，且执行域与原轨道相同；可以时为 null，否则给出原因。
export function clipMoveIssue(elements: SkillElements, documentId: string, clipKind: string, fromTrackId: string, toTrackId: string): string | null {
  const from = trackPlacement(elements, documentId, fromTrackId), to = trackPlacement(elements, documentId, toTrackId);
  if (!from || !to) return '轨道种类不在元素查询里';
  if (!clipTemplatesFor(elements, documentId, toTrackId).some(template => template.kind === clipKind))
    return `${to.template.kind} 轨道不能放 ${clipKind} 片段`;
  return from.domain === to.domain ? null : `跨轨道移动必须保持执行域（${from.domain} → ${to.domain}）`;
}
