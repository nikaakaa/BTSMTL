import type { ObservationStreamEvent } from '../api/types';

export interface WorkerError {
  path: string;
  status: number;
  reason: string;
  raw: string;
}

export type WorkerRequest =
  | { type: 'fetch'; id: number; path: string }
  | { type: 'stream'; id: number; path: string; lastEventId: string | null }
  | { type: 'format'; id: number; text: string }
  | { type: 'cancel'; id: number };

export type WorkerResponse =
  | { type: 'result'; id: number; value: unknown }
  | { type: 'events'; id: number; events: ObservationStreamEvent[] }
  | { type: 'done'; id: number }
  | { type: 'error'; id: number; error: WorkerError };
