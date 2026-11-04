import type { SkillCompilation } from '../api/authoring';
import type { SaveOutcome } from '../state/authoring';
import { consoleActions } from '../state/store';
import { Button, Tip } from '../ui/controls';
import { openPanel } from './layout';
import styles from './toolbar.module.css';

type Publication = NonNullable<SkillCompilation['publicationResult']>;
type GraphWork = NonNullable<Publication['graphs'][number]['compilation']['work']>;

const workNames = {
  Unchanged: '无变化，结构与参数复用',
  Sources: '更新来源，结构与参数复用',
  Parameters: '生成参数，结构复用',
  Structure: '编译结构',
} satisfies Record<GraphWork, string>;

const previewNames = {
  accepted: '等待采用', adopted: '已采用', failed: '采用失败', unavailable: '预览不可用',
} satisfies Record<NonNullable<Extract<SaveOutcome, { compilationState: 'succeeded' }>['preview']>['stage'], string>;

type PreviewCommand = NonNullable<Extract<SaveOutcome, { compilationState: 'not-required' }>['preview']>;

const commandNames = {
  sent: '命令已发送，等待采用', submitted: '命令已受理，等待采用', applied: '已采用',
  rejected: '采用被拒绝', 'not-applied': '尚未采用',
} satisfies Record<PreviewCommand['stage'], string>;

function submissionErrors(outcome: SaveOutcome): string[] {
  if (outcome.compilationState === 'not-required') return [
    outcome.previewError === null ? null : `预览提交失败：${outcome.previewError}`,
    outcome.jobError === null ? null : `作业提交失败：${outcome.jobError}`,
  ].filter(error => error !== null);
  if (outcome.compilationState === 'succeeded' && outcome.jobError !== null)
    return [`作业提交失败：${outcome.jobError}`];
  return [];
}

function resultLabel(outcome: SaveOutcome): string {
  const errors = submissionErrors(outcome);
  if (outcome.compilationState === 'not-required')
    return ['输入已保存', ...errors, outcome.preview === null ? null : commandNames[outcome.preview.stage]]
      .filter(part => part !== null).join('；');
  if (outcome.compilationState === 'succeeded')
    return [`${outcome.compilation.artifactReused ? '已复用' : '已编译'} · ${outcome.preview === null ? '未预览' : previewNames[outcome.preview.stage]}`,
      ...errors].join('；');
  return outcome.ok ? '已保存' : outcome.compilationState === 'failed' ? '编译未通过' : outcome.saved ? '已保存，尚未应用' : '保存未完成';
}

function resultDetails(outcome: SaveOutcome): string {
  const lines = [outcome.message, ...submissionErrors(outcome)];
  if (outcome.compilationState === 'not-required') {
    lines.push('输入文件已保存；不需要重新编译技能。', ...commandDetails(outcome.preview));
    return lines.join('\n');
  }
  if (outcome.compilationState !== 'succeeded') return lines.join('\n');
  const { compilation, preview } = outcome;
  lines.push(`本次编译：${compilation.artifactReused ? '复用已编译产物' : '执行编译'}`,
    `本次来源修订：${compilation.sourceRevision}`);
  const publication = compilation.publicationResult;
  if (publication === null) lines.push('原发布回执：未提供');
  else {
    lines.push(`原发布回执阶段：${publication.stage}`, `原发布来源修订：${publication.sourceRevision}`,
      `原发布内容版本：${publication.publicationHash}`,
      `原回执耗时（毫秒）：读取 ${publication.milliseconds.read}，编译 ${publication.milliseconds.compile}，准备 ${publication.milliseconds.prepare}`,
      `原发布后续阶段：编辑器安装 ${publication.editorInstallation}，内容载入 ${publication.contentLoad}，会话采用 ${publication.sessionAdoption}`);
    if (publication.transaction !== undefined)
      lines.push(`原发布批次耗时（毫秒）：准备 ${publication.transaction.milliseconds.prepare}，提交 ${publication.transaction.milliseconds.commit}`);
    for (const graph of publication.graphs) {
      lines.push(`图 ${graph.graphId}`,
        `编译工作：${graph.compilation.work === null ? '原回执未提供' : workNames[graph.compilation.work]}`,
        `原编译耗时（毫秒）：前端 ${graph.compilation.milliseconds.frontend}，数值与身份 ${graph.compilation.milliseconds.fpAndHash}`,
        'work' in graph.publication
          ? `发布工作：${workNames[graph.publication.work]}；原编码耗时 ${graph.publication.milliseconds.encoding} 毫秒，原发布耗时 ${graph.publication.milliseconds.total} 毫秒`
          : '发布工作：已由同批较早技能准备，本回执未提供耗时',
        `结构版本：${graph.structureIdentity}`, `参数版本：${graph.parameterIdentity}`,
        `目标参数版本：${graph.targetParameterIdentity}`, `来源版本：${graph.sourceIdentity}`,
        `状态布局版本：${graph.stateLayoutIdentity}`, `执行版本：${graph.executionIdentity}`, `内容版本：${graph.contentIdentity}`);
    }
  }
  if (preview === null) {
    lines.push('当前预览：未提交，未取得实际采用回执');
    return lines.join('\n');
  }
  lines.push(`当前预览阶段：${preview.stage}（${previewNames[preview.stage]}）`);
  if (preview.package === null) lines.push('本次准备包：未提供');
  else {
    lines.push(`本次准备包阶段：${preview.package.stage}`, `本次准备的场景版本：${preview.package.sceneRevision}`);
    if (preview.package.program !== null)
      lines.push(`本次准备的执行图版本：${preview.package.program.programRevision}`);
  }
  lines.push(...commandDetails(preview.command));
  return lines.join('\n');
}

function commandDetails(command: PreviewCommand | null): string[] {
  const lines: string[] = [];
  if (command === null) lines.push('当前命令：未提供');
  else {
    lines.push(`当前命令阶段：${command.stage}（${commandNames[command.stage]}）`);
    const ack = command.ack;
    if (ack === null) lines.push('实际 ACK：尚未收到，尚无实际采用版本');
    else {
      lines.push(`实际 ACK：${ack.ok ? '成功' : '拒绝'}，命令 ${ack.commandId}`,
        `ACK 采用档位：${ack.adoptedMode === null ? '未声明' : ack.adoptedMode}`);
      if (ack.error !== undefined && ack.error !== null) lines.push(`ACK 原因：${ack.error}`);
      if (ack.content === null) lines.push('ACK 内容身份：未提供');
      else lines.push(`ACK 内容类型：${ack.content.kind}`, `ACK 内容版本：${ack.content.revision}`,
        `ACK 文档身份：${ack.content.documentId === null ? '未提供' : ack.content.documentId}`,
        `ACK 编排内容身份：${ack.content.contentIdentity === null ? '未提供' : ack.content.contentIdentity}`,
        `ACK 编排内容版本：${ack.content.contentRevision === null ? '未提供' : ack.content.contentRevision}`,
        `ACK 绑定版本：${ack.content.bindingRevision === null ? '未提供' : ack.content.bindingRevision}`);
      if (ack.clock === null) lines.push('ACK 时钟：未提供');
      else lines.push(`ACK 来源时钟：${ack.clock.seconds} 秒；${ack.clock.positionDisplayName} ${ack.clock.position.hostFrame}`);
    }
  }
  return lines;
}

export function SaveResult({ outcome }: { outcome: SaveOutcome }) {
  const warning = submissionErrors(outcome).length !== 0 ||
    (outcome.compilationState === 'not-required' && (outcome.preview?.stage === 'rejected' || outcome.preview?.stage === 'not-applied')) ||
    (outcome.compilationState === 'succeeded' && (outcome.preview?.stage === 'failed' || outcome.preview?.stage === 'unavailable'));
  return (
    <Tip content={`${outcome.message}\n点击查看完整保存结果与原始回执`}>
      <Button variant="ghost" className={outcome.ok && !warning ? styles.dim : styles.warn} onClick={() => {
        const entryId = consoleActions.log(!outcome.ok ? 'error' : warning ? 'warn' : 'info', '作者', outcome.message,
          `${resultDetails(outcome)}\n\n${JSON.stringify(outcome, null, 2)}`);
        consoleActions.focus(entryId);
        openPanel('console');
      }}>{resultLabel(outcome)}</Button>
    </Tip>
  );
}
