// 面板类型表（D3）。类型 id 固定，实例 id 为"类型 id-序号"。
export const panelTypes = [
  'player', 'viewport', 'resources', 'wall-contact', 'graph', 'inspector', 'frames', 'timeline', 'query', 'console', 'content',
] as const;

export type PanelType = typeof panelTypes[number];

export const panelInfo: Record<PanelType, { title: string; multi: boolean }> = {
  player: { title: '画面', multi: false },
  'wall-contact': { title: '扶墙调节', multi: false },
  viewport: { title: '逻辑视图', multi: true },
  resources: { title: '动画资源', multi: false },
  graph: { title: '节点图', multi: true },
  inspector: { title: '检查器', multi: true },
  frames: { title: '帧记录', multi: true },
  timeline: { title: 'Timeline', multi: false },
  query: { title: '记录查询', multi: false },
  console: { title: '控制台', multi: false },
  content: { title: '内容', multi: false },
};

export function isPanelType(value: string): value is PanelType {
  return (panelTypes as readonly string[]).includes(value);
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
