import { createContext, useContext } from 'react';
import type { AdapterCatalogItem, ReferenceChoice } from '../../api/authoring';
import type { ResourceReferenceMetadata } from '../../api/resourceRegistration';
import { resourceCatalogChoices, useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringDocument, useAuthoring } from '../../state/authoring';
import { resourceReferenceChoice, type ReferenceCatalog, type ResourceObjectFieldSelection } from '../../state/referenceSelection';
import { useResourceScope } from '../../state/resourceScope';
import type { JsonObject, JsonValue } from '../../state/skillJson';
import { ReferencePicker } from './ReferencePicker';

export const ResourceScopeContext = createContext<string | null | undefined>(undefined);

interface ResourceFieldPickerProps {
  id: string; label: string; metadata: ResourceReferenceMetadata; value: JsonValue | undefined;
  configuration: JsonObject; onChoose(choice: ReferenceChoice): void;
}

export function ResourceFieldPicker(props: ResourceFieldPickerProps) {
  return props.metadata.source === 'AuthoringDocument' ? <DocumentFieldPicker {...props} /> : <AdapterFieldPicker {...props} />;
}

function DocumentFieldPicker({ id, label, metadata, value, configuration, onChoose }: ResourceFieldPickerProps) {
  const directory = useAuthoring(state => state.directory);
  const reason = useAuthoring(state => state.unavailableReason);
  const documents = directory?.filter(document => document.contentDomain === metadata.domain) ?? [];
  const choices: ReferenceCatalog = !metadata.availability.available
    ? { kind: 'unavailable', reason: metadata.availability.reason }
    : directory === null || reason !== null ? { kind: 'unavailable', reason: reason ?? '正在读取工作区作者文档。' }
      : { kind: 'ready', referenceKind: metadata.domain, invalidReason: `当前引用不在领域 ${metadata.domain} 的工作区作者文档中，原字符串已保留。`,
        items: documents.map(document => ({ value: document.sourceIdentity, name: document.identity,
          detail: document.documentPath, source: document.rootFile === null ? document.documentPath : `${document.documentPath}/${document.rootFile}`,
          conflict: documents.filter(other => other.sourceIdentity === document.sourceIdentity).length > 1
            ? `领域 ${metadata.domain} 的引用 ${document.sourceIdentity} 命中多个正式作者文档。` : null,
          targets: [], associated: [] })) };
  return <ReferencePicker id={id} label={label} value={value} configuration={configuration} catalog={choices} onChoose={onChoose} />;
}

function AdapterFieldPicker({ id, label, metadata, value, configuration, onChoose }: ResourceFieldPickerProps) {
  const inherited = useContext(ResourceScopeContext);
  const preview = useAuthoring(state => authoringDocument(state).execution.previewScope);
  const scopeValue = inherited === undefined ? preview : inherited;
  const scope = useResourceScope(scopeValue);
  const catalog = useAdapterCatalog();
  const context = scope.kind === 'ready' ? scope.scope.context : '';
  const makeChoice = (item: Pick<AdapterCatalogItem, 'value' | 'display' | 'adapter' | 'context'>, conflict: string | null) =>
    resourceReferenceChoice({ ...item, domain: metadata.domain }, conflict);
  let choices: ReferenceCatalog;
  if (!metadata.availability.available) choices = { kind: 'unavailable', reason: metadata.availability.reason };
  else if (catalog.kind === 'failed') choices = { kind: 'unavailable', reason: catalog.failure.text };
  else if (catalog.kind !== 'ready') choices = { kind: 'unavailable', reason: '正在读取正式适配者目录。' };
  else if (scope.kind !== 'ready' && (metadata.selection === 'mapped' || scopeValue !== null))
    choices = { kind: 'unavailable', reason: scope.kind === 'loading' ? '正在读取实际执行 Scope。' : scope.reason };
  else choices = resourceCatalogChoices(catalog.snapshot, metadata.domain, context);
  const objects: ResourceObjectFieldSelection | undefined = metadata.selection === 'asset' && metadata.objectType !== null && scope.kind === 'ready'
    ? { kind: 'available', scope: { adapter: scope.scope.adapter, context: scope.scope.context,
      domain: metadata.domain, objectType: metadata.objectType }, valueMember: 'value',
      choose: candidate => onChoose(makeChoice({ value: candidate.value, display: candidate.name, adapter: scope.scope.adapter, context }, null)) } : undefined;
  return <ReferencePicker id={id} label={label} domain={metadata.domain} selection={metadata.selection} value={value}
    resourceContext={scope.kind === 'ready' ? { adapter: scope.scope.adapter, context: scope.scope.context } : null}
    configuration={configuration} catalog={choices} onChoose={onChoose} resourceObjects={objects} />;
}
