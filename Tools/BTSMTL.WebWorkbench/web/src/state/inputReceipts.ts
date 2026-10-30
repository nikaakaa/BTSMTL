import { useEffect, useState } from 'react';
import { playerApi } from '../api/player';
import type { PlayerArchive, PlayerJobId, readPlayerInput, readPlayerInputCatalog } from '../api/playerRecords';

type InputPage = ReturnType<typeof readPlayerInput>;
type ActorInput = InputPage['page']['items'][number]['actors'][number];
type InputValue = ActorInput['values'][number];
type KeyDeclaration = ReturnType<typeof readPlayerInputCatalog>['players'][number]['keys'][number];

// 档案输入通道整段还原到方案时间：每个按键在哪些时段收到了非中性值，哪些帧收到了新请求；
// 未收到的帧单列。时间按日程起点帧换算，与输入 Timeline 同一坐标。
export interface ReceivedSpan { start: number; end: number; label: string }
export interface ReceivedKey { name: string; spans: ReceivedSpan[]; requests: ReceivedSpan[] }
export type ReceivedInput =
  | { kind: 'loading' }
  | { kind: 'failed'; reason: string }
  | { kind: 'ready'; keys: ReceivedKey[]; missing: ReceivedSpan[] };

type FrameInput = { frame: number; input: ActorInput };
const cache = new Map<string, Promise<FrameInput[]>>();
const cacheLimit = 8;

function frames(jobId: PlayerJobId, actorId: string, metadata: PlayerArchive) {
  const id = `${jobId}:${actorId}`;
  let request = cache.get(id);
  if (!request) {
    request = (async () => {
      const result: FrameInput[] = [];
      let offset: number | null = 0;
      while (offset !== null) {
        const page: InputPage = await playerApi.input(jobId, { firstSdkFrame: metadata.firstRecordedSdkFrame,
          lastSdkFrame: metadata.lastRecordedSdkFrame, offset, limit: 128 });
        for (const item of page.page.items) {
          const input = item.actors.find(actor => actor.actorId === actorId);
          if (input) result.push({ frame: Number(item.source.sdkFrame), input });
        }
        offset = page.page.nextOffset;
      }
      return result;
    })();
    request.catch(() => cache.delete(id));
    cache.set(id, request);
    while (cache.size > cacheLimit) for (const oldest of cache.keys()) { cache.delete(oldest); break; }
  }
  return request;
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

function spans(records: FrameInput[], keys: KeyDeclaration[], metadata: PlayerArchive, scheduleStartFrame: number) {
  const tick = 1 / metadata.sdkTickRate;
  const at = (frame: number) => (frame - scheduleStartFrame) / metadata.sdkTickRate;
  const extend = (list: ReceivedSpan[], start: number, label: string) => {
    const last = list.at(-1);
    if (last && last.label === label && Math.abs(last.end - start) < tick / 2) last.end = start + tick;
    else list.push({ start, end: start + tick, label });
  };
  const result = keys.map(key => ({ name: key.name, spans: [] as ReceivedSpan[], requests: [] as ReceivedSpan[] }));
  const missing: ReceivedSpan[] = [];
  const seen = new Set<string>();
  for (const { frame, input: record } of records) {
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
  return { keys: result.filter(key => key.spans.length || key.requests.length), missing };
}

export function useReceivedInput(source: { jobId: PlayerJobId; actorId: string; metadata: PlayerArchive;
  keys: KeyDeclaration[] } | null): ReceivedInput | null {
  const [state, setState] = useState<{ id: string; value: ReceivedInput } | null>(null);
  const id = source ? `${source.jobId}:${source.actorId}` : null;
  useEffect(() => {
    if (!source || !id) return;
    let live = true;
    setState({ id, value: { kind: 'loading' } });
    frames(source.jobId, source.actorId, source.metadata).then(records => {
      if (live) setState({ id, value: { kind: 'ready',
        ...spans(records, source.keys, source.metadata, source.metadata.scheduleStartFrame) } });
    }, (error: unknown) => {
      if (live) setState({ id, value: { kind: 'failed', reason: error instanceof Error ? error.message : String(error) } });
    });
    return () => { live = false; };
  }, [id, source?.keys, source?.metadata]);
  return id && state?.id === id ? state.value : null;
}
