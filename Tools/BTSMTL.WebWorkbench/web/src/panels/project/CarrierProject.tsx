import { useEffect, useMemo, useState } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type AuthoringCarrier, type AuthoringCarriers, type CarrierResource } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import styles from './project.module.css';

interface ResourceRow { name: string; kind: string; source: string; key: string; identity: string; detail: string }

function resourcesOf(carrier: AuthoringCarrier): ResourceRow[] {
  const rows: ResourceRow[] = [];
  const add = (kind: string, source: string, resources: CarrierResource[]) => {
    rows.push(...resources.map(resource => ({ ...resource, kind, source, detail: '' })));
  };
  if (carrier.vfx !== null) {
    const profile = carrier.vfx;
    add('特效资源', profile.source, profile.resources);
    rows.push(...profile.anchors.map(anchor => ({ name: anchor.key, key: anchor.key, kind: '特效锚点', source: profile.source, identity: '', detail: anchor.bone })));
    rows.push(...profile.stateModifiers.map(modifier => ({ name: modifier.id, key: modifier.id, kind: '特效状态修饰', source: profile.source, identity: '', detail: modifier.resourceIds.join(' + ') })));
  }
  if (carrier.camera !== null) {
    const profile = carrier.camera;
    add('相机序列', profile.source, profile.sequences);
    rows.push(...profile.effects.map(effect => ({ ...effect.resource, kind: '相机效果', source: profile.source, detail: effect.kind })));
    rows.push(...profile.targets.map(target => ({ name: target.key, key: target.key, kind: '相机目标', source: profile.source, identity: '',
      detail: [target.space, target.anchorKey, target.aimPointKey, target.preferredBoneKey].filter(Boolean).join(' / ') })));
  }
  if (carrier.audio !== null) {
    const profile = carrier.audio;
    add('音频事件', profile.source, profile.events);
    add('音频规则', profile.source, profile.rules);
    add('音频参数', profile.source, profile.parameters);
  }
  if (carrier.input !== null) {
    const profile = carrier.input;
    rows.push(...profile.keys.map(key => ({ name: key.name, key: key.name, kind: '输入键', source: profile.source, identity: key.actionIdentity, detail: key.kind })));
  }
  return rows;
}

export function CarrierProject() {
  const [catalog, setCatalog] = useState<AuthoringCarriers | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const { target, preview } = useAuthoring(useShallow(state => ({ target: state.target, preview: authoringDocument(state).previewDefinitionGuid })));
  useEffect(() => {
    const controller = new AbortController();
    setCatalog(null);
    setFailure(null);
    authoringApi.carriers(controller.signal).then(value => {
      useAuthoring.setState({ carrierRevision: value.authoringRevision });
      setCatalog(value);
    }, error => {
      if (!isAbort(error)) setFailure({ text: '载体与资源目录读取失败', entryId: consoleActions.error('Project', error) });
    });
    return () => controller.abort();
  }, [refresh]);
  const shown = useMemo(() => {
    const query = search.toLocaleLowerCase();
    return (catalog?.items ?? []).filter(item => `${item.name} ${item.source} ${item.identity}`.toLocaleLowerCase().includes(query));
  }, [catalog, search]);
  const carrier = catalog?.items.find(item => item.identity === selected) ?? null;
  const currentPreview = catalog?.items.find(item => item.identity === preview) ?? null;
  return <div className={styles.catalog}>
    <div className={styles.browser}>
      <PanelBar>
        <TextInput aria-label="搜索载体" placeholder="搜索载体名称、来源" value={search} className={styles.search} onChange={event => setSearch(event.currentTarget.value)} />
        <Button variant="ghost" onClick={() => setRefresh(value => value + 1)}>刷新</Button>
      </PanelBar>
      {failure ? <ErrorLine {...failure} /> : catalog === null ? <Empty text="正在读取已保存载体" /> :
        !catalog.availability.available ? <Empty text={catalog.availability.reason} /> :
          <VirtualList className={styles.list} count={shown.length} rowHeight={48} label="已保存载体" render={index => {
            const item = shown[index];
            return <button className={styles.row} aria-selected={item.identity === selected} onClick={() => setSelected(item.identity)}>
              <span>{item.name}{item.identity === preview ? ' · 当前预览载体' : ''}</span><small>{item.source}</small>
            </button>;
          }} />}
      <div className={styles.note}>{shown.length} 个载体 · 配置只读</div>
    </div>
    <div className={styles.details}>
      <div className={styles.facts}>
        <span>{target?.kind === 'skill' ? `${target.name} 的预览载体` : '打开技能后可选择预览载体'}</span>
        <strong>{preview === null ? '未选择' : currentPreview?.name ?? '原选择已失效，身份已保留'}</strong>
        {preview !== null && <Button variant="ghost" onClick={() => authoringActions.selectPreviewCarrier(null)}>清除选择</Button>}
      </div>
      {carrier ? <>
        <PanelBar><Button disabled={target?.kind !== 'skill' || preview === carrier.identity}
          onClick={() => authoringActions.selectPreviewCarrier(carrier.identity)}>设为当前技能预览载体</Button></PanelBar>
        <div className={styles.facts}><strong>{carrier.name}</strong><code>{carrier.source}</code><small>{carrier.identity}</small>
          {carrier.vfx === null && <span className={styles.issue}>未配置特效 Profile</span>}
          {carrier.camera === null && <span className={styles.issue}>未配置相机 Profile</span>}
          {carrier.audio === null && <span className={styles.issue}>未配置音频 Profile</span>}
          {carrier.input === null && <span className={styles.issue}>未配置输入 Profile</span>}
        </div>
        {catalog && !catalog.locateAvailability.available && <div className={styles.note}>Unity 定位不可用：{catalog.locateAvailability.reason}</div>}
        <CarrierResources key={carrier.identity} carrier={carrier} />
      </> : <Empty text="选择载体查看表现资源与锚点" />}
    </div>
  </div>;
}

function CarrierResources({ carrier }: { carrier: AuthoringCarrier }) {
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState<string | null>(null);
  const [selected, setSelected] = useState<ResourceRow | null>(null);
  const rows = useMemo(() => resourcesOf(carrier), [carrier]);
  const kinds = useMemo(() => [...new Set(rows.map(row => row.kind))], [rows]);
  const shown = useMemo(() => {
    const query = search.toLocaleLowerCase();
    return rows.filter(row => (kind === null || row.kind === kind) && `${row.name} ${row.key} ${row.detail} ${row.source}`.toLocaleLowerCase().includes(query));
  }, [rows, search, kind]);
  return <div className={styles.resources}>
    <PanelBar>
      <TextInput aria-label="搜索载体资源" placeholder="搜索资源或锚点" value={search} className={styles.search} onChange={event => setSearch(event.currentTarget.value)} />
      <MenuButton label="载体资源类别" caret entries={[
        { label: '全部类别', onSelect: () => setKind(null) },
        ...kinds.map(value => ({ label: value, onSelect: () => setKind(value) })),
      ]}>{kind ?? '全部类别'}</MenuButton>
    </PanelBar>
    <VirtualList className={styles.list} count={shown.length} rowHeight={44} label="载体只读资源" render={index => {
      const row = shown[index];
      return <button className={styles.row} aria-selected={row === selected} onClick={() => setSelected(row)}>
        <span>{row.name}</span><small>{row.kind} · {row.detail || row.key}</small>
      </button>;
    }} />
    {selected && <div className={styles.facts}><strong>{selected.name} · {selected.kind}</strong>
      <code>{selected.source}</code><small>{selected.key}{selected.identity ? ` · ${selected.identity}` : ''}</small>
      {selected.detail && <span>{selected.detail}</span>}
    </div>}
    <div className={styles.note}>{shown.length} / {rows.length} 项 · 引用绑定使用上方当前字段的正式候选</div>
  </div>;
}
