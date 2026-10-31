import type { IDockviewPanelHeaderProps } from 'dockview-react';
import { useEffect, useState } from 'react';

// 面板标签（照 Unity 编辑器窗口标签）：不画关闭按钮，中键单击关闭。
export function PanelTab({ api }: IDockviewPanelHeaderProps) {
  const [title, setTitle] = useState(api.title);
  useEffect(() => {
    const subscription = api.onDidTitleChange(event => setTitle(event.title));
    return () => subscription.dispose();
  }, [api]);
  return (
    <div className="dv-default-tab"
      // 中键按下不触发浏览器的自动滚动。
      onPointerDown={event => { if (event.button === 1) event.preventDefault(); }}
      onAuxClick={event => {
        if (event.button !== 1) return;
        event.preventDefault();
        api.close();
      }}>
      <span className="dv-default-tab-content">{title}</span>
    </div>
  );
}
