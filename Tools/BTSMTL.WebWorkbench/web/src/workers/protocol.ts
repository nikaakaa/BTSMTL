import type { PlayerArchiveRange, PlayerFramesPage, PlayerJobId, PlayerObservationPage } from '../api/playerRecords';

export interface WorkerError {
  path: string;
  status: number;
  reason: string;
  raw: string;
}

export type WorkerRequest =
  | { type: 'frames'; id: number; jobId: PlayerJobId; offset: number; limit: number }
  | { type: 'observations'; id: number; jobId: PlayerJobId; range: PlayerArchiveRange }
  | { type: 'format'; id: number; text: string }
  | { type: 'cancel'; id: number };

export type WorkerResponse =
  | { type: 'frames'; id: number; page: PlayerFramesPage }
  | { type: 'observations'; id: number; page: PlayerObservationPage }
  | { type: 'format'; id: number; lines: string[] }
  | { type: 'error'; id: number; error: WorkerError };
