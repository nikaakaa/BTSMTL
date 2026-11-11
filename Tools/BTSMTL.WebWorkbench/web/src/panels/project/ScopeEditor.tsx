import { useEffect, useState } from 'react';
import { isAbort } from '../../api/http';
import { resourceRegistrationApi, scopeQueryKey, type ScopeQuery } from '../../api/resourceRegistration';
import { playerApi } from '../../api/player';
import { previewApi } from '../../api/preview';
import type { PlayerPreviewContent } from '../../api/playerRecords';
import type { Sources, TimelineSource } from '../../api/types';
import { authoringActions, scopeDocument, useAuthoring } from '../../state/authoring';
import { consoleActions, contentActions, getState, selectedScene, useWorkbench } from '../../state/store';
import { projectActions } from '../../state/project';
import { playerService } from '../../state/player';
import { quickJobFailure, quickPreview } from '../../state/quickPreview';
import { Fold, PanelBar } from '../../ui/blocks';
import { Button } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import styles from './project.module.css';

type PreviewRefresh = { kind: 'idle' } | { kind: 'refreshing' } | { kind: 'submitted'; message: string };

export function ScopeEditor({ query }: { query: ScopeQuery }) {
  const document = useAuthoring(state => scopeDocument(state, query));
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [refresh, setRefresh] = useState<PreviewRefresh>({ kind: 'idle' });
  const player = useWorkbench(state => state.live?.player ?? null);
  const quickFailure = useWorkbench(state => quickJobFailure(state.quickPreview.job));
  const key = scopeQueryKey(query);
  useEffect(() => {
    const controller = new AbortController();
    authoringActions.openScope(query, controller.signal).catch(error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) });
    });
    return () => { controller.abort(); authoringActions.releaseScope(query); };
  }, [key]);
  if (document === undefined) return failure === null ? <div className={styles.note}>正在读取实际 Scope 的资源声明与来源修订。</div> : <ErrorLine {...failure} />;
  const source = document.source;
  return <div onFocusCapture={() => authoringActions.activateScope(query)} onPointerDownCapture={() => authoringActions.activateScope(query)}>
    <div className={styles.facts}><strong>执行 Scope · {source.scope}</strong>
      <small>{source.adapter} · 上下文 {source.context || '公共'}</small></div>
    {failure && <ErrorLine {...failure} />}
    <PanelBar wrap>
      <Button onClick={async () => {
        try { await authoringActions.openScope(query, undefined, true); setFailure(null); }
        catch (error) { setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Scope', error) }); }
      }}>重读 Scope 来源</Button>
    </PanelBar>
    <Fold title="原生动画绑定（只读）" defaultOpen>
      <div className={styles.note}>绑定与修订来自 Editor 发布的执行范围目录；参数在对应原生资源中编辑，实际准备结果由预览 Player 报告。</div>
      {source.publishedResources.length === 0 && <div className={styles.note}>此执行范围没有角色原生动画绑定。</div>}
      {source.publishedResources.map(binding => <div key={binding.id} className={styles.facts}>
        <strong>{binding.displayName}</strong>
        {binding.kind === 'unavailable' ? <span className={styles.issue}>{binding.reason}</span> : <>
          <code>{binding.resource.path}</code>
          <small>对象身份：{binding.resource.assetToken}</small>
          <small>资源修订：{binding.resource.revision}</small>
          <PanelBar><Button onClick={() => projectActions.locateResourceFile(binding.resource.path)}>定位正式资源文件</Button></PanelBar>
        </>}
      </div>)}
    </Fold>
    <Fold title="预览准备与刷新" defaultOpen>
      <div className={styles.note}>先在原生资源中保存并完成正式发布，再刷新使用此 Profile 的当前预览。这里不生成资源或创建播放器。</div>
      {player !== null && <div className={styles.facts}><small>所选 Player 报告：{player.state}</small>
        {player.error !== null && <span className={styles.issue}>{player.error}</span>}</div>}
      {quickFailure !== null && <div className={styles.issue}>快速预览准备：{quickFailure}</div>}
      <PanelBar><Button disabled={refresh.kind === 'refreshing'} onClick={async () => {
        setRefresh({ kind: 'refreshing' }); setFailure(null);
        try {
          const message = await refreshPublishedPreview(query);
          await authoringActions.openScope(query, undefined, true);
          setRefresh({ kind: 'submitted', message });
        } catch (error) {
          setRefresh({ kind: 'idle' });
          setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('原生动画预览', error) });
        }
      }}>{refresh.kind === 'refreshing' ? '正在核对并刷新预览…' : '刷新受影响的当前预览'}</Button></PanelBar>
      {refresh.kind === 'submitted' && <div className={styles.note}>{refresh.message}</div>}
    </Fold>
    <Fold title="资源领域声明" defaultOpen>{source.resourceDeclarations.map(item => <div key={item.domain} className={styles.facts}>
      <strong>{item.domain}</strong><small>{item.source === 'AuthoringDocument' ? '工作区作者文档' : `实际对象类型：${item.objectType}`}</small>
      {!item.availability.available && <span className={styles.issue}>{item.availability.reason}</span>}
    </div>)}</Fold>
    <div className={styles.note}>执行范围提供当前载体的资源上下文；映射与容量通过实际 Profile 编辑并保存。</div>
    <Fold title="Scope 身份"><div className={styles.facts}><small>来源修订：{source.revision}</small></div></Fold>
  </div>;
}

function timelineScene(sources: Sources, timeline: TimelineSource) {
  const scenes = sources.quickScenes.filter(scene => scene.sceneGuid === timeline.scene.guid && scene.sceneRevision === timeline.scene.dependencyHash);
  if (scenes.length !== 1)
    throw new Error('Timeline 没有对应修订的正式场景发布，请先通过正式入口重新发布 Timeline 与场景。');
  return scenes[0].publication;
}

async function refreshPublishedPreview(query: ScopeQuery): Promise<string> {
  const before = getState();
  const sources = await previewApi.sources();
  if (before.previewMode === 'quick') {
    const selection = before.contentSelection;
    if (selection === null) throw new Error('先选择正式场景或 Timeline，并读取快速预览。');
    const description = before.quickPreview.description;
    if (description.kind !== 'ready') throw new Error('先读取当前快速预览入口，再刷新它使用的原生资源。');
    let scene;
    if (selection.kind === 'scene') {
      scene = selectedScene({ sources, contentSelection: selection });
      if (scene === undefined) throw new Error('所选场景已不在正式发布目录中。');
    } else {
      const timeline = sources.timelines.find(item => item.content.package === selection.package);
      if (timeline === undefined) throw new Error('所选 Timeline 已不在正式发布目录中。');
      scene = timelineScene(sources, timeline);
    }
    const reply = await resourceRegistrationApi.preview(query, scene.path);
    if (!reply.available) throw new Error(reply.reason);
    if (!reply.actors.includes(description.reply.data.actorId))
      throw new Error('当前快速预览角色没有使用此 Profile，不刷新无关预览。');
    if (getState().previewMode !== before.previewMode || getState().contentSelection !== selection)
      throw new Error('预览选择已经变化，请按当前入口重新刷新。');
    contentActions.setSources(sources);
    await quickPreview.readSource();
    return `已通知现有快速预览重新准备；受影响角色：${reply.actors.join('、')}。准备结果见快速预览状态。`;
  }
  if (before.previewMode !== 'real' || before.liveId === null)
    throw new Error('先选择真实预览档位并载入一个 Player。');
  const live = await playerApi.live(before.liveId);
  const target = live.previewTarget;
  if (target === null) throw new Error('所选 Player 尚无已确认载入的内容，请先载入正式场景或 Timeline。');
  const content = target.content;
  let next: PlayerPreviewContent;
  let scene;
  if (content.kind === 'scene') {
    scene = { path: content.scene, revision: content.revision };
    next = content;
  } else if (content.kind === 'timeline') {
    const timeline = sources.timelines.find(item => item.content.timeline === content.timeline);
    if (timeline === undefined) throw new Error('Player 当前 Timeline 已不在正式发布目录中。');
    scene = timelineScene(sources, timeline);
    next = timeline.content;
  } else throw new Error('所选 Player 当前载入快速预览内容，请使用快速预览档位刷新。');
  const reply = await resourceRegistrationApi.preview(query, scene.path);
  if (!reply.available) throw new Error(reply.reason);
  if (getState().liveId !== target.liveId || getState().live?.player?.source?.loadCommandId !== target.loadCommandId)
    throw new Error('所选 Player 或载入内容已经变化，请重新核对后刷新。');
  if (next.kind === 'scene') next = { ...next, revision: reply.scene.revision };
  contentActions.setSources(sources);
  const receipt = await playerService.loadContent(next);
  if (receipt === null) throw new Error('Player 未完成本次刷新请求，具体原因见 Player 记录。');
  if (receipt.stage !== 'applied' && receipt.stage !== 'submitted')
    throw new Error(receipt.ack?.error ?? 'Player 拒绝采用本次已发布内容。');
  return `已${receipt.stage === 'applied' ? '确认载入命令' : '提交刷新命令'}；受影响角色：${reply.actors.join('、')}。实际准备状态由所选 Player 报告。`;
}
