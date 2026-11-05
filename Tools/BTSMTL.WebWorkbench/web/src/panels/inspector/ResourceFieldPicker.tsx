import { createContext, useContext, useEffect } from 'react';
import { authoringApi, type AdapterCatalogItem, type ReferenceChoice } from '../../api/authoring';
import type { ResourceReferenceMetadata } from '../../api/resourceRegistration';
import { adapterCatalogCache, useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringDocument, useAuthoring } from '../../state/authoring';
import type { ReferenceCatalog, ResourceObjectFieldSelection } from '../../state/referenceSelection';
import { useResourceScope } from '../../state/resourceScope';
import type { JsonObject, JsonValue } from '../../state/skillJson';
import { ReferencePicker } from './ReferencePicker';

export const ResourceScopeContext = createContext<string | null | undefined>(undefined);

export function ResourceFieldPicker({ id, label, metadata, value, configuration, onChoose }: {
  id: string; label: string; metadata: ResourceReferenceMetadata; value: JsonValue | undefined;
  configuration: JsonObject; onChoose(choice: ReferenceChoice): void;
}) {
  const inherited = useContext(ResourceScopeContext);
  const preview = useAuthoring(state => authoringDocument(state).execution.previewScope);
  const scopeValue = inherited === undefined ? preview : inherited;
  const scope = useResourceScope(scopeValue);
  const catalog = useAdapterCatalog();
  useEffect(() => { void adapterCatalogCache.ensure(authoringApi.catalog); }, []);
  const context = scope.kind === 'ready' ? scope.scope.context : '';
  const makeChoice = (item: Pick<AdapterCatalogItem, 'value' | 'display' | 'adapter' | 'context'>, conflict: string | null): ReferenceChoice => ({
    value: item.value, name: item.display, detail: item.context || '公共', source: item.adapter, conflict,
    targets: [{ kind: 'Resource', domain: metadata.domain, value: item.value, context }], associated: [],
  });
  let choices: ReferenceCatalog;
  if (!metadata.availability.available) choices = { kind: 'unavailable', reason: metadata.availability.reason };
  else if (catalog.kind === 'failed') choices = { kind: 'unavailable', reason: catalog.failure.text };
  else if (catalog.kind !== 'ready') choices = { kind: 'unavailable', reason: '正在读取正式适配者目录。' };
  else if (scope.kind !== 'ready' && (metadata.selection === 'mapped' || scopeValue !== null))
    choices = { kind: 'unavailable', reason: scope.kind === 'loading' ? '正在读取实际执行 Scope。' : scope.reason };
  else {
    const values = new Map<string, AdapterCatalogItem[]>();
    for (const group of catalog.snapshot.domains.filter(group => group.domain === metadata.domain))
      for (const item of group.items.filter(item => item.context === context || item.context === ''))
        values.set(item.value, [...values.get(item.value) ?? [], { ...item, adapter: group.adapter, domain: group.domain }]);
    const items = [...values.values()].flatMap(entries => {
      const exact = entries.filter(item => item.context === context);
      const selected = exact.length !== 0 ? exact : entries;
      const conflict = selected.length > 1 ? `领域 ${metadata.domain}、引用 ${selected[0].value}、上下文 ${context} 命中多条正式目录。` : null;
      return selected.map(item => makeChoice(item, conflict));
    });
    choices = { kind: 'ready', referenceKind: metadata.domain, items,
      invalidReason: `当前引用不在领域 ${metadata.domain}、上下文 ${context} 的正式目录中，原字符串已保留。` };
  }
  const objects: ResourceObjectFieldSelection | undefined = metadata.selection === 'asset' && metadata.objectType !== null && scope.kind === 'ready'
    ? { kind: 'available', scope: { adapter: scope.scope.adapter, context: scope.scope.context,
      domain: metadata.domain, objectType: metadata.objectType }, valueMember: 'value',
      choose: candidate => onChoose(makeChoice({ value: candidate.value, display: candidate.name, adapter: scope.scope.adapter, context }, null)) } : undefined;
  return <ReferencePicker id={id} label={label} domain={metadata.domain} selection={metadata.selection} value={value}
    resourceContext={scope.kind === 'ready' ? { adapter: scope.scope.adapter, context: scope.scope.context } : null}
    configuration={configuration} catalog={choices} onChoose={onChoose} resourceObjects={objects} />;
}
