import type { ReactNode, SVGProps } from 'react';

// Unity 编辑器 Dark 皮肤图标的矢量重画：照原图形状画在像素网格上，颜色取 currentColor，任何缩放都清晰。
// BTSMTL 连同编辑器开源，不带 Unity 的 PNG。

type IconProps = Omit<SVGProps<SVGSVGElement>, 'children'>;

function Icon({ box = 16, children, ...props }: IconProps & { box?: number; children: ReactNode }) {
  return (
    <svg width={box} height={box} viewBox={`0 0 ${box} ${box}`} fill="currentColor" aria-hidden focusable="false" {...props}>
      {children}
    </svg>
  );
}

// 窗口与轨道的选项按钮（pane options）：竖排三点。
export const OptionsIcon = (props: IconProps) => (
  <Icon {...props}><circle cx="8" cy="3.5" r="1.3" /><circle cx="8" cy="8" r="1.3" /><circle cx="8" cy="12.5" r="1.3" /></Icon>
);

// 设置齿轮（_Popup）。
export const SettingsIcon = (props: IconProps) => (
  <Icon {...props}>
    <path fillRule="evenodd" d="M6.36 2.64 6.72 .71h2.56l.36 1.93.99.42 1.61-1.12 1.82 1.82-1.12 1.61.42.99 1.93.36v2.56l-1.93.36-.42.99 1.12 1.61-1.82 1.82-1.61-1.12-.99.42-.36 1.93H6.72l-.36-1.93-.99-.42-1.61 1.12-1.82-1.82 1.12-1.61-.42-.99-1.93-.36V6.72l1.93-.36.42-.99-1.12-1.61 1.82-1.82 1.61 1.12zM8 5.6a2.4 2.4 0 1 0 0 4.8 2.4 2.4 0 0 0 0-4.8z" />
  </Icon>
);

// 新建（CreateAddNew）。
export const AddIcon = (props: IconProps) => (
  <Icon {...props}><path d="M7 3h2v4h4v2H9v4H7V9H3V7h4z" /></Icon>
);

export const SearchIcon = (props: IconProps) => (
  <Icon {...props}>
    <path fillRule="evenodd" d="M6.5 2a4.5 4.5 0 0 1 3.67 7.1l3.37 3.37-1.07 1.07-3.37-3.37A4.5 4.5 0 1 1 6.5 2zm0 1.5a3 3 0 1 0 0 6 3 3 0 0 0 0-6z" />
  </Icon>
);

// 轨道静音开关（TrackMuteButton）：未静音为暗色眼睛，静音为亮色带斜线的眼睛。
export const MuteIcon = ({ muted, ...props }: IconProps & { muted: boolean }) => (
  <Icon {...props}>
    <path fill="none" stroke="currentColor" strokeWidth="1.3" d="M1.2 8C2.9 5.1 5.3 3.6 8 3.6s5.1 1.5 6.8 4.4C13.1 10.9 10.7 12.4 8 12.4S2.9 10.9 1.2 8z" />
    <circle cx="8" cy="8" r="2.2" />
    {muted && <path stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" d="M2.5 13.5 13.5 2.5" />}
  </Icon>
);

// 轨道曲线展开（Slate 轨道头的 ShowCurves）：坐标框内一条起伏的曲线。
export const CurveIcon = (props: IconProps) => (
  <Icon {...props}>
    <path fill="none" stroke="currentColor" strokeWidth="1.1" opacity=".55" d="M2 2.5v11h12" />
    <path fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" d="M3.5 11.5C6 11.5 6 4.5 8.5 4.5s2.5 4 5 4" />
  </Icon>
);

// 折叠三角（Foldout，13×13）。
export const FoldoutIcon = ({ open, ...props }: IconProps & { open: boolean }) => (
  <Icon box={13} {...props}><path d={open ? 'M2.5 4.5h8l-4 4.5z' : 'M4.5 2.5v8l4.5-4z'} /></Icon>
);

// 菜单与复选框的对勾（10×10）。
export const CheckIcon = (props: IconProps) => (
  <Icon box={10} {...props}>
    <path fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" d="M1.6 5.3 4 7.6l4.4-5.2" />
  </Icon>
);

// 关闭与删除。
export const CloseIcon = (props: IconProps) => (
  <Icon {...props}><path fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" d="m4.5 4.5 7 7m0-7-7 7" /></Icon>
);

// 拆到独立窗口：前后错开的两个窗口。
export const PopoutIcon = (props: IconProps) => (
  <Icon {...props}><path fill="none" stroke="currentColor" d="M5.5 2.5h8v8h-8zm0 3h-3v8h8v-3" /></Icon>
);

export const UndoIcon = (props: IconProps) => (
  <Icon {...props}>
    <path fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" d="M5.5 3 2.5 6l3 3m-3-3h7a3.5 3.5 0 0 1 0 7H6" />
  </Icon>
);

export const RedoIcon = (props: IconProps) => (
  <Icon {...props}>
    <path fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" d="m10.5 3 3 3-3 3m3-3h-7a3.5 3.5 0 0 0 0 7H10" />
  </Icon>
);

// 面包屑层级之间的细长箭头（7×20）。
export const CrumbArrow = (props: IconProps) => (
  <svg width="7" height="20" viewBox="0 0 7 20" fill="none" stroke="currentColor" aria-hidden focusable="false" {...props}>
    <path d="M1 3.5 6 10l-5 6.5" />
  </svg>
);

// Timeline 选择器（TimelineSelector）：左侧短竖条加方框。
export const SelectorIcon = (props: IconProps) => (
  <Icon {...props}>
    <path d="M1 5.5h1.5v5H1z" />
    <path fill="none" stroke="currentColor" strokeWidth="1.3" d="M4.65 3.15h9.7v9.7h-9.7z" />
  </Icon>
);
