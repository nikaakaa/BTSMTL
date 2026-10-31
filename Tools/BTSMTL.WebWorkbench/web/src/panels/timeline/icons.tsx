import type { ReactElement, SVGProps } from 'react';
import type { TrackKind } from '../../state/timelineModel';

// Timeline 窗口专用图形的矢量重画（照 com.unity.timeline Dark 皮肤的轨道图标、标记与播放头形状），颜色取 currentColor。

type ShapeProps = Omit<SVGProps<SVGSVGElement>, 'children'>;

// 轨道头类型图标（16×16）：动画为关键帧，位移曲线为带端点的曲线，逻辑为节点树，输入为按键，其它为通用轨道。
const trackIcons: Record<TrackKind, ReactElement> = {
  anim: <path d="M1 7.3h14v1.4H1zM4.5 4.6 7.9 8 4.5 11.4 1.1 8zm7 0L14.9 8l-3.4 3.4L8.1 8z" />,
  motion: <>
    <path fill="none" stroke="currentColor" strokeWidth="1.4" d="M4 12.5C9.5 12.5 6.5 3.5 12 3.5" />
    <path d="M1.5 11h3v3h-3zm10-9h3v3h-3z" />
  </>,
  act: <>
    <path d="M5.5 1.5h5v4h-5zm-4 9h5v4h-5zm8 0h5v4h-5z" />
    <path fill="none" stroke="currentColor" strokeWidth="1.2" d="M8 5.5V8M4 10.5V8h8v2.5" />
  </>,
  in: <>
    <path fill="none" stroke="currentColor" strokeWidth="1.3" d="M3.5 2.65h9a1.85 1.85 0 0 1 1.85 1.85v7a1.85 1.85 0 0 1-1.85 1.85h-9a1.85 1.85 0 0 1-1.85-1.85v-7A1.85 1.85 0 0 1 3.5 2.65z" />
    <path d="M5 9h6v1.5H5z" />
  </>,
  other: <path d="M2 3h12v2H2zm0 4h12v2H2zm0 4h12v2H2z" />,
};

export const TrackIcon = ({ kind }: { kind: TrackKind }) => (
  <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden focusable="false">{trackIcons[kind]}</svg>
);

// 面包屑里的 Timeline 资源图标（16×16）：错开的三段片段。
export const TimelineAssetIcon = (props: ShapeProps) => (
  <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden focusable="false" {...props}>
    <path d="M1.5 3h7.5v2.5H1.5zm4.5 4h8.5v2.5H6zm-3 4h6.5v2.5H3z" />
  </svg>
);

// 标记旗头（9×11）：上方方、下方收成尖，尖端对准时刻。
export const MarkerHead = (props: ShapeProps) => (
  <svg width="9" height="11" viewBox="0 0 9 11" aria-hidden focusable="false" {...props}>
    <path d="M.5.5h8v6L4.5 10.5.5 6.5z" />
  </svg>
);

// 播放头（TimelineTimeCursor，11×20）：上方圆角的白色五边形，尖端接游标线。
export const PlayheadShape = () => (
  <svg width="11" height="20" viewBox="0 0 11 20" aria-hidden focusable="false">
    <path d="M1.5 0h8A1.5 1.5 0 0 1 11 1.5v12L5.5 20 0 13.5v-12A1.5 1.5 0 0 1 1.5 0z" />
  </svg>
);

// 曲线参数的关键帧按钮（12×12，照 Slate prevKey / key / nextKey）：菱形为关键帧，两侧为跳到上一个、下一个关键帧。
const keyIcons = {
  previous: <path d="M1 1.5h1.6v9H1zM10.5 1.5v9L4 6z" />,
  key: <path d="M6 1 11 6 6 11 1 6z" />,
  next: <path d="M9.4 1.5H11v9H9.4zM1.5 1.5v9L8 6z" />,
};

export const KeyIcon = ({ kind }: { kind: keyof typeof keyIcons }) => (
  <svg width="12" height="12" viewBox="0 0 12 12" fill="currentColor" aria-hidden focusable="false">{keyIcons[kind]}</svg>
);
