import { bindValueEditor, dialog } from './graph-edits.js';

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function graphContext(graph, target, files, definitions, capabilities) {
  const contract = definitions.stateMachine;
  if (contract === undefined) return '';
  const owned = files.filter(file => Array.isArray(file.document?.nodes) &&
    file.document.owner.documentId === graph.id && file.document.owner.elementId === target.id);
  const roleName = role => role === contract.bodyRole ? 'StateBody'
    : role === contract.conditionRole ? '转移条件' : role === contract.role ? '嵌套 FSM' : role;
  let content = owned.length ? `<section class="owned-graph-links"><h3>本对象拥有的图</h3>${owned.map(file =>
    `<button data-document="${html(file.path)}"><strong>${html(file.document.displayName)}</strong><small>${html(roleName(file.document.role))}</small></button>`).join('')}</section>` : '';
  if (graph.role === contract.bodyRole && target === graph) {
    const declaration = definitions.flowGraphs.find(item => item.role === graph.role);
    content += `<section class="owned-graph-links"><h3>StateBody 生命周期入口</h3>${declaration.anchors.flatMap(anchor =>
      graph.nodes.filter(node => node.capability === anchor.capability).map(node =>
        `<button data-select-node="${html(node.id)}">${html(node.displayName)}<small>${html(anchor.name)}</small></button>`)).join('')}</section>`;
  }
  if (graph.role === contract.role && graph.nodes.includes(target)) {
    const declaration = capabilities.get(target.capability);
    if (declaration.fields.some(field => field.id === contract.bodyField) &&
        !owned.some(file => file.document.role === contract.bodyRole))
      content += '<p class="authoring-gap">此状态尚无可下钻的私有 StateBody。创建私有图需要共同创建合同，不能复用其它状态的可变 StateBody。</p>';
    const outgoing = graph.edges.filter(edge => edge.sourceNode === target.id).sort((left, right) => left.order - right.order);
    content += `<section class="owned-graph-links"><h3>从此状态出发的转移</h3>${outgoing.map(edge => {
      const destination = graph.nodes.find(node => node.id === edge.targetNode);
      return `<button data-select-edge="${html(edge.id)}">${html(destination.displayName)}<small>同输出顺序 ${edge.order + 1} · ${html(edge.sourcePort)}</small></button>`;
    }).join('') || '<p class="muted">此状态没有出边。</p>'}</section>`;
  }
  return content;
}

export function editTransition(edge, definitions, references, apply) {
  const fields = Object.entries(edge.fields);
  const descriptions = definitions.capabilities.flatMap(capability => capability.fields);
  const editors = new Map();
  const modal = dialog('配置当前转移', `<p>同时修改此连接的实际作者字段，作为一批共同候选提交。执行顺序通过“调整执行顺序”单独编辑；本表不补造缺失字段或默认值。</p>${fields.map(([id]) =>
    `<fieldset><legend>${html(id)}</legend><div data-transition-field="${html(id)}"></div></fieldset>`).join('')}`,
    fields.length ? () => {
      const edits = fields.map(([fieldId, before]) => ({ fieldId, before, value: editors.get(fieldId).read() }))
        .filter(field => JSON.stringify(field.value) !== JSON.stringify(field.before))
        .map(({ fieldId, value }) => ({ kind: 'set-edge-field', edgeId: edge.id, fieldId, value }));
      return edits.length ? apply(edits) : undefined;
    } : null);
  modal.querySelectorAll('[data-transition-field]').forEach(container => {
    const id = container.dataset.transitionField, value = edge.fields[id];
    const definition = descriptions.find(field => field.valueType === value.type)?.valueDefinition;
    editors.set(id, bindValueEditor(container, value, [], references, definition));
  });
}
