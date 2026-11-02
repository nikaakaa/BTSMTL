import { array, boolean, identity, nullable, number, object, opaque, text, type Decoder } from './playerRecords';
import type { SkillEditingView } from './authoring';

export const readQuickScenes = array(object({ publication: object({ path: text, revision: text }), scenePath: text, sceneGuid: text, sceneRevision: text }));
export const readPublishedTimelineScene = object({ path: text, guid: text, dependencyHash: text });
const document = object({ path: text, revision: text, savedRevision: nullable(text), hasUnsavedChanges: nullable(boolean) });
const source = object({ kind: text, identity: text, path: text, revision: text, sceneRevision: text, documents: array(document) });
const inputDeclaration = object({ inputId: text, kind: text, type: nullable(text) });
const inputPoint = object({ inputId: text, time: number, value: opaque });
const conditionPoint = object({ key: text, time: number, satisfied: boolean, restoreDefault: boolean });
const location = object({ graphId: text, nodeId: text, edgeId: text, displayPath: text });
const scheme = object({ index: number, name: text, revision: text, timelineId: nullable(text) });
const description = object({
  scenePath: text, carrierPath: text, carrierIdentity: text, tickRate: number,
  skills: array(object({ directory: text, displayName: text, identity: text })),
  timelines: array(object({ path: text, elementId: text, name: text, identity: text })),
  inputs: array(inputDeclaration), actionRequests: array(object({ inputId: text, bufferSeconds: number })),
  root: nullable(object({
    owner: text, label: text, machines: array(object({ index: number, operation: number, entryTimeline: nullable(text), entryTimelineId: nullable(text) })),
    inputSchemes: array(scheme), currentInputScheme: number, inputSchemeSource: text,
  })),
  source: nullable(source), note: text,
});
const plan = object({
  root: text, sourceType: text, clock: object({ unit: text, origin: number, owner: text, tickRate: number }),
  inputScheme: number, inputSchemeRevision: text, inputRevision: text, conditionsUsed: array(conditionPoint),
  assumptions: array(text), duration: number, stopReason: nullable(text), stopDetail: nullable(text), status: text,
  segments: array(object({
    timeline: nullable(text), timelineId: nullable(text), assetPath: nullable(text), state: text, stateSource: nullable(location),
    start: number, end: number, localStart: number, localEnd: number, playback: number, cycle: number,
    occurrence: number, playbackCompleted: boolean, callerPlayback: number, callerClipId: text,
  })),
  conditions: array(object({ key: text, label: text, source: text, operationIndex: number, undeterminable: boolean })),
  suggestions: array(object({
    fromState: text, toState: text, transition: text, fromSource: nullable(location), toSource: nullable(location), transitionSource: location,
    start: number, end: number, missing: array(object({ key: text, label: text })),
  })),
  declaredInputs: array(inputDeclaration), inputsUsed: array(inputPoint), inputSource: text,
});
const status = object({
  jobId: identity<'quick-job'>(), stage: text, completed: boolean, cancellationRequested: boolean,
  nativeMode: text, adoptedMode: nullable(text), source: nullable(source), samplesDone: number, samplesTotal: number,
  outputDirectory: nullable(text), summaryPath: nullable(text), plan: nullable(plan), error: nullable(text),
  cleanupErrors: array(text), notes: array(text), logErrors: array(text),
});
const reply = <T>(data: Decoder<T>) => object({ success: boolean, pending: boolean, code: nullable(text), message: nullable(text), data: nullable(data), rawJson: text });

export const readQuickDescriptionReply = reply(description);
export const readQuickStatusReply = reply(status);
export type PreviewQuickDescription = ReturnType<typeof description>;
export type PreviewQuickSource = ReturnType<typeof source>;
export type PreviewQuickPlan = ReturnType<typeof plan>;
export type PreviewQuickLocation = ReturnType<typeof location>;
export type PreviewQuickStatus = ReturnType<typeof status>;
export type PreviewQuickJobId = PreviewQuickStatus['jobId'];
export type PreviewQuickStatusReply = ReturnType<typeof readQuickStatusReply>;
export type PreviewQuickDescriptionReply = ReturnType<typeof readQuickDescriptionReply>;
export type PreviewQuickInputPoint = ReturnType<typeof inputPoint>;
export type PreviewQuickConditionPoint = ReturnType<typeof conditionPoint>;
export type PreviewQuickRoot = { kind: 'nativeDocument'; path: string; machine: number }
  | { kind: 'webEditingView'; skill: string; editingView: SkillEditingView; machine: number }
  | { kind: 'timelineAsset'; path: string; elementId: string };
export interface PreviewQuickTarget { scenePath: string; root?: PreviewQuickRoot }
export type PreviewQuickInputOverlay = { points: PreviewQuickInputPoint[]; conditions: PreviewQuickConditionPoint[] }
  | { points?: never; conditions?: never };
export type PreviewQuickInput = PreviewQuickInputOverlay & {
  scheme: number;
  revision: string;
};
export interface PreviewQuickRequest {
  mode: 'quick';
  target: PreviewQuickTarget & { root: PreviewQuickRoot };
  sourceRevision: string;
  input: PreviewQuickInput;
  outputDirectory: string;
  rangeEndSeconds?: number;
  timeoutSeconds?: number;
}
