import { workbenchExtensions } from '../extensions';

// 面板类型表（D3）。类型 id 固定，实例 id 为"类型 id-序号"；宿主扩展面板追加在内置面板之后。
export type PanelType = string;

export interface PanelDefinition {
  title: string;
  multi: boolean;
  // 默认停靠时依次寻找的同组面板类型。
  companions: readonly string[];
}

const sceneGroup = ['player', 'viewport'];
const sideGroup = ['inspector', 'frames'];
const bottomGroup = ['timeline', 'query', 'console'];

const builtinPanels = {
  player: { title: '画面', multi: false, companions: sceneGroup },
  viewport: { title: '逻辑视图', multi: true, companions: sceneGroup },
  resources: { title: 'Project', multi: false, companions: ['resources'] },
  graph: { title: '节点图', multi: true, companions: ['graph'] },
  inspector: { title: '检查器', multi: true, companions: sideGroup },
  frames: { title: '帧记录', multi: true, companions: sideGroup },
  timeline: { title: 'Timeline', multi: false, companions: bottomGroup },
  query: { title: '记录查询', multi: false, companions: bottomGroup },
  console: { title: '控制台', multi: false, companions: bottomGroup },
  export: { title: '内容导出', multi: false, companions: bottomGroup },
} satisfies Record<string, PanelDefinition>;

export type BuiltinPanelType = keyof typeof builtinPanels;

function definitions(): typeof builtinPanels & Record<string, PanelDefinition> {
  const result: typeof builtinPanels & Record<string, PanelDefinition> = { ...builtinPanels };
  for (const extension of workbenchExtensions)
    for (const panel of extension.panels ?? []) {
      if (Object.hasOwn(result, panel.type)) throw new Error(`扩展 ${extension.id} 的面板类型 ${panel.type} 已被占用`);
      result[panel.type] = { title: panel.title, multi: panel.multi, companions: panel.companions };
    }
  return result;
}

export const panelInfo: Readonly<Record<BuiltinPanelType, PanelDefinition> & Record<string, PanelDefinition>> = definitions();
export const panelTypes: readonly PanelType[] = Object.keys(panelInfo);

export function isPanelType(value: string): value is PanelType {
  return Object.hasOwn(panelInfo, value);
}

export function panelTypeOf(id: string): PanelType | null {
  const type = id.slice(0, id.lastIndexOf('-'));
  return isPanelType(type) ? type : null;
}

export function instanceNumber(id: string): number {
  return Number(id.slice(id.lastIndexOf('-') + 1)) || 0;
}

export function instanceTitle(type: PanelType, number: number): string {
  return number > 1 ? `${panelInfo[type].title} ${number}` : panelInfo[type].title;
}
