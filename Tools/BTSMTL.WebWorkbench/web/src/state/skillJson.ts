// 技能 JSON 的读写：原文按宿主格式（两格缩进、"键": 值、原换行）整份写回，未改动的文件逐字节不变。
// 浮点成员在原文里写作 1.0 这类整数值时，解析为 JsonFloat 保留小数点；新写入的浮点也用 JsonFloat。

export class JsonFloat {
  constructor(readonly value: number) {}
}

export type JsonValue = null | boolean | number | string | JsonFloat | JsonValue[] | { [key: string]: JsonValue };
export type JsonObject = { [key: string]: JsonValue };

type SourceReviver = (key: string, value: unknown, context: { source?: string }) => unknown;

export function parseSkillJson(text: string): JsonValue {
  const reviver: SourceReviver = (_key, value, context) =>
    typeof value === 'number' && Number.isInteger(value) && context.source && /[.eE]/.test(context.source)
      ? new JsonFloat(value) : value;
  return JSON.parse(text, reviver as Parameters<typeof JSON.parse>[1]) as JsonValue;
}

export const numberOf = (value: JsonValue | undefined) => value instanceof JsonFloat ? value.value : typeof value === 'number' ? value : NaN;

const floatText = (value: number) => Number.isInteger(value) ? `${value}.0` : String(value);

export function stringifySkillJson(value: JsonValue, newline: string): string {
  const write = (item: JsonValue, indent: string): string => {
    if (item instanceof JsonFloat) return floatText(item.value);
    if (item === null || typeof item !== 'object') return JSON.stringify(item);
    const inner = indent + '  ';
    if (Array.isArray(item))
      return item.length ? `[${newline}${item.map(entry => inner + write(entry, inner)).join(`,${newline}`)}${newline}${indent}]` : '[]';
    const keys = Object.keys(item);
    return keys.length
      ? `{${newline}${keys.map(key => `${inner}${JSON.stringify(key)}: ${write(item[key], inner)}`).join(`,${newline}`)}${newline}${indent}}`
      : '{}';
  };
  return write(value, '');
}

// 原文的换行与结尾空白（宿主文件为 CRLF 正文、LF 结尾）。
export interface TextShape { newline: string; tail: string }
export const shapeOf = (text: string): TextShape =>
  ({ newline: text.includes('\r\n') ? '\r\n' : '\n', tail: text.slice(text.trimEnd().length) });
export const writeSkillJson = (value: JsonValue, shape: TextShape) => stringifySkillJson(value, shape.newline) + shape.tail;

// 宿主 Timeline 时间以 FP 原始值（秒 × 65536）存盘。
export const fpRaw = (seconds: number) => Math.round(seconds * 65536);
export const fpSeconds = (raw: number) => raw / 65536;
