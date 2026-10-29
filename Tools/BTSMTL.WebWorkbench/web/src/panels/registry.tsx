import type { IDockviewPanelProps } from 'dockview-react';
import type { FunctionComponent } from 'react';
import { PanelFrame } from '../shell/panel';
import { panelInfo, panelTypes, type PanelType } from '../shell/panelTypes';
import { Empty } from '../ui/blocks';

function Placeholder({ type }: { type: PanelType }) {
  return <Empty text={`${panelInfo[type].title}尚未接入`} />;
}

const contents: Partial<Record<PanelType, FunctionComponent>> = {};

export const panelComponents: Record<string, FunctionComponent<IDockviewPanelProps>> = Object.fromEntries(
  panelTypes.map(type => {
    const Content = contents[type];
    const component = (props: IDockviewPanelProps) => (
      <PanelFrame type={type} props={props}>{Content ? <Content /> : <Placeholder type={type} />}</PanelFrame>
    );
    component.displayName = `${type}Panel`;
    return [type, component];
  }),
);
