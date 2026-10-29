import type { IDockviewPanelProps } from 'dockview-react';
import type { FunctionComponent } from 'react';
import { PanelFrame } from '../shell/panel';
import { panelTypes, type PanelType } from '../shell/panelTypes';
import { ConsolePanel } from './ConsolePanel';
import { ContentPanel } from './ContentPanel';
import { GraphPanel } from './graph/GraphPanel';
import { InspectorPanel } from './inspector/InspectorPanel';
import { PlayerPanel } from './player/PlayerPanel';
import { ComparePanel } from './records/ComparePanel';
import { FramesPanel } from './records/FramesPanel';
import { QueryPanel } from './records/QueryPanel';
import { ResourcesPanel } from './resources/ResourcesPanel';
import { TimelinePanel } from './timeline/TimelinePanel';
import { ViewportPanel } from './viewport/ViewportPanel';
import { WallContactPanel } from './wall-contact/WallContactPanel';

const contents: Record<PanelType, FunctionComponent> = {
  player: PlayerPanel,
  'wall-contact': WallContactPanel,
  viewport: ViewportPanel,
  resources: ResourcesPanel,
  graph: GraphPanel,
  inspector: InspectorPanel,
  frames: FramesPanel,
  timeline: TimelinePanel,
  query: QueryPanel,
  compare: ComparePanel,
  console: ConsolePanel,
  content: ContentPanel,
};

export const panelComponents: Record<string, FunctionComponent<IDockviewPanelProps>> = Object.fromEntries(
  panelTypes.map(type => {
    const Content = contents[type];
    const component = (props: IDockviewPanelProps) => <PanelFrame type={type} props={props}><Content /></PanelFrame>;
    component.displayName = `${type}Panel`;
    return [type, component];
  }),
);
