import type {
  AddPanelPositionOptions, DockviewApi, DockviewGroupPanel, IDockviewPanel, PopoutWindowFailure, SerializedDockview,
} from 'dockview-react';
import { consoleActions, layoutActions } from '../state/store';
import { instanceNumber, instanceTitle, panelInfo, panelTypeOf, panelTypes, type PanelType } from './panelTypes';

// 布局：保存、恢复、默认布局、按类型打开与多开、拆出窗口（D3、D12）。

const storageKey = 'btsmtl.workbench.layout';
const signature = panelTypes.join(',');
const source = 'layout';
const inspectorWidth = 372;

let api: DockviewApi | null = null;
let saveTimer: ReturnType<typeof setTimeout> | null = null;

interface Saved {
  signature: string;
  layout: SerializedDockview;
}

// 同组页签：按类型找默认所在组。
const companions: Record<PanelType, PanelType[]> = {
  player: ['player', 'viewport', 'resources'],
  viewport: ['player', 'viewport', 'resources'],
  resources: ['player', 'viewport', 'resources'],
  graph: ['graph'],
  inspector: ['inspector', 'frames'],
  frames: ['inspector', 'frames'],
  timeline: ['timeline', 'query', 'compare', 'console'],
  query: ['timeline', 'query', 'compare', 'console'],
  compare: ['timeline', 'query', 'compare', 'console'],
  console: ['timeline', 'query', 'compare', 'console'],
  content: ['content'],
};

function dock(): DockviewApi {
  if (!api) throw new Error('停靠外壳尚未就绪');
  return api;
}

function instances(type: PanelType): IDockviewPanel[] {
  return dock().panels
    .filter(panel => panelTypeOf(panel.id) === type)
    .sort((left, right) => instanceNumber(left.id) - instanceNumber(right.id));
}

function addInstance(type: PanelType, number: number, position: AddPanelPositionOptions | undefined, inactive = false) {
  return dock().addPanel({
    id: `${type}-${number}`,
    component: type,
    title: instanceTitle(type, number),
    position,
    inactive,
  });
}

function defaultPosition(type: PanelType): AddPanelPositionOptions | undefined {
  for (const companion of companions[type]) {
    const anchor = instances(companion)[0];
    if (anchor) return { referenceGroup: anchor.group, direction: 'within' };
  }
  if (type === 'graph') {
    const player = companions.player.map(item => instances(item)[0]).find(Boolean);
    if (player) return { referenceGroup: player.group, direction: 'right' };
  }
  switch (type) {
    case 'inspector': case 'frames': return { direction: 'right' };
    case 'timeline': case 'query': case 'compare': case 'console': return { direction: 'below' };
    case 'content': return { direction: 'left' };
    default: return undefined;
  }
}

// ── 默认布局（spec：画面、节点图、Timeline 同时可见，检查器纵贯右侧） ──

function buildDefault() {
  const dockview = dock();
  dockview.clear();
  const player = addInstance('player', 1, undefined);
  addInstance('viewport', 1, { referencePanel: player, direction: 'within' }, true);
  addInstance('resources', 1, { referencePanel: player, direction: 'within' }, true);
  const timeline = addInstance('timeline', 1, { referencePanel: player, direction: 'below' });
  for (const type of ['query', 'compare', 'console'] as const)
    addInstance(type, 1, { referencePanel: timeline, direction: 'within' }, true);
  const graph = addInstance('graph', 1, { referencePanel: player, direction: 'right' });
  const inspector = addInstance('inspector', 1, { direction: 'right' });
  addInstance('frames', 1, { referencePanel: inspector, direction: 'within' }, true);
  player.api.setActive();
  timeline.api.setActive();
  graph.api.setActive();
  inspector.api.setActive();

  const size = () => {
    const width = dockview.width, height = dockview.height;
    if (!width || !height) return false;
    inspector.group.api.setSize({ width: inspectorWidth });
    player.group.api.setSize({ height: Math.round(height * 0.43) });
    player.group.api.setSize({ width: Math.round((width - inspectorWidth) * 0.5) });
    return true;
  };
  if (!size()) requestAnimationFrame(() => size());
}

// ── 保存与恢复 ──

function readSaved(): { layout: SerializedDockview } | { reason: string } | null {
  const text = localStorage.getItem(storageKey);
  if (!text) return null;
  let saved: Saved;
  try {
    saved = JSON.parse(text) as Saved;
  } catch {
    return { reason: '保存的布局无法解析' };
  }
  if (saved.signature !== signature || !saved.layout?.panels) return { reason: '保存的布局来自另一组面板类型' };
  const counts = new Map<PanelType, number>();
  for (const panel of Object.values(saved.layout.panels)) {
    const type = panelTypeOf(panel.id);
    if (!type || panel.contentComponent !== type) return { reason: `保存的布局引用了未知面板 ${panel.contentComponent ?? panel.id}` };
    counts.set(type, (counts.get(type) ?? 0) + 1);
  }
  for (const [type, count] of counts)
    if (count > 1 && !panelInfo[type].multi) return { reason: `保存的布局中${panelInfo[type].title}出现了 ${count} 个` };
  return { layout: saved.layout };
}

function save() {
  saveTimer = null;
  if (!api) return;
  localStorage.setItem(storageKey, JSON.stringify({ signature, layout: api.toJSON() } satisfies Saved));
}

export function requestLayoutSave() {
  if (saveTimer) clearTimeout(saveTimer);
  saveTimer = setTimeout(save, 250);
}

function syncSlice() {
  if (!api) return;
  layoutActions.setOpenPanels(api.panels.map(panel => panel.id));
  layoutActions.setActivePanel(api.activePanel?.id ?? null);
}

const failureNames: Record<PopoutWindowFailure['reason'], string> = {
  'url-refused': '承载页地址被拒绝',
  blocked: '浏览器拦截了新窗口',
  unscriptable: '无法访问新窗口的页面',
  closed: '新窗口在加载完成前被关闭',
};
let popoutFailureLogged = false;

export function attachLayout(dockview: DockviewApi) {
  api = dockview;
  const saved = readSaved();
  if (saved && 'layout' in saved) {
    try {
      dockview.fromJSON(saved.layout);
    } catch (error) {
      consoleActions.log('warn', source, '保存的布局无法恢复，已使用默认布局', error instanceof Error ? error.message : String(error));
      buildDefault();
    }
  } else {
    if (saved) consoleActions.log('warn', source, `${saved.reason}，已使用默认布局`);
    buildDefault();
  }
  if (!dockview.panels.length) buildDefault();
  syncSlice();
  requestLayoutSave();
  dockview.onDidLayoutChange(() => { requestLayoutSave(); syncSlice(); });
  dockview.onDidActivePanelChange(() => syncSlice());
  dockview.onDidOpenPopoutWindowFail(failure => {
    popoutFailureLogged = true;
    consoleActions.log('error', source, `独立窗口未打开：${failureNames[failure.reason]}`, failure.error?.message);
  });
}

export function resetLayout() {
  buildDefault();
  save();
  syncSlice();
}

// 按类型打开：已打开时激活第一个实例；没有时在默认位置打开一个。
export function openPanel(type: PanelType): IDockviewPanel {
  const existing = instances(type)[0];
  if (existing) {
    existing.api.setActive();
    return existing;
  }
  return addInstance(type, 1, defaultPosition(type));
}

// 可多开的类型新开一个实例，放在同类实例所在组。
export function openNewInstance(type: PanelType): IDockviewPanel {
  if (!panelInfo[type].multi) return openPanel(type);
  const current = instances(type);
  const number = current.reduce((max, panel) => Math.max(max, instanceNumber(panel.id)), 0) + 1;
  const anchor = current.at(-1);
  return addInstance(type, number, anchor ? { referenceGroup: anchor.group, direction: 'within' } : defaultPosition(type));
}

// 只在用户操作时调用；失败时面板留在原位并写入控制台。
export async function popoutGroup(group: DockviewGroupPanel) {
  popoutFailureLogged = false;
  const opened = await dock().addPopoutGroup(group, { popoutUrl: '/popout.html' });
  if (!opened && !popoutFailureLogged) consoleActions.log('error', source, '独立窗口未打开');
}

export function firstInstance(type: PanelType): IDockviewPanel | undefined {
  return api ? instances(type)[0] : undefined;
}

export function visiblePanels(): string[] {
  return api ? api.panels.filter(panel => panel.api.isVisible).map(panel => panel.id) : [];
}

export function activePanelType(): PanelType | null {
  return api?.activePanel ? panelTypeOf(api.activePanel.id) : null;
}
