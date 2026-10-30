import { useMemo } from 'react';
import type { PlayerArchive, PlayerJobId, readPlayerInputCatalog } from '../api/playerRecords';
import { archive } from './archive';
import { scheduleZeroFrame } from './inputModel';
import { useWorkbench } from './store';

type FrameInput = ReturnType<typeof archive.inputFrames>[number];
type InputValue = FrameInput['input']['values'][number];
type KeyDeclaration = ReturnType<typeof readPlayerInputCatalog>['players'][number]['keys'][number];

export interface ReceivedSpan { start: number; end: number; label: string }
export interface ReceivedKey { name: string; spans: ReceivedSpan[]; requests: ReceivedSpan[] }
export interface ReceivedInput {
  keys: ReceivedKey[];
  missing: ReceivedSpan[];
  ranges: { first: string; last: string }[];
}

function neutral(value: InputValue) {
  const runtime = value.runtimeValue;
  if (runtime === null) return !value.value || value.value.every(component => component === 0);
  switch (runtime.kind) {
    case 'none': return true;
    case 'boolean': return !runtime.value;
    case 'int32': return runtime.value === 0;
    case 'uint64': return runtime.value === '0';
    case 'scalar': case 'yaw': return BigInt(runtime.raw) === 0n;
    case 'vector2': case 'vector3': case 'quaternion': return runtime.raw.every(component => BigInt(component) === 0n);
    default: return false;
  }
}

const shown = (value: InputValue) => value.runtimeValue?.kind === 'boolean' || (value.value?.length === 1 && value.value[0] === 1)
  ? value.inputId : `${value.inputId} ${JSON.stringify(value.value ?? value.runtimeValue)}`;

function spans(records: FrameInput[], keys: KeyDeclaration[], metadata: PlayerArchive) {
  const tick = 1 / metadata.sdkTickRate;
  const zeroFrame = BigInt(scheduleZeroFrame(metadata));
  const at = (frame: string) => Number(BigInt(frame) - zeroFrame) / metadata.sdkTickRate;
  const extend = (list: ReceivedSpan[], start: number, label: string) => {
    const last = list.at(-1);
    if (last && last.label === label && Math.abs(last.end - start) < tick / 2) last.end = start + tick;
    else list.push({ start, end: start + tick, label });
  };
  const result = keys.map(key => ({ name: key.name, spans: [] as ReceivedSpan[], requests: [] as ReceivedSpan[] }));
  const missing: ReceivedSpan[] = [];
  const ranges: ReceivedInput['ranges'] = [];
  const seen = new Set<string>();
  for (const { frame, input: record } of records) {
    const last = ranges.at(-1);
    if (last && BigInt(last.last) + 1n === BigInt(frame)) last.last = frame;
    else ranges.push({ first: frame, last: frame });
    const start = at(frame);
    if (!record.received) { extend(missing, start, '未收到输入'); continue; }
    keys.forEach((key, index) => {
      const active = record.values.filter(value => key.valueIds.includes(value.inputId) && !neutral(value));
      if (active.length) extend(result[index].spans, start, active.map(shown).join(' · '));
      for (const request of record.requests)
        if (key.requestIds.includes(request.requestId) && !seen.has(`${request.requestId}/${request.sequence}`)) {
          seen.add(`${request.requestId}/${request.sequence}`);
          result[index].requests.push({ start, end: start + tick, label: `${request.requestId} #${request.sequence}` });
        }
    });
  }
  return { keys: result.filter(key => key.spans.length || key.requests.length), missing, ranges };
}

export function useReceivedInput(source: { jobId: PlayerJobId; actorId: string; metadata: PlayerArchive;
  keys: KeyDeclaration[] } | null): ReceivedInput | null {
  const frame = useWorkbench(state => state.archiveFrame);
  return useMemo(() => source === null ? null
    : spans(archive.inputFrames(source.jobId, source.actorId), source.keys, source.metadata), [source, frame]);
}
