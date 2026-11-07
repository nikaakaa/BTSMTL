import type { FunctionComponent } from 'react';
import hostExtensions from 'virtual:btsmtl-workbench-extensions';

// 宿主扩展：宿主构建时经 BTSMTL_WORKBENCH_EXTENSIONS 给出入口模块，各入口默认导出一个 WorkbenchExtension。
// 工作台本身不认识任何宿主业务面板；停靠面板与画面工具只经此处登记。
export interface WorkbenchPanelContribution {
  type: string;
  title: string;
  multi: boolean;
  // 默认停靠时依次寻找的同组面板类型。
  companions: readonly string[];
  component: FunctionComponent;
}

export interface WorkbenchPlayerToolContribution {
  id: string;
  label: string;
  component: FunctionComponent;
}

export interface WorkbenchExtension {
  id: string;
  panels?: readonly WorkbenchPanelContribution[];
  playerTools?: readonly WorkbenchPlayerToolContribution[];
}

export const workbenchExtensions: readonly WorkbenchExtension[] = hostExtensions;
export const playerTools: readonly WorkbenchPlayerToolContribution[] = workbenchExtensions.flatMap(item => item.playerTools ?? []);
