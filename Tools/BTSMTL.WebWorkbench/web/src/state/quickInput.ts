import type { InputCatalog, PlayerInputChoice } from '../api/types';
import type { PreviewQuickInputSource } from '../api/previewQuickRecords';

export function quickRunInputChoice(catalog: InputCatalog, actorId: string,
  source: Extract<PreviewQuickInputSource, { kind: 'documentInput' }>): PlayerInputChoice {
  const player = catalog.players.find(item => item.actorId === actorId);
  if (player === undefined) throw new Error(`当前真实场景没有快速预览角色 ${actorId}`);
  const plan = player.localPlans.find(item => item.documentPath === source.documentPath && item.id === source.inputId);
  if (plan === undefined) throw new Error('当前真实场景没有所选输入方案；请选择与方案匹配的场景或先应用入口内容');
  const scheme = plan.schemes.find(item => item.id === source.schemeId);
  if (scheme === undefined) throw new Error('所选输入方案已被删除或更换，请重新选择');
  return { actorId, inputPlanRoot: plan.path, inputPlanRevision: plan.revision, schemeIndex: scheme.index };
}
