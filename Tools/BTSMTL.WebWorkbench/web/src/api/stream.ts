import { ApiError } from './http';

export interface ServerEvent {
  event: string;
  id: string | null;
  data: string;
}

// 用 fetch 读取 SSE：可取消、可带 Last-Event-ID 续读，在 Worker 中同样可用。
export async function readServerEvents(path: string, signal: AbortSignal, lastEventId: string | null,
  onEvent: (event: ServerEvent) => void): Promise<void> {
  const headers: Record<string, string> = { Accept: 'text/event-stream' };
  if (lastEventId) headers['Last-Event-ID'] = lastEventId;
  const response = await fetch(path, { headers, signal });
  if (!response.ok || !response.body) {
    const raw = await response.text();
    throw new ApiError(path, response.status, raw.trim() || `HTTP ${response.status}`, raw);
  }
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  let event = 'message';
  let id: string | null = null;
  let data: string[] = [];
  for (;;) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += value;
    let newline: number;
    while ((newline = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, newline).replace(/\r$/, '');
      buffer = buffer.slice(newline + 1);
      if (line === '') {
        if (data.length) onEvent({ event, id, data: data.join('\n') });
        event = 'message';
        id = null;
        data = [];
      } else if (!line.startsWith(':')) {
        const colon = line.indexOf(':');
        const field = colon < 0 ? line : line.slice(0, colon);
        const text = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '');
        if (field === 'event') event = text;
        else if (field === 'id') id = text;
        else if (field === 'data') data.push(text);
      }
    }
  }
}
