import { createContext, useContext } from 'react';
import type { AdapterCatalogItem, ReferenceChoice } from '../../api/authoring';
import type { ResourceReferenceMetadata } from '../../api/resourceRegistration';
import { resourceCatalogChoices, useAdapterCatalog } from '../../state/adapterCatalog';
import { authoringDocument, useAuthoring } from '../../state/authoring';
import { projectActions } from '../../state/project';
import { consoleActions } from '../../state/store';
import { Button } from '../../ui/controls';
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
  switch (props.metadata.source) {
    case null: return <ReferencePicker id={props.id} label={props.label} value={props.value} configuration={props.configuration}
      catalog={{ kind: 'unavailable', reason: props.metadata.availability.reason }} onChoose={props.onChoose} />;
    case 'AuthoringDocument': return <DocumentFieldPicker {...props} metadata={props.metadata} />;
    case 'AdapterResource': return <AdapterFieldPicker {...props} metadata={props.metadata} />;
  }
}

function DocumentFieldPicker({ id, label, metadata, value, configuration, onChoose }:
  ResourceFieldPickerProps & { metadata: Extract<ResourceReferenceMetadata, { source: 'AuthoringDocument' }> }) {
  const catalog = useAuthoring(state => state.elementCatalog);
  const elements = catalog.items.filter(element => element.contentDomain === metadata.domain && element.sourceIdentity !== null);
  const choices: ReferenceCatalog = !metadata.availability.available
    ? { kind: 'unavailable', reason: metadata.availability.reason }
    : catalog.kind !== 'ready' ? { kind: 'unavailable', reason: catalog.kind === 'unavailable' ? catalog.reason : '正在读取工作区作者元素。' }
      : { kind: 'ready', referenceKind: metadata.domain, invalidReason: '找不到所引用的内容，可能已改名或删除；原值没有改动。',
        items: elements.map(element => ({ value: element.sourceIdentity, name: element.name,
          detail: element.documentPath, source: `${element.documentPath}/${element.relativePath}#${element.jsonPath}`,
          conflict: elements.filter(other => other.sourceIdentity === element.sourceIdentity).length > 1
            ? `有多处内容使用同一个引用「${element.sourceIdentity}」，无法确定是哪一个。` : null,
          targets: [], associated: [] })) };
  const selected = elements.filter(element => element.sourceIdentity === value);
  return <><ReferencePicker id={id} label={label} value={value} configuration={configuration} catalog={choices} onChoose={onChoose} />
    {catalog.kind === 'ready' && selected.length === 1 && <Button variant="ghost" onClick={() => void projectActions.openDocumentElement(selected[0])
      .catch(error => consoleActions.error('作者文档', error))}>定位作者内容</Button>}</>;
}

function AdapterFieldPicker({ id, label, metadata, value, configuration, onChoose }:
  ResourceFieldPickerProps & { metadata: Extract<ResourceReferenceMetadata, { source: 'AdapterResource' }> }) {
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
