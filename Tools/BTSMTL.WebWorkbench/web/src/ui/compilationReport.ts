import type { CompilationReport } from '../api/authoring';

const phaseNames: Readonly<Partial<Record<string, string>>> = {
  'compilation-inputs': '编译输入准备', 'artifact-read': '读取复用产物', 'compiler-process': '编译进程',
  'unity-publication-bindings': '原生发布绑定准备', 'unity-publication-process': '原生发布进程',
  'request-wait': '刷新请求等待', 'request-prepare': '刷新请求准备', 'scene-read': '读取场景',
  'package-prepare': '准备场景包', 'player-command': 'Player 命令发送至实际 ACK',
};

export function preparationPhaseDetails(phases: CompilationReport['phases'], owner: string): string[] {
  return phases.length === 0 ? [`${owner}：尚无阶段回执`]
    : phases.map(phase => `${owner} · ${phaseNames[phase.stage] ?? phase.stage}：${phase.milliseconds} 毫秒`);
}

export function compilationDetails(compilation: CompilationReport): string[] {
  return [`本次编译：${compilation.artifactReused ? '复用已编译产物' : '执行编译'}`,
    `本次来源修订：${compilation.sourceRevision}`, `编译键：${compilation.compilationKey}`,
    `产物回执路径：${compilation.artifactReceiptPath}`,
    ...preparationPhaseDetails(compilation.phases, '本次编译与发布阶段'),
    `编译发布回执：${JSON.stringify(compilation.publicationResult, null, 2)}`,
    `原生资产发布回执：${JSON.stringify(compilation.unityPublication, null, 2)}`];
}
