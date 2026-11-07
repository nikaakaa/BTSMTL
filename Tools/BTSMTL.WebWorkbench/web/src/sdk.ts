// 宿主扩展唯一导入入口（@btsmtl/workbench）：扩展只经这里使用工作台状态、播放器通道、请求与界面组件。
export type { WorkbenchExtension, WorkbenchPanelContribution, WorkbenchPlayerToolContribution } from './extensions';
export { getJson, postJson } from './api/http';
export { playerApi } from './api/player';
export {
  array, boolean, choice, nullable, number, object, optional, samePreviewLoad, text,
  type Decoder, type PreviewSampleSource,
} from './api/playerRecords';
export type { HostLiveCommand, PlayerAck, ViewerPlayer } from './api/types';
export { playerService } from './state/player';
export { consoleActions, getState, useWorkbench } from './state/store';
export { Empty, Facts, Fold, PanelBar } from './ui/blocks';
export { Button, Checkbox, FieldLabel, Select, TextInput, Tip } from './ui/controls';
export { ErrorLine } from './ui/ErrorLine';
