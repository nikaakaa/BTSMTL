import { getJson, postJson, query } from './http';
import type {
  CancelResult, ControlRequest, ControlResult, Frame, Geometry, GraphPage, GraphQuery,
  HostAvailability, HostRecord, InputCatalog, InputTimeline, ObservationPage, ObservationQuery, Page,
  PlaybackView, Route, RoutePage, RunReceipt, Sources, StartRunRequest, Summary, TimelinePage,
} from './types';

const base = '/api/preview';
const run = (id: string) => `${base}/runs/${encodeURIComponent(id)}`;

export const previewApi = {
  host: (signal?: AbortSignal) => getJson<HostAvailability>(`${base}/host`, signal),
  sources: (signal?: AbortSignal) => getJson<Sources>(`${base}/sources`, signal),
  inputs: (scene: string, revision: string, signal?: AbortSignal) =>
    getJson<InputCatalog>(`${base}/inputs${query({ scene, revision })}`, signal),

  runs: (signal?: AbortSignal) => getJson<RunReceipt[]>(`${base}/runs`, signal),
  run: (id: string, signal?: AbortSignal) => getJson<RunReceipt>(run(id), signal),
  start: (request: StartRunRequest, signal?: AbortSignal) => postJson<RunReceipt>(`${base}/runs`, request, signal),
  cancel: (id: string, signal?: AbortSignal) => postJson<CancelResult>(`${run(id)}/cancel`, {}, signal),

  playback: (id: string, signal?: AbortSignal) => getJson<PlaybackView>(`${run(id)}/playback`, signal),
  control: (id: string, request: ControlRequest, signal?: AbortSignal) =>
    postJson<ControlResult>(`${run(id)}/controls`, request, signal),
  readControl: (id: string, commandId: string, signal?: AbortSignal) =>
    getJson<ControlResult>(`${run(id)}/controls/${encodeURIComponent(commandId)}`, signal),

  summary: (id: string, signal?: AbortSignal) => getJson<Summary>(`${run(id)}/summary`, signal),
  frames: (id: string, offset: number, limit: number, signal?: AbortSignal) =>
    getJson<Page<Frame>>(`${run(id)}/frames${query({ offset, limit })}`, signal),
  geometry: (id: string, signal?: AbortSignal) => getJson<Geometry>(`${run(id)}/geometry`, signal),

  inputTimeline: (id: string, signal?: AbortSignal) => getJson<InputTimeline>(`${run(id)}/input-timeline`, signal),
  timelines: (id: string, offset: number, limit: number, signal?: AbortSignal) =>
    getJson<TimelinePage>(`${run(id)}/timelines${query({ offset, limit })}`, signal),

  route: (id: string, signal?: AbortSignal) => getJson<Route>(`${run(id)}/route`, signal),
  routeFrames: (id: string, range: ObservationQuery & { offset: number; limit: number }, signal?: AbortSignal) =>
    getJson<RoutePage>(`${run(id)}/route/frames${query({ ...range })}`, signal),
  graphs: (id: string, graph: GraphQuery, signal?: AbortSignal) =>
    getJson<GraphPage>(`${run(id)}/graphs${query({ ...graph })}`, signal),

  observations: (id: string, range: ObservationQuery & { offset: number; limit: number }, signal?: AbortSignal) =>
    getJson<ObservationPage>(`${run(id)}/observations${query({ ...range })}`, signal),
  observationStreamPath: (id: string, range: ObservationQuery) => `${run(id)}/observations/stream${query({ ...range })}`,

  sourceMap: (id: string, offset: number, limit: number, signal?: AbortSignal) =>
    getJson<Page<HostRecord>>(`${run(id)}/source-map${query({ offset, limit })}`, signal),
};
