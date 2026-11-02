import { useEffect, useMemo, useState, type ComponentProps } from 'react';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type AuthoringCarrier, type AuthoringCarriers, type CarrierResource, type ProfileEntryTarget } from '../../api/authoring';
import { isAbort } from '../../api/http';
import { authoringActions, authoringDocument, useAuthoring } from '../../state/authoring';
import { referenceTargetKey, useReferenceSelection, writeReferenceDrag } from '../../state/referenceSelection';
import { consoleActions } from '../../state/store';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, MenuButton, TextInput } from '../../ui/controls';
import { ErrorLine } from '../../ui/ErrorLine';
import { VirtualList } from '../../ui/VirtualList';
import { TargetReferrers } from './TargetReferrers';
import { LocateAssetButton, type AssetLocateTarget } from './LocateAssetButton';
import { BusinessKeyRename, BusinessKeyRenameReceipt, type BusinessKeyRenameCompletion } from './BusinessKeyRename';
import styles from './project.module.css';

interface ResourceRow {
  name: string; kind: string; source: string; key: string; identity: string | null; detail: string;
  domain: ProfileEntryTarget['domain']; entryKind: string;
  location: AssetLocateTarget;
}

const resourceLocation = (resource: CarrierResource): AssetLocateTarget => resource.identity === ''
  ? { kind: 'unavailable', reason: '该资源尚未绑定 Unity 资产' } : { kind: 'asset', identity: resource.identity };

const matchesTarget = (row: ResourceRow, target: ProfileEntryTarget) => row.source === target.profileSource &&
  row.domain === target.domain && row.entryKind === target.entryKind && row.key === target.key && row.identity === target.resourceIdentity;

const resourceTarget = (carrier: AuthoringCarrier, row: ResourceRow): ProfileEntryTarget => ({
  kind: 'ProfileEntry', carrierIdentity: carrier.identity, profileSource: row.source, domain: row.domain,
  entryKind: row.entryKind, key: row.key, resourceIdentity: row.identity,
});

function resourcesOf(carrier: AuthoringCarrier): ResourceRow[] {
  const rows: ResourceRow[] = [];
  const add = (kind: string, source: string, domain: ResourceRow['domain'], entryKind: string, resources: CarrierResource[],
    location: (resource: CarrierResource) => AssetLocateTarget) => {
    rows.push(...resources.map(resource => ({ ...resource, kind, source, domain, entryKind, detail: '', location: location(resource) })));
  };
  if (carrier.vfx !== null) {
    const profile = carrier.vfx;
    const location: AssetLocateTarget = { kind: 'asset', identity: profile.identity };
    add('特效资源', profile.source, 'Vfx', 'vfx-resource', profile.resources, resourceLocation);
    rows.push(...profile.anchors.map((anchor): ResourceRow => ({ name: anchor.key, key: anchor.key, kind: '特效锚点', source: profile.source,
      domain: 'Vfx', entryKind: 'vfx-anchor', identity: null, detail: anchor.bone, location })));
    rows.push(...profile.stateModifiers.map((modifier): ResourceRow => ({ name: modifier.id, key: modifier.id, kind: '特效状态修饰', source: profile.source,
      domain: 'Vfx', entryKind: 'vfx-state-modifier', identity: null, detail: modifier.resourceIds.join(' + '), location })));
  }
  if (carrier.camera !== null) {
    const profile = carrier.camera;
    const location: AssetLocateTarget = { kind: 'asset', identity: profile.identity };
    add('相机序列', profile.source, 'Camera', 'camera-sequence', profile.sequences, resourceLocation);
    rows.push(...profile.effects.map((effect): ResourceRow => ({ ...effect.resource, kind: '相机效果', source: profile.source,
      domain: 'Camera', entryKind: `camera-effect-${effect.kind}`, detail: effect.kind, location: resourceLocation(effect.resource) })));
    rows.push(...profile.targets.map((target): ResourceRow => ({ name: target.key, key: target.key, kind: '相机目标', source: profile.source,
      domain: 'Camera', entryKind: 'camera-target', identity: null,
      detail: [target.space, target.anchorKey, target.aimPointKey, target.preferredBoneKey].filter(Boolean).join(' / '), location })));
  }
  if (carrier.audio !== null) {
    const profile = carrier.audio;
    const location: AssetLocateTarget = { kind: 'asset', identity: profile.identity };
    add('音频事件', profile.source, 'Audio', 'audio-resource', profile.events, () => location);
    add('音频规则', profile.source, 'Audio', 'audio-rule', profile.rules, () => location);
    add('音频参数', profile.source, 'Audio', 'audio-parameter', profile.parameters, () => location);
    rows.push(...Array.from({ length: profile.actionSoundSlotCount }, (_, slot): ResourceRow => ({
      name: `声音通道 ${slot}`, key: String(slot), kind: '动作声音通道', source: profile.source,
      domain: 'Audio', entryKind: 'audio-sound', identity: null, detail: '', location,
    })));
  }
  if (carrier.input !== null) {
    const profile = carrier.input;
    const location: AssetLocateTarget = { kind: 'asset', identity: profile.identity };
    rows.push(...profile.keys.map((key): ResourceRow => ({ name: key.name, key: key.name, kind: '输入键', source: profile.source,
      domain: 'Input', entryKind: 'preview-input-key', identity: key.actionIdentity, detail: key.kind, location })));
  }
  return rows;
}

export function CarrierProject({ target: focusTarget }: { target: ProfileEntryTarget | null }) {
  const [catalog, setCatalog] = useState<AuthoringCarriers | null>(null);
  const [failure, setFailure] = useState<{ text: string; entryId: number | null } | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [migration, setMigration] = useState<BusinessKeyRenameCompletion | null>(null);
  const carrierRevision = useAuthoring(state => state.carrierRevision);
  useEffect(() => { if (focusTarget !== null) { setSelected(focusTarget.carrierIdentity); setSearch(''); } }, [focusTarget]);
  const { target, preview } = useAuthoring(useShallow(state => ({ target: state.target, preview: authoringDocument(state).previewDefinitionGuid })));
  useEffect(() => {
    if (catalog !== null && catalog.authoringRevision === carrierRevision) return;
    const controller = new AbortController();
    setFailure(null);
    authoringApi.carriers(controller.signal).then(value => {
      setCatalog(value);
      useAuthoring.setState({ carrierRevision: value.authoringRevision });
    }, error => {
      if (!isAbort(error)) setFailure({ text: error instanceof Error ? error.message : String(error), entryId: consoleActions.error('Project', error) });
    });
    return () => controller.abort();
  }, [refresh, carrierRevision]);
  const shown = useMemo(() => {
    const query = search.toLocaleLowerCase();
    return (catalog?.items ?? []).filter(item => `${item.name} ${item.source} ${item.identity}`.toLocaleLowerCase().includes(query));
  }, [catalog, search]);
  const carrier = catalog?.items.find(item => item.identity === selected) ?? null;
  const currentPreview = catalog?.items.find(item => item.identity === preview) ?? null;
  const selectedIndex = shown.findIndex(item => item.identity === selected);
  const onMigrationRefreshed: ComponentProps<typeof BusinessKeyRename>['onRefreshed'] = (result, value) => {
    setMigration({ result, refresh: value });
    if (value.catalog !== null) {
      setCatalog(value.catalog);
      useAuthoring.setState({ carrierRevision: value.catalog.authoringRevision });
    }
  };
  return <div className={styles.catalog}>
    <div className={styles.browser}>
      <PanelBar>
        <TextInput aria-label="搜索载体" placeholder="搜索载体名称、来源" value={search} className={styles.search} onChange={event => setSearch(event.currentTarget.value)} />
        <Button variant="ghost" onClick={() => { setCatalog(null); setRefresh(value => value + 1); }}>刷新</Button>
      </PanelBar>
      {failure ? <ErrorLine {...failure} /> : catalog === null ? <Empty text="正在读取已保存载体" /> :
        !catalog.availability.available ? <Empty text={catalog.availability.reason} /> :
          <VirtualList className={styles.list} count={shown.length} rowHeight={48} label="已保存载体"
            revealKey={JSON.stringify(focusTarget)} revealIndex={selectedIndex < 0 ? undefined : selectedIndex} render={index => {
            const item = shown[index];
            return <button className={styles.row} aria-selected={item.identity === selected} onClick={() => setSelected(item.identity)}>
              <span>{item.name}{item.identity === preview ? ' · 当前预览载体' : ''}</span><small>{item.source}</small>
            </button>;
          }} />}
      <div className={styles.note}>{shown.length} 个载体</div>
    </div>
    <div className={styles.details}>
      <div className={styles.facts}>
        <span>{target?.kind === 'skill' ? `${target.name} 的预览载体` : '打开技能后可选择预览载体'}</span>
        <strong>{preview === null ? '未选择' : currentPreview?.name ?? '原选择已失效，身份已保留'}</strong>
        {preview !== null && <Button variant="ghost" onClick={() => authoringActions.selectPreviewCarrier(null)}>清除选择</Button>}
      </div>
      {carrier ? <>
        <PanelBar><Button disabled={target?.kind !== 'skill' || preview === carrier.identity}
          onClick={() => authoringActions.selectPreviewCarrier(carrier.identity)}>设为当前技能预览载体</Button>
          <LocateAssetButton key={carrier.identity} target={{ kind: 'asset', identity: carrier.identity }} /></PanelBar>
        <div className={styles.facts}><strong>{carrier.name}</strong><code>{carrier.source}</code><small>{carrier.identity}</small>
          {carrier.vfx === null && <span className={styles.issue}>未配置特效 Profile</span>}
          {carrier.camera === null && <span className={styles.issue}>未配置相机 Profile</span>}
          {carrier.audio === null && <span className={styles.issue}>未配置音频 Profile</span>}
          {carrier.input === null && <span className={styles.issue}>未配置输入 Profile</span>}
        </div>
        {migration?.result.target.carrierIdentity === carrier.identity && <BusinessKeyRenameReceipt completion={migration}
          onRefreshed={onMigrationRefreshed} />}
        <CarrierResources key={carrier.identity} carrier={carrier} target={focusTarget?.carrierIdentity === carrier.identity ? focusTarget : null}
          migration={migration?.result.target.carrierIdentity === carrier.identity ? migration : null}
          onSaved={result => setMigration({ result, refresh: null })}
          onRefreshed={onMigrationRefreshed} />
      </> : <Empty text={catalog?.availability.available && focusTarget !== null && selected === focusTarget.carrierIdentity
        ? `引用的来源载体已失效，原身份已保留：${focusTarget.carrierIdentity}` : '选择载体查看表现资源与锚点'} />}
    </div>
  </div>;
}

function CarrierResources({ carrier, target, migration, onSaved, onRefreshed }: {
  carrier: AuthoringCarrier; target: ProfileEntryTarget | null;
  migration: BusinessKeyRenameCompletion | null;
  onSaved: ComponentProps<typeof BusinessKeyRename>['onSaved'];
  onRefreshed: ComponentProps<typeof BusinessKeyRename>['onRefreshed'];
}) {
  const [search, setSearch] = useState('');
  const [kind, setKind] = useState<string | null>(null);
  const [selection, setSelection] = useState<ProfileEntryTarget | null>(null);
  const rows = useMemo(() => resourcesOf(carrier), [carrier]);
  const selected = selection === null ? null : rows.find(row => matchesTarget(row, selection)) ?? null;
  const field = useReferenceSelection(state => state.field);
  const choicesFor = (row: ResourceRow) => {
    if (field?.catalog.kind !== 'ready' || field.catalog.referenceKind !== row.entryKind) return [];
    const key = referenceTargetKey(resourceTarget(carrier, row));
    return field.catalog.items.filter(choice => choice.targets.some(value => referenceTargetKey(value) === key));
  };
  useEffect(() => {
    if (target !== null) {
      setSearch('');
      setKind(null);
      setSelection(target);
    }
  }, [target]);
  useEffect(() => {
    if (migration === null || migration.refresh === null || migration.refresh.catalog === null) return;
    const { result } = migration;
    const renamed = rows.find(row => row.source === result.target.profileSource && row.domain === result.target.domain &&
      row.entryKind === result.target.entryKind && row.key === result.newKey);
    if (renamed !== undefined) setSelection(current => current !== null && referenceTargetKey(current) === referenceTargetKey(result.target)
      ? resourceTarget(carrier, renamed) : current);
  }, [migration, rows, carrier]);
  const kinds = useMemo(() => [...new Set(rows.map(row => row.kind))], [rows]);
  const shown = useMemo(() => {
    const query = search.toLocaleLowerCase();
    return rows.filter(row => (kind === null || row.kind === kind) && `${row.name} ${row.key} ${row.detail} ${row.source}`.toLocaleLowerCase().includes(query));
  }, [rows, search, kind]);
  const selectedIndex = selected === null ? -1 : shown.indexOf(selected);
  return <div className={styles.resources}>
    <PanelBar>
      <TextInput aria-label="搜索载体资源" placeholder="搜索资源或锚点" value={search} className={styles.search} onChange={event => setSearch(event.currentTarget.value)} />
      <MenuButton label="载体资源类别" caret entries={[
        { label: '全部类别', onSelect: () => setKind(null) },
        ...kinds.map(value => ({ label: value, onSelect: () => setKind(value) })),
      ]}>{kind ?? '全部类别'}</MenuButton>
    </PanelBar>
    <VirtualList className={styles.list} count={shown.length} rowHeight={44} label="载体资源"
      revealKey={JSON.stringify(target)} revealIndex={selectedIndex < 0 ? undefined : selectedIndex} render={index => {
      const row = shown[index];
      const choices = choicesFor(row);
      return <button className={styles.row} aria-selected={row === selected} onClick={() => setSelection(resourceTarget(carrier, row))} draggable={choices.length === 1}
        onDragStart={event => { if (field?.catalog.kind === 'ready' && choices.length === 1)
          writeReferenceDrag(event.dataTransfer, field.catalog.referenceKind, choices[0]); }}>
        <span>{row.name}</span><small>{row.kind} · {row.detail || row.key}</small>
      </button>;
    }} />
    {selected && <div className={styles.facts}><strong>{selected.name} · {selected.kind}</strong>
      <code>{selected.source}</code><small>{selected.key}{selected.identity ? ` · ${selected.identity}` : ''}</small>
      {selected.detail && <span>{selected.detail}</span>}
      <LocateAssetButton key={selected.location.kind === 'asset' ? selected.location.identity : JSON.stringify(resourceTarget(carrier, selected))}
        target={selected.location} />
      {selected.entryKind !== 'audio-sound' && <BusinessKeyRename key={referenceTargetKey(resourceTarget(carrier, selected))}
        target={resourceTarget(carrier, selected)} onSaved={onSaved} onRefreshed={onRefreshed} />}
      {field?.catalog.kind === 'ready' && choicesFor(selected).map((choice, index) => <Button key={index} variant="ghost"
        onClick={() => field.choose(choice)}>绑定到 {field.name}：{choice.name}{choice.conflict ? ` · ${choice.conflict}` : ''}</Button>)}
    </div>}
    {selected && <TargetReferrers target={resourceTarget(carrier, selected)} />}
    {selection !== null && selected === null && <div className={styles.issue}>引用的配置条目已失效，原身份已保留：{selection.key}</div>}
    <div className={styles.note}>{shown.length} / {rows.length} 项 · 引用绑定使用上方当前字段的正式候选</div>
  </div>;
}
