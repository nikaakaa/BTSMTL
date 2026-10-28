import { dialog, referenceIdentity } from './graph-edits.js';

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function privateGraphFields(graph, target, definitions) {
  const contract = definitions.stateMachine;
  if (target.capability) {
    const capability = definitions.capabilities.find(item => item.id === target.capability);
    const declaration = definitions.nodeAuthoring.find(item => item.id === target.capability);
    return declaration.graphs.filter(item => [contract.role, contract.bodyRole, contract.conditionRole].includes(item.role))
      .flatMap(item => {
        const field = capability.fields.find(field => field.id === item.field);
        return field ? [{ field, role: item.role }] : [];
      });
  }
  return graph.role === contract.role && graph.edges.includes(target)
    ? [{ field: definitions.graphAuthoring.edge.fields.find(field => field.id === contract.conditionField),
      role: contract.conditionRole }] : [];
}

export function privateGraphReferences(graph, target, definitions, references, files) {
  const declarations = privateGraphFields(graph, target, definitions);
  if (!declarations.length) return references;
  const types = new Set(declarations.map(item => item.field.valueType));
  const current = new Set(declarations.map(({ field }) => target.fields[field.id] || field.defaultValue)
    .filter(value => value.kind === 'reference').map(referenceIdentity));
  const documents = new Map(files.filter(file => Array.isArray(file.document?.nodes))
    .map(file => [file.document.id, file.document]));
  return references.filter(item => {
    if (!types.has(item.value.type) || current.has(referenceIdentity(item.value))) return true;
    const reference = item.value.target;
    if (reference.rootId !== graph.owner.rootId) return false;
    const document = documents.get(reference.documentId);
    return document && declarations.some(item => item.role === document.role) &&
      document.owner.documentId === graph.id && document.owner.elementId === target.id;
  });
}

function newGraph(parent, target, role, displayName, rootId, definitions) {
  const machine = role === definitions.stateMachine.role;
  const anchors = machine ? definitions.stateMachine.anchors : definitions.flowGraphs.find(item => item.role === role).anchors;
  const description = machine ? definitions.graphAuthoring.stateMachine : definitions.graphAuthoring.flow;
  const nodes = anchors.map(anchor => {
    const capability = machine
      ? definitions.capabilities.find(item => item.roles.includes(role) && item.stateKind === anchor.kind)
      : definitions.capabilities.find(item => item.id === anchor.capability);
    return { id: anchor.identity, capability: capability.id, displayName: capability.displayName,
      fields: {}, dynamicPorts: [] };
  });
  return { formatVersion: parent.formatVersion, id: crypto.randomUUID(), role,
    owner: { rootId, documentId: parent.id, elementId: target.id }, displayName,
    definitionVersion: parent.definitionVersion,
    fields: Object.fromEntries(description.fields.filter(field => field.documentMember === 'Field')
      .map(field => [field.id, structuredClone(field.defaultValue)])), nodes, edges: [], variables: [] };
}

export function privateGraphFiles(snapshot, parent, target, declarations, definitions, insertNode = false) {
  const root = structuredClone(snapshot.root), graph = structuredClone(parent);
  if (insertNode) graph.nodes.push(structuredClone(target));
  const owner = [...graph.nodes, ...graph.edges].find(item => item.id === target.id);
  const added = declarations.map(({ field, role, displayName }) => {
    const document = newGraph(graph, owner, role, displayName, root.id, definitions);
    const path = `graphs/${document.id}.json`;
    root.files.push({ id: document.id, kind: 'Graph', path });
    owner.fields[field.id] = { type: field.valueType, kind: 'reference',
      target: { rootId: root.id, documentId: document.id, elementId: document.id } };
    return { path, document };
  });
  const changes = new Map([[root.id, root], [graph.id, graph]]);
  const text = (document, source) => {
    const value = JSON.stringify(document, null, 2) + (source.content.endsWith('\n') ? '\n' : '');
    return source.content.includes('\r\n') ? value.replaceAll('\n', '\r\n') : value;
  };
  const files = snapshot.files.map(file => ({ path: file.path, text: changes.has(file.document.id)
    ? text(changes.get(file.document.id), file) : file.content }));
  files.push(...added.map(file => ({ path: file.path, text: text(file.document, snapshot.files[0]) })));
  return { files, added };
}

export function createPrivateGraph(snapshot, graph, target, declaration, definitions, apply) {
  dialog(`新建私有 ${declaration.role}`, `<p>新图由当前对象唯一拥有。固定入口来自正式图定义，新图、父引用和根文件清单一起交给共同候选服务。之后可继续编辑并统一保存。</p><label>图名称<input name="displayName" value="${html(`${target.displayName || '转移'} · ${declaration.role}`)}" required autocomplete="off"></label>`,
    form => {
      const candidate = privateGraphFiles(snapshot, graph, target,
        [{ ...declaration, displayName: new FormData(form).get('displayName') }], definitions);
      return apply(candidate.files, candidate.added[0].path);
    });
}
