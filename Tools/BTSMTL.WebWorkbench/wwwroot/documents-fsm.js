import { bindValueEditor, dialog } from './graph-edits.js';

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function graphContext(graph, target, files, definitions, capabilities) {
  const contract = definitions.stateMachine;
  if (contract === undefined) return '';
  const linked = Object.entries(target.fields).flatMap(([field, value]) => {
    if (value.kind !== 'reference' || value.target.rootId !== graph.owner.rootId) return [];
    const file = files.find(file => file.document?.id === value.target.documentId && Array.isArray(file.document.nodes));
    return file ? [{ field, file, element: value.target.elementId }] : [];
  });
  const owned = files.filter(file => Array.isArray(file.document?.nodes) &&
    file.document.owner.documentId === graph.id && file.document.owner.elementId === target.id &&
    !linked.some(link => link.file === file));
  const roleName = role => role === contract.bodyRole ? 'StateBody'
    : role === contract.conditionRole ? '转移条件' : role === contract.role ? '嵌套 FSM' : role;
  let content = linked.length ? `<section class="owned-graph-links"><h3>本对象引用的图</h3>${linked.map(link =>
    `<button data-document="${html(link.file.path)}" data-element="${html(link.element)}"><strong>${html(link.file.document.displayName)}</strong><small>${html(roleName(link.file.document.role))} · ${html(link.field)}</small></button>`).join('')}</section>` : '';
  if (owned.length) content += `<section class="owned-graph-links"><h3>本对象拥有的图</h3>${owned.map(file =>
    `<button data-document="${html(file.path)}"><strong>${html(file.document.displayName)}</strong><small>${html(roleName(file.document.role))}</small></button>`).join('')}</section>`;
  if (graph.role === contract.role && target === graph)
    content += `<section class="owned-graph-links"><h3>FSM 系统入口</h3>${contract.anchors.flatMap(anchor =>
      graph.nodes.filter(node => node.id === anchor.identity).map(node =>
        `<button data-select-node="${html(node.id)}">${html(node.displayName)}<small>${html(anchor.kind)} · ${html(anchor.name)}</small></button>`)).join('')}</section>`;
  if (graph.role === contract.bodyRole && target === graph) {
    const declaration = definitions.flowGraphs.find(item => item.role === graph.role);
    content += `<section class="owned-graph-links"><h3>StateBody 生命周期入口</h3>${declaration.anchors.flatMap(anchor =>
      graph.nodes.filter(node => node.capability === anchor.capability).map(node =>
        `<button data-select-node="${html(node.id)}">${html(node.displayName)}<small>${html(anchor.name)}</small></button>`)).join('')}</section>`;
  }
  if (graph.role === contract.role && graph.nodes.includes(target)) {
    const declaration = capabilities.get(target.capability);
    if (declaration.fields.some(field => field.id === contract.bodyField) &&
        !linked.some(link => link.file.document.role === contract.bodyRole))
      content += '<p class="authoring-gap">此状态尚未引用可下钻的 StateBody。创建私有图需要共同创建合同，不能复用其它状态的可变 StateBody。</p>';
    const outgoing = graph.edges.filter(edge => edge.sourceNode === target.id).sort((left, right) => left.order - right.order);
    content += `<section class="owned-graph-links"><h3>从此状态出发的转移</h3>${outgoing.map(edge => {
      const destination = graph.nodes.find(node => node.id === edge.targetNode);
      return `<button data-select-edge="${html(edge.id)}">${html(destination.displayName)}<small>同输出顺序 ${edge.order + 1} · ${html(edge.sourcePort)}</small></button>`;
    }).join('') || '<p class="muted">此状态没有出边。</p>'}</section>`;
  }
  return content;
}

export function editTransition(edge, definitions, references, apply) {
  const fields = definitions.graphAuthoring.edge.fields.filter(field =>
    field.documentMember === 'Field' && field.visible && field.writable);
  const editors = new Map();
  const initial = field => edge.fields[field.id] || field.defaultValue;
  const modal = dialog('配置当前转移', `<p>条件、优先级和中断规则使用此连接的原作者定义。未写出的字段显示领域默认值，只有实际修改才写入候选。执行顺序通过“调整执行顺序”编辑。</p>${fields.map(field =>
    `<fieldset><legend>${html(field.displayName)}${edge.fields[field.id] ? '' : ' · 领域默认值'}</legend><div data-transition-field="${html(field.id)}"></div></fieldset>`).join('')}`,
    fields.length ? () => {
      const edits = fields.map(field => ({ fieldId: field.id, before: initial(field), value: editors.get(field.id).read() }))
        .filter(field => JSON.stringify(field.value) !== JSON.stringify(field.before))
        .map(({ fieldId, value }) => ({ kind: 'set-edge-field', edgeId: edge.id, fieldId, value }));
      return edits.length ? apply(edits) : undefined;
    } : null);
  modal.querySelectorAll('[data-transition-field]').forEach(container => {
    const field = fields.find(field => field.id === container.dataset.transitionField);
    editors.set(field.id, bindValueEditor(container, initial(field), field.constraint.AllowedValues,
      references, field.valueDefinition));
  });
}
