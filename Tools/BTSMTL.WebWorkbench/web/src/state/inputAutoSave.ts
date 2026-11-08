// 输入方案是编辑器侧数据：输入方案、其输入 Timeline 或这些 Timeline 引用的图改动后自动保存，不编译技能。
// 是否只含输入修改由 Host 按输入闭包判定；混有其他修改时 Host 拒绝且不写盘，留给作者手动保存。
import { authoringActions, authoringDocument, timelinePath, useAuthoring } from './authoring';
import { parseSkillJson, type JsonObject, type JsonValue } from './skillJson';

const delay = 800;
let timer: ReturnType<typeof setTimeout> | null = null;
let seen: string | null = null;

function collectStrings(value: JsonValue, into: Set<string>) {
  if (typeof value === 'string') into.add(value);
  else if (Array.isArray(value)) for (const item of value) collectStrings(item, into);
  else if (value !== null && typeof value === 'object') for (const item of Object.values(value)) collectStrings(item, into);
}

function touchesInputs(): boolean {
  const state = useAuthoring.getState();
  if (state.target === null) return false;
  const document = authoringDocument(state);
  const changed = [...Object.keys(document.edited), ...document.removed];
  if (changed.length === 0) return false;
  const read = (path: string): JsonValue | null => document.edited[path]
    ?? (document.files[path] !== undefined ? parseSkillJson(document.files[path]) : null);
  const plans = [...new Set([...Object.keys(document.files), ...Object.keys(document.edited)])]
    .filter(path => path.startsWith('inputs/') && path.endsWith('.json') && !document.removed.includes(path));
  const inputPaths = new Set(plans);
  for (const plan of plans) for (const scheme of ((read(plan) as JsonObject | null)?.Schemes as JsonObject[] | undefined) ?? []) {
    const path = timelinePath(scheme.InputTimeline as string);
    inputPaths.add(path);
    const timeline = read(path);
    if (timeline === null) continue;
    const strings = new Set<string>();
    collectStrings(timeline, strings);
    for (const value of strings) inputPaths.add(`graphs/${value}.json`);
  }
  return changed.some(path => inputPaths.has(path) || path.startsWith('inputs/'));
}

export function startInputAutoSave() {
  useAuthoring.subscribe(state => {
    if (state.target === null) return;
    const document = authoringDocument(state);
    const key = `${state.target.documentPath}#${document.version}`;
    if (key === seen || document.saving) return;
    seen = key;
    if (!touchesInputs()) return;
    if (timer !== null) clearTimeout(timer);
    timer = setTimeout(() => {
      timer = null;
      const current = authoringDocument(useAuthoring.getState());
      if (!current.saving && Object.keys(current.edited).length + current.removed.length > 0) void authoringActions.save('inputs');
    }, delay);
  });
}
