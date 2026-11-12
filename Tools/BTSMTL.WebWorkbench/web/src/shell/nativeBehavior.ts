const guarded = new WeakSet<Window>();

// 拆出的独立窗口有自己的 Element 构造，按方法判断而不是 instanceof；文字节点没有 closest，按不可编辑处理。
const within = (target: EventTarget | null, selector: string) =>
  (target as Partial<Element> | null)?.closest?.(selector) != null;

// 工作台窗口（含拆出的独立窗口）不出现浏览器原生右键菜单、文字选择与元素拖拽。
// 工作台自己的右键菜单在 React 根上先处理，冒泡到 window 时再取消默认行为。
export function guardNativeBehavior(target: Window) {
  if (guarded.has(target)) return;
  guarded.add(target);
  target.addEventListener('contextmenu', event => event.preventDefault());
  target.addEventListener('selectstart', event => {
    if (!within(event.target, 'input, textarea, [contenteditable="true"]')) event.preventDefault();
  });
  target.addEventListener('dragstart', event => {
    if (!within(event.target, '[draggable="true"]')) event.preventDefault();
  });
}
