import { array, boolean, choice, identity, nullable, number, object, opaque, text, type Decoder } from './playerRecords';
import type { WorkspaceEditingView } from './workspaceDocuments';

export const readQuickScenes = array(object({ publication: object({ path: text, revision: text }), scenePath: text, sceneGuid: text, sceneRevision: text }));
export const readPublishedTimelineScene = object({ path: text, guid: text, dependencyHash: text });
const entry = (value: unknown, path = 'PreviewQuick.entry') => {
  const { kind } = object({ kind: choice('graph', 'timeline') })(value, path);
  switch (kind) {
    case 'graph': return { kind, ...object({ graphId: text, machine: number })(value, path) };
    case 'timeline': return { kind, ...object({ timelineId: text })(value, path) };
  }
};
const authoring = object({ documentPath: text, savedRevision: text, entry });
const document = object({ kind: text, documentPath: text, identity: text, revision: text, savedRevision: text, hasUnsavedChanges: boolean });
const source = object({ kind: text, authoring, revision: text, sceneRevision: text, documents: array(document) });
const inputSource = (value: unknown, path = 'PreviewQuick.input.source') => {
  const { kind } = object({ kind: choice('documentInput', 'rootDefaults') })(value, path);
  switch (kind) {
    case 'documentInput': return { kind, ...object({ documentPath: text, savedRevision: text, inputId: text })(value, path) };
    case 'rootDefaults': return { kind };
  }
};
const inputDeclaration = object({ inputId: text, kind: text, type: nullable(text) });
const inputPoint = object({ inputId: text, time: number, value: opaque });
const conditionPoint = object({ key: text, time: number, satisfied: boolean, restoreDefault: boolean });
const location = object({ documentPath: text, graphId: text, nodeId: text, edgeId: text, displayPath: text });
const scheme = object({ source: inputSource, name: text, revision: text, timelineId: nullable(text) });
const description = object({
  scenePath: text, carrierPath: text, carrierIdentity: text, tickRate: number,
  documents: array(object({ kind: text, documentPath: text, identity: text, displayName: text, savedRevision: text,
    entries: array(object({ entry, displayName: text })) })),
  inputs: array(inputDeclaration), actionRequests: array(object({ inputId: text, bufferSeconds: number })),
  root: nullable(object({
    authoring, label: text, machines: array(object({ authoring, operation: number, entryTimeline: nullable(text), entryTimelineId: nullable(text) })),
    inputSchemes: array(scheme), currentInputScheme: inputSource,
  })),
  source: nullable(source), note: text,
});
const plan = object({
  root: text, sourceType: text, clock: object({ unit: text, origin: number, owner: text, tickRate: number }),
  input: inputSource, inputSchemeRevision: text, inputRevision: text, conditionsUsed: array(conditionPoint),
  assumptions: array(text), duration: number, stopReason: nullable(text), stopDetail: nullable(text), status: text,
  segments: array(object({
    timeline: nullable(text), timelineId: nullable(text), documentPath: nullable(text), state: text, stateSource: nullable(location),
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
export type PreviewQuickEntry = ReturnType<typeof entry>;
export type PreviewQuickAuthoringTarget = ReturnType<typeof authoring>;
export type PreviewQuickInputSource = ReturnType<typeof inputSource>;
export type PreviewQuickRoot = { kind: 'nativeDocument'; authoring: PreviewQuickAuthoringTarget }
  | { kind: 'webEditingView'; authoring: PreviewQuickAuthoringTarget; editingView: WorkspaceEditingView }
  | { kind: 'authoringDocument'; authoring: PreviewQuickAuthoringTarget };
export interface PreviewQuickTarget { scenePath: string; root: PreviewQuickRoot | null }
export type PreviewQuickInputOverlay = { points: PreviewQuickInputPoint[]; conditions: PreviewQuickConditionPoint[] }
  | { points?: never; conditions?: never };
export type PreviewQuickInput = PreviewQuickInputOverlay & {
  source: PreviewQuickInputSource;
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
