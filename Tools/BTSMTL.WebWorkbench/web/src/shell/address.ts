import { authoringActions, useAuthoring } from '../state/authoring';
import { session } from '../state/session';
import { getState, resourceActions, selectionActions, useWorkbench, type Selection, type SelectionKind } from '../state/store';
import { activePanelType, openPanel } from './layout';
import { isPanelType } from './panelTypes';
import { setAddressPanel } from './readiness';

// 地址同步（D8）：/?run=&frame=&select=&panel=，不选运行时为 /?skill=&select=，资源为 /?panel=resources&resource=&time=。
// 加载时恢复，状态变化时用 replaceState 回写。

const selectionKinds: SelectionKind[] = ['node', 'edge', 'clip', 'track', 'actor'];
const writeDelay = 200;

function parseSelection(text: string | null): Selection | null {
  if (!text) return null;
  const split = text.indexOf(':');
  const kind = text.slice(0, split) as SelectionKind;
  return split > 0 && selectionKinds.includes(kind) ? { kind, id: text.slice(split + 1) } : null;
}

const initial = new URLSearchParams(location.search);

// 外壳就绪后激活地址指定的面板；没有打开的实例时在默认位置打开。
export function applyPanelAddress() {
  const panel = initial.get('panel');
  if (!panel || !isPanelType(panel)) return;
  openPanel(panel);
  setAddressPanel(panel);
}

// 宿主连接后恢复运行、帧、选中对象与资源。
export function applyStateAddress() {
  const run = initial.get('run');
  const skill = initial.get('skill');
  if (run) {
    const frame = Number(initial.get('frame') ?? '0');
    session.selectRun(run, Number.isSafeInteger(frame) && frame >= 0 ? frame : 0);
    selectionActions.select(parseSelection(initial.get('select')));
  } else if (skill) {
    void authoringActions.openSkill(skill);
    selectionActions.select(parseSelection(initial.get('select')));
  }
  const resource = initial.get('resource');
  if (resource) {
    resourceActions.select(resource);
    const time = Number(initial.get('time') ?? '0');
    if (Number.isFinite(time) && time >= 0) resourceActions.setTime(time);
  }
  writing = true;
}

let writing = false;
let timer: ReturnType<typeof setTimeout> | null = null;

function write() {
  timer = null;
  const state = getState();
  const params = new URLSearchParams();
  const skill = useAuthoring.getState().skill;
  if (state.runId) {
    params.set('run', state.runId);
    params.set('frame', String(state.frameIndex));
  } else if (skill) params.set('skill', skill);
  if (state.selection) params.set('select', `${state.selection.kind}:${state.selection.id}`);
  const panel = activePanelType();
  if (panel) params.set('panel', panel);
  // 资源只在看资源面板时写入；不经运行直接编辑技能 JSON 时地址也不带资源面板自动选中的片段。
  if (state.identity && panel === 'resources') {
    params.set('resource', state.identity);
    params.set('time', String(Number(state.time.toFixed(4))));
  }
  const search = params.toString();
  if (`?${search}` !== location.search) history.replaceState(null, '', search ? `?${search}` : location.pathname);
}

useWorkbench.subscribe((state, previous) => {
  if (!writing) return;
  if (state.runId === previous.runId && state.frameIndex === previous.frameIndex && state.selection === previous.selection &&
    state.activePanel === previous.activePanel && state.identity === previous.identity && state.time === previous.time) return;
  timer ??= setTimeout(write, writeDelay);
});

useAuthoring.subscribe((state, previous) => {
  if (writing && state.skill !== previous.skill) timer ??= setTimeout(write, writeDelay);
});
