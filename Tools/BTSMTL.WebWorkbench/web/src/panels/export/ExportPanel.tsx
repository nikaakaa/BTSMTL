import { useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { SkillEditingView, SkillExportKind } from '../../api/authoring';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { exportActions, useExports, type ExportOperation } from '../../state/exports';
import { consoleActions } from '../../state/store';
import { openPanel } from '../../shell/layout';
import { compilationDetails } from '../../ui/compilationReport';
import { Empty, Fold, IdentityFold, PanelBar } from '../../ui/blocks';
import { Button, Select } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './export.module.css';

const purposeNames = { authoring: '作者内容', runtime: '运行数据' } satisfies Record<SkillExportKind, string>;

export function ExportPanel() {
  const [purpose, setPurpose] = useState<SkillExportKind>('authoring');
  const { target, product, loading, documentRevision, sourceRevision } = useAuthoring(useShallow(state => ({
    target: state.target, product: authoringDocument(state).product, loading: authoringDocument(state).loading,
    documentRevision: authoringDocument(state).documentRevision, sourceRevision: authoringDocument(state).sourceRevision,
  })));
  const operations = useExports(state => state.operations);
  const skill = target?.kind === 'skill' ? target.name : null;
  const pending = operations.some(operation => operation.kind === 'preparing' && operation.skill === skill && operation.purpose === purpose);
  return <div className={styles.root}>
    <PanelBar wrap>
      <Select<SkillExportKind> label="导出用途" value={purpose} onChange={setPurpose} options={[
        { value: 'authoring', label: purposeNames.authoring }, { value: 'runtime', label: purposeNames.runtime },
      ]} />
      {skill !== null && <Button disabled={loading || pending} onClick={() => void exportActions.prepare({
        skill, purpose, editingView: authoringActions.editingView(),
      })}>{pending ? '准备中' : `导出${purposeNames[purpose]}`}</Button>}
    </PanelBar>
    {skill === null ? <Empty text="选择技能以导出当前作者内容或运行数据" /> : <section className={styles.source}>
      <strong>{skill}</strong>
      <ProductScope product={product} />
      <IdentityFold fields={[
        ...(documentRevision === null ? [] : [{ displayName: '作者输入上下文修订', value: documentRevision }]),
        ...(sourceRevision === null ? [] : [{ displayName: '技能编译来源闭包修订', value: sourceRevision }]),
      ]} />
      <p>{purpose === 'authoring'
        ? '保留作者树、UID、布局、Timeline 和输入方案，供继续编辑。包含本次编辑视图，不保存文档。'
        : '按本次编辑视图编译运行数据，下载编译清单及其配套内容文件。不保存其它作者内容。'}</p>
    </section>}
    <div className={styles.results}>
      {[...operations].reverse().map(operation => <ExportResult key={operation.id} operation={operation} />)}
    </div>
  </div>;
}

function ProductScope({ product }: { product: SkillEditingView['product'] }) {
  return product === null ? <div className={styles.note}>未选择 Product 作者范围</div>
    : <dl className={styles.scope}><dt>Product</dt><dd>{product.value}</dd><dt>版本</dt><dd>{product.revision}</dd></dl>;
}

function ExportResult({ operation }: { operation: ExportOperation }) {
  const compilation = operation.kind === 'ready' && operation.result.kind === 'runtime' ? operation.result.compilation : null;
  return <Fold title={`${operation.skill} · ${purposeNames[operation.purpose]} · ${new Date(operation.startedAt).toLocaleTimeString()}`} defaultOpen>
    {operation.kind === 'preparing' ? <><div role="status" className={styles.note}>准备中，等待宿主返回本次导出结果。</div>
      <ProductScope product={operation.product} /></>
      : operation.kind === 'failed' ? <ErrorLine text={`导出失败：${operation.reason}`} entryId={operation.entryId} />
        : <>
          <div role="status" className={styles.note}>本次{purposeNames[operation.result.kind]}已准备好。</div>
          <ProductScope product={operation.result.product} />
          <dl className={styles.scope}><dt>内容修订</dt><dd>{operation.result.sourceRevision}</dd></dl>
          <a className={styles.download} href={operation.result.downloadUrl} download={operation.result.fileName}>
            {operation.result.kind === 'authoring' ? '下载作者内容' : '下载运行编译清单'} · {operation.result.fileName}
          </a>
          {operation.result.kind === 'runtime' && <>
            <p className={styles.note}>运行清单需要配套内容文件；清单本身不包含可直接运行的完整闭包。</p>
            <Fold title={`配套文件（${operation.result.files.length}）`}>
              <div className={styles.files}>{operation.result.files.map(file => <a key={file.path} href={file.downloadUrl} download>
                {file.path}
              </a>)}</div>
            </Fold>
          </>}
          <IdentityFold fields={[
            { displayName: '导出修订', value: operation.result.sourceRevision },
            { displayName: 'Tick Rate', value: String(operation.result.tickRate) },
            { displayName: '预览 Scope', value: operation.result.execution?.previewScope ? JSON.stringify(operation.result.execution.previewScope) : '未选择' },
            { displayName: '实际运行 Scope', value: operation.result.execution?.runtimeScope ? JSON.stringify(operation.result.execution.runtimeScope) : '未选择' },
            { displayName: 'Effect Profile', value: operation.result.effectProfileGuid },
            ...(operation.result.kind === 'runtime' ? [{ displayName: '编译身份', value: operation.result.compilation.compilationKey }] : []),
          ]} />
          {compilation !== null && <Button variant="ghost" onClick={() => {
            const entryId = consoleActions.log('info', '导出', `${operation.skill} 运行数据编译结果`,
              `${compilationDetails(compilation).join('\n')}\n\n${JSON.stringify(compilation, null, 2)}`);
            consoleActions.focus(entryId);
            openPanel('console');
          }}>查看编译与发布结果</Button>}
        </>}
  </Fold>;
}
