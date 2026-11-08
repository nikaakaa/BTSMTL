import type { IDockviewPanelProps } from 'dockview-react';
import type { FunctionComponent } from 'react';
import { workbenchExtensions } from '../extensions';
import { PanelFrame } from '../shell/panel';
import { panelTypes, type BuiltinPanelType } from '../shell/panelTypes';
import { ConsolePanel } from './ConsolePanel';
import { ExportPanel } from './export/ExportPanel';
import { GraphPanel } from './graph/GraphPanel';
import { InspectorPanel } from './inspector/InspectorPanel';
import { PlayerPanel } from './player/PlayerPanel';
import { FramesPanel } from './records/FramesPanel';
import { QueryPanel } from './records/QueryPanel';
import { ProjectPanel } from './project/ProjectPanel';
import { TimelinePanel } from './timeline/TimelinePanel';
import { ViewportPanel } from './viewport/ViewportPanel';

const builtinContents: Record<BuiltinPanelType, FunctionComponent> = {
  player: PlayerPanel,
  viewport: ViewportPanel,
  resources: ProjectPanel,
  graph: GraphPanel,
  inspector: InspectorPanel,
  frames: FramesPanel,
  timeline: TimelinePanel,
  query: QueryPanel,
  console: ConsolePanel,
  export: ExportPanel,
};

const contents: Record<string, FunctionComponent> = {
  ...builtinContents,
  ...Object.fromEntries(workbenchExtensions.flatMap(extension =>
    (extension.panels ?? []).map(panel => [panel.type, panel.component]))),
};

export const panelComponents: Record<string, FunctionComponent<IDockviewPanelProps>> = Object.fromEntries(
  panelTypes.map(type => {
    const Content = contents[type];
    const component = (props: IDockviewPanelProps) => <PanelFrame type={type} props={props}><Content /></PanelFrame>;
    component.displayName = `${type}Panel`;
    return [type, component];
  }),
);
