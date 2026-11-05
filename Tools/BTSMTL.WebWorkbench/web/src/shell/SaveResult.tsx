import type { SaveOutcome } from '../state/authoring';
import { consoleActions } from '../state/store';
import { Button, Tip } from '../ui/controls';
import { compilationDetails, preparationPhaseDetails } from '../ui/compilationReport';
import { openPanel } from './layout';
import styles from './toolbar.module.css';

const previewNames = {
  accepted: '等待采用', adopted: '已采用', failed: '采用失败', unavailable: '预览不可用',
} satisfies Record<NonNullable<Extract<SaveOutcome, { compilationState: 'succeeded' }>['preview']>['stage'], string>;

type PreviewCommand = NonNullable<Extract<SaveOutcome, { compilationState: 'not-required' }>['preview']>;

const commandNames = {
  sent: '命令已发送，等待采用', submitted: '命令已受理，等待采用', applied: '命令已应用',
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
  if ('publicationState' in outcome && outcome.publicationState === 'failed') return '已编译，Unity 资产发布失败';
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
  const lines = [outcome.message];
  if (outcome.compilationState === 'not-required') {
    lines.push('输入文件已保存；不需要重新编译技能。', ...commandDetails(outcome.preview));
    return lines.join('\n');
  }
  if (outcome.compilationState !== 'succeeded') return lines.join('\n');
  const { compilation, preview } = outcome;
  lines.push(...compilationDetails(compilation));
  if (preview === null) {
    lines.push('当前预览：未提交，未取得实际采用回执');
    return lines.join('\n');
  }
  lines.push(`当前预览阶段：${preview.stage}（${previewNames[preview.stage]}）`);
  lines.push(...preparationPhaseDetails(preview.phases, '本次预览刷新阶段'),
    `实际采用场景版本：${preview.adoptedSceneRevision ?? '尚未确认采用'}`);
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
    lines.push(command.milliseconds === null ? '命令发送至实际 ACK 耗时：尚未取得'
      : `命令发送至实际 ACK 耗时：${command.milliseconds} 毫秒`);
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
