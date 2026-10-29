export class ApiError extends Error {
  readonly status: number;
  readonly reason: string;
  readonly raw: string;
  readonly path: string;

  constructor(path: string, status: number, reason: string, raw: string) {
    super(reason);
    this.name = 'ApiError';
    this.path = path;
    this.status = status;
    this.reason = reason;
    this.raw = raw;
  }
}

// 宿主错误体为 { error } 或 { code, message }；其余原样作为原因。
function reasonOf(status: number, raw: string): string {
  try {
    const body: unknown = JSON.parse(raw);
    if (body && typeof body === 'object') {
      const record = body as Record<string, unknown>;
      for (const key of ['error', 'message', 'detail', 'title'])
        if (typeof record[key] === 'string' && record[key]) return record[key] as string;
    }
  } catch {
    // 非 JSON 回执按文本显示。
  }
  return raw.trim() || `HTTP ${status}`;
}

async function send(path: string, init: RequestInit): Promise<Response> {
  let response: Response;
  try {
    response = await fetch(path, init);
  } catch (error) {
    if (init.signal?.aborted) throw error;
    throw new ApiError(path, 0, '宿主未响应', error instanceof Error ? error.message : String(error));
  }
  if (!response.ok) {
    const raw = await response.text();
    throw new ApiError(path, response.status, reasonOf(response.status, raw), raw);
  }
  return response;
}

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  return (await send(path, { signal })).json() as Promise<T>;
}

export async function postJson<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const response = await send(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });
  return response.json() as Promise<T>;
}

// 回执原文：作者 JSON 需要保留浮点写法，由调用方按技能 JSON 规则解析。
export async function postText(path: string, body: unknown, signal?: AbortSignal): Promise<string> {
  const response = await send(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  });
  return response.text();
}

export async function getBlob(path: string, signal?: AbortSignal): Promise<Response> {
  return send(path, { signal });
}

export function query(values: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(values))
    if (value !== undefined && value !== '') search.set(key, String(value));
  const text = search.toString();
  return text ? `?${text}` : '';
}

export function isAbort(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}
