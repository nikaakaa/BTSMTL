import type { SkillCompilation } from '../api/authoring';

type Publication = NonNullable<SkillCompilation['publicationResult']>;
type GraphWork = NonNullable<Publication['graphs'][number]['compilation']['work']>;
type GraphVersions = Pick<Publication['graphs'][number], 'structureIdentity' | 'parameterIdentity' | 'targetParameterIdentity'
  | 'sourceIdentity' | 'stateLayoutIdentity' | 'executionIdentity' | 'contentIdentity'>;

const workNames = {
  Unchanged: '无变化，结构与参数复用', Sources: '更新来源，结构与参数复用',
  Parameters: '生成参数，结构复用', Structure: '编译结构',
} satisfies Record<GraphWork, string>;

const phaseNames: Readonly<Partial<Record<string, string>>> = {
  'compilation-inputs': '编译输入准备', 'artifact-read': '读取复用产物', 'compiler-process': '编译进程',
  'unity-publication-bindings': '原生发布绑定准备', 'unity-publication-process': '原生发布进程',
  'request-wait': '刷新请求等待', 'request-prepare': '刷新请求准备', 'scene-read': '读取场景',
  'package-prepare': '准备场景包', 'player-command': 'Player 命令发送至实际 ACK',
};

export function preparationPhaseDetails(phases: SkillCompilation['phases'], owner: string): string[] {
  return phases.length === 0 ? [`${owner}：尚无阶段回执`]
    : phases.map(phase => `${owner} · ${phaseNames[phase.stage] ?? phase.stage}：${phase.milliseconds} 毫秒`);
}

function graphVersionDetails(graph: GraphVersions): string[] {
  return [`结构版本：${graph.structureIdentity}`, `参数版本：${graph.parameterIdentity}`,
    `目标参数版本：${graph.targetParameterIdentity}`, `来源版本：${graph.sourceIdentity}`,
    `状态布局版本：${graph.stateLayoutIdentity}`, `执行版本：${graph.executionIdentity}`, `内容版本：${graph.contentIdentity}`];
}

export function compilationDetails(compilation: SkillCompilation): string[] {
  const lines = [`本次编译：${compilation.artifactReused ? '复用已编译产物' : '执行编译'}`,
    `本次来源修订：${compilation.sourceRevision}`, `编译键：${compilation.compilationKey}`,
    `产物回执路径：${compilation.artifactReceiptPath}`,
    ...preparationPhaseDetails(compilation.phases, '本次编译与发布阶段')];
  const publication = compilation.publicationResult;
  if (publication === null) lines.push('原编译发布回执：未提供');
  else {
    lines.push(`原编译发布回执阶段：${publication.stage}`, `原编译发布来源修订：${publication.sourceRevision}`,
      `原编译发布内容版本：${publication.publicationHash}`,
      `原图产物目录：${publication.output}`, `原发布产物路径：${publication.publicationOutput}`,
      `原产物回执耗时（毫秒）：读取 ${publication.milliseconds.read}，编译 ${publication.milliseconds.compile}，准备 ${publication.milliseconds.prepare}`,
      `原产物回执后续阶段：编辑器安装 ${publication.editorInstallation}，内容载入 ${publication.contentLoad}，会话采用 ${publication.sessionAdoption}`);
    if (publication.transaction !== undefined)
      lines.push(`原发布批次耗时（毫秒）：准备 ${publication.transaction.milliseconds.prepare}，提交 ${publication.transaction.milliseconds.commit}`);
    if (publication.receiptFile !== undefined)
      lines.push(`原发布回执文件：${publication.receiptFile.path}（${publication.receiptFile.stage}）`);
    for (const file of publication.files)
      lines.push(`原发布文件（${file.kind}）：${file.path}；阶段 ${file.stage}${file.graphId === null ? '' : `；图 ${file.graphId}`}`);
    for (const graph of publication.graphs)
      lines.push(`原编译产物 · 图 ${graph.graphId}`,
        `原编译工作：${graph.compilation.work === null ? '原回执未提供' : workNames[graph.compilation.work]}`,
        `原编译耗时（毫秒）：前端 ${graph.compilation.milliseconds.frontend}，数值与身份 ${graph.compilation.milliseconds.fpAndHash}`,
        'work' in graph.publication
          ? `原发布工作：${workNames[graph.publication.work]}；原编码耗时 ${graph.publication.milliseconds.encoding} 毫秒，原发布耗时 ${graph.publication.milliseconds.total} 毫秒`
          : '原发布工作：已由同批较早技能准备，本回执未提供耗时', ...graphVersionDetails(graph));
  }
  const native = compilation.unityPublication;
  if (native === null) lines.push('本次原生资产发布：未提供回执');
  else {
    lines.push(`本次原生资产发布阶段：${native.stage}`, `本次原生发布图数：${native.graphCount}`,
      `本次实际写入文件数：${native.writtenFiles.length}`, `本次原生发布回执：${native.receiptFile}`,
      `本次原生发布耗时（毫秒）：准备 ${native.milliseconds.prepare}，提交 ${native.milliseconds.commit}，合计 ${native.milliseconds.total}（其中编码与身份 ${native.milliseconds.encodingAndHash}）`,
      `原生发布器后续阶段：编辑器导入 ${native.editorImport}，会话采用 ${native.sessionAdoption}`);
    for (const graph of native.graphs) {
      const timing = graph.publication.milliseconds;
      lines.push(`本次原生发布 · 图 ${graph.graphId} · ${graph.numericTarget}`,
        `本次发布工作：${workNames[graph.publication.work]}`, `图来源修订：${graph.sourceRevision}`,
        `原生资产：${graph.assetPath}`, `原生资产 GUID：${graph.assetGuid}`,
        `此前内容版本：${graph.previousContentIdentity ?? '未记录'}`,
        `本次图发布耗时（毫秒）：读取 ${timing.read}，编码与身份 ${timing.encodingAndHash}，准备 ${timing.prepare}，合计 ${timing.total}`,
        ...graphVersionDetails(graph));
      for (const file of graph.publication.files)
        lines.push(`本次图分块（${file.kind}）：${file.stage}；版本 ${file.identity}；资产引用 ${file.assetReference}；${file.path === null ? '沿用既有引用，未提供新文件路径' : `路径 ${file.path}`}`);
    }
    for (const path of native.writtenFiles) lines.push(`本次实际写入：${path}`);
  }
  return lines;
}
