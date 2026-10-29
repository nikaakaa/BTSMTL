import { getJson, postJson } from '../../api/http';

export interface WallVector { x: number; y: number; z: number }
export interface WallPlane { enabled: boolean; visible: boolean; position: WallVector; rotation: WallVector; size: WallVector }
export interface WallTarget { graphId: string; nodeId: string; instanceId: string; graphRevision: string }
export interface WallValue { id: string; value: string }
export interface WallField extends WallValue { displayName: string; group: string; kind: 'float' | 'integer' | 'boolean' }
export interface WallHand {
  phase: string; result: string; hasSolution: boolean; armWeight: number; wristWeight: number; fingerWeight: number;
  positionResidual: number; palmAngle: number; minimumClearance: number; fingerUp: number; elbowAngle: number;
  forearmTwist: number; animatedElbowAngle: number; jointLimitExcess: number; jointRotationResidual: number;
  shoulderAngles: WallVector; wristAngles: WallVector; animatedWristAngles: WallVector;
  handPosition: WallVector; elbowPosition: WallVector; targetPosition: WallVector;
}
export interface WallSnapshot {
  loadCommandId: string; actorId: string; sourceAssetId: string; requestedVersion: string; appliedVersion: string;
  completionIdentity: string; targets: WallTarget[]; selected: number; fields: WallField[];
  left: WallHand | null; right: WallHand | null; wall: WallPlane;
}
export interface WallSource {
  sourceAssetId: string; assetPath: string; graphId: string; nodeId: string; revision: string; saved: boolean; values: WallValue[];
}
export interface WallSourceTarget { sourceAssetId: string; graphId: string; nodeId: string }
type WallCommand = { type: 'wall-contact'; commandId: string; focusActorId: string } & (
  | { operation: 'read'; graphId?: string; nodeId?: string; instanceId?: string; graphRevision?: string }
  | ({ operation: 'observe'; loadCommandId: string } & WallTarget)
  | ({ operation: 'apply'; loadCommandId: string; expectedParameterVersion: string; values: WallValue[] } & WallTarget)
  | ({ operation: 'wall'; loadCommandId: string; wall: WallPlane } & WallTarget)
);

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('扶墙回执应为对象');
  return value as Record<string, unknown>;
}
function text(value: unknown): string {
  if (typeof value !== 'string') throw new Error('扶墙回执缺少文本字段');
  return value;
}
function number(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error('扶墙回执缺少有限数值');
  return value;
}
function flag(value: unknown): boolean {
  if (typeof value !== 'boolean') throw new Error('扶墙回执缺少布尔字段');
  return value;
}
function array(value: unknown): unknown[] {
  if (!Array.isArray(value)) throw new Error('扶墙回执缺少数组');
  return value;
}
function vector(value: unknown): WallVector {
  const item = object(value);
  return { x: number(item.x), y: number(item.y), z: number(item.z) };
}
function target(value: unknown): WallTarget {
  const item = object(value);
  return { graphId: text(item.graphId), nodeId: text(item.nodeId), instanceId: text(item.instanceId), graphRevision: text(item.graphRevision) };
}
function parameter(value: unknown): WallValue {
  const item = object(value);
  return { id: text(item.id), value: text(item.value) };
}
function field(value: unknown): WallField {
  const item = object(value);
  const kind = item.kind;
  if (kind !== 'float' && kind !== 'integer' && kind !== 'boolean') throw new Error(`不支持的扶墙参数类型：${String(kind)}`);
  return { id: text(item.id), value: text(item.value), displayName: text(item.displayName), group: text(item.group), kind };
}
function hand(value: unknown): WallHand | null {
  if (value === null) return null;
  const item = object(value);
  return {
    phase: text(item.phase), result: text(item.result), hasSolution: flag(item.hasSolution),
    armWeight: number(item.armWeight), wristWeight: number(item.wristWeight), fingerWeight: number(item.fingerWeight),
    positionResidual: number(item.positionResidual), palmAngle: number(item.palmAngle), minimumClearance: number(item.minimumClearance),
    fingerUp: number(item.fingerUp), elbowAngle: number(item.elbowAngle), forearmTwist: number(item.forearmTwist),
    animatedElbowAngle: number(item.animatedElbowAngle), jointLimitExcess: number(item.jointLimitExcess),
    jointRotationResidual: number(item.jointRotationResidual), shoulderAngles: vector(item.shoulderAngles),
    wristAngles: vector(item.wristAngles), animatedWristAngles: vector(item.animatedWristAngles),
    handPosition: vector(item.handPosition), elbowPosition: vector(item.elbowPosition), targetPosition: vector(item.targetPosition),
  };
}
function snapshot(value: unknown): WallSnapshot {
  const item = object(value);
  const plane = object(item.wall);
  const targets = array(item.targets).map(target);
  const selected = number(item.selected);
  if (!Number.isInteger(selected) || selected < 0 || selected >= targets.length) throw new Error('扶墙节点选择超出回执范围');
  const completionIdentity = text(item.completionIdentity);
  return {
    loadCommandId: text(item.loadCommandId), actorId: text(item.actorId), sourceAssetId: text(item.sourceAssetId),
    requestedVersion: text(item.requestedVersion), appliedVersion: text(item.appliedVersion), completionIdentity,
    targets, selected, fields: array(item.fields).map(field),
    left: completionIdentity.length ? hand(item.left) : null, right: completionIdentity.length ? hand(item.right) : null,
    wall: { enabled: flag(plane.enabled), visible: flag(plane.visible), position: vector(plane.position), rotation: vector(plane.rotation), size: vector(plane.size) },
  };
}
function source(value: unknown): WallSource {
  const item = object(value);
  return {
    sourceAssetId: text(item.sourceAssetId), assetPath: text(item.assetPath), graphId: text(item.graphId), nodeId: text(item.nodeId),
    revision: text(item.revision), saved: flag(item.saved), values: array(item.values).map(parameter),
  };
}

export class PendingWallCommand extends Error {
  constructor(readonly commandId: string, reason: string) { super(reason); }
}

async function receipt(value: unknown, commandId: string): Promise<WallSnapshot> {
  const result = object(value);
  if (result.stage === 'submitted') throw new PendingWallCommand(commandId, '命令已提交，尚未收到 Player 回执，请重查原命令');
  if (result.stage === 'not-applied') throw new Error('原命令没有进入 Player，可以重新操作');
  if (result.stage !== 'applied') {
    const ack = object(result.ack);
    throw new Error(text(ack.error));
  }
  return snapshot(object(result.ack).wallContact);
}

export const wallContactApi = {
  status(rawJson: string): WallSnapshot | null {
    const status = object(JSON.parse(rawJson));
    if (typeof status.wallContactAvailable !== 'boolean')
      throw new Error('当前 Player 尚未提供扶墙结果推送，请载入包含扶墙调节的新版 Player');
    return status.wallContactAvailable ? snapshot(status.wallContact) : null;
  },
  async command(liveId: string, command: WallCommand): Promise<WallSnapshot> {
    let response: unknown;
    try {
      response = await postJson<unknown>(`/api/preview/live/${encodeURIComponent(liveId)}/commands`, command);
    } catch (error) {
      throw new PendingWallCommand(command.commandId, `未确认 Player 结果，请重查原命令：${error instanceof Error ? error.message : String(error)}`);
    }
    return receipt(response, command.commandId);
  },
  async recheck(liveId: string, commandId: string): Promise<WallSnapshot> {
    return receipt(await getJson<unknown>(`/api/preview/live/${encodeURIComponent(liveId)}/commands/${encodeURIComponent(commandId)}`), commandId);
  },
  async readSource(input: WallSourceTarget): Promise<WallSource> {
    return source(await postJson<unknown>('/api/authoring/wall-contact/read', input));
  },
  async saveSource(input: { target: WallSourceTarget; expectedRevision: string; values: WallValue[] }): Promise<WallSource> {
    return source(await postJson<unknown>('/api/authoring/wall-contact/save', input));
  },
};
