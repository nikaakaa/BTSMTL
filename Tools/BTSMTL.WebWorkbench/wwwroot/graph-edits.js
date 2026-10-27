export class GraphEditSession {
  constructor(snapshot, rootPath, source, definitionsRevision, send) {
    this.baseline = snapshot.files.map(file => ({ ...file }));
    this.context = { rootPath, source: source === 'migrations' ? 'Migrations' : 'Workspace',
      rootRevision: snapshot.revision, definitionsRevision };
    this.send = send;
    this.batches = [];
    this.redoBatches = [];
    this.receipt = null;
    this.busy = false;
  }

  async apply(documentId, edits) {
    await this.preview([...this.batches, { documentId, edits }], []);
  }

  async undo() {
    await this.preview(this.batches.slice(0, -1), [...this.redoBatches, this.batches.at(-1)]);
  }

  async redo() {
    await this.preview([...this.batches, this.redoBatches.at(-1)], this.redoBatches.slice(0, -1));
  }

  async preview(batches, redoBatches) {
    if (this.busy) throw new Error('上一项图修改预览尚未返回。');
    this.busy = true;
    try {
      const receipt = await this.send('/api/authoring/graphs/edits/preview', {
        ...this.context, requestId: crypto.randomUUID(), batches
      });
      this.batches = batches;
      this.redoBatches = redoBatches;
      this.receipt = receipt;
    } finally { this.busy = false; }
  }

  files() {
    const candidates = new Map(this.receipt?.files.map(file => [file.path, file]) || []);
    return this.baseline.map(file => {
      const candidate = candidates.get(file.path);
      return candidate ? { ...file, content: candidate.content, document: JSON.parse(candidate.content),
        portShapes: undefined, stateMachineReceipt: undefined, stateMachineProblem: undefined,
        topologyReceipt: undefined, topologyProblem: undefined, draftChanged: candidate.changed } : { ...file };
    });
  }

  discard() {
    this.batches = [];
    this.redoBatches = [];
    this.receipt = null;
  }
}

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

function dialog(title, contents, submit) {
  const modal = document.createElement('dialog');
  modal.className = 'edit-dialog';
  modal.innerHTML = `<form><header><h2>${html(title)}</h2><button type="button" data-close aria-label="关闭">×</button></header>${contents}<p class="dialog-error" role="alert"></p><footer><span>修改仅生成候选，尚未保存或发布</span><button type="button" data-close>取消</button>${submit ? '<button class="primary" type="submit">预览修改</button>' : ''}</footer></form>`;
  document.body.append(modal);
  modal.addEventListener('close', () => modal.remove());
  modal.querySelectorAll('[data-close]').forEach(button => button.onclick = () => modal.close());
  modal.querySelector('form').onsubmit = async event => {
    event.preventDefault();
    if (!submit) return;
    const form = event.currentTarget;
    form.querySelectorAll('button').forEach(button => { button.disabled = true; });
    try { await submit(form); modal.close(); }
    catch (error) { modal.querySelector('.dialog-error').textContent = error.message; }
    finally { form.querySelectorAll('button').forEach(button => { button.disabled = false; }); }
  };
  modal.showModal();
  return modal;
}

function valueControls(value, path = [], choices = []) {
  const location = html(JSON.stringify(path));
  if (value.kind === 'scalar') {
    const values = value.valueKind === 'Boolean' ? ['true', 'false'] : choices;
    const control = values.length
      ? `<select data-value-path="${location}">${values.map(item => `<option value="${html(item)}" ${item === value.value ? 'selected' : ''}>${html(item)}</option>`).join('')}</select>`
      : `<input data-value-path="${location}" value="${html(value.value)}" autocomplete="off" spellcheck="false">`;
    return `${control}<small>${html(value.type)} · ${html(value.valueKind)} · 原值按文本提交</small>`;
  }
  if (value.kind === 'object') return Object.entries(value.members).map(([key, member]) =>
    `<fieldset><legend>${html(key)}</legend>${valueControls(member, [...path, 'members', key])}</fieldset>`).join('');
  if (value.kind === 'sequence') return `<p>${value.items.length} 项。类型和顺序随候选保留。</p>${value.items.map((item, index) =>
    `<fieldset><legend>第 ${index + 1} 项</legend>${valueControls(item, [...path, 'items', index])}</fieldset>`).join('')}`;
  if (value.kind === 'reference') return Object.entries(value.target).map(([key, target]) =>
    `<label>${html(key)}<input data-reference-path="${location}" data-reference-member="${html(key)}" value="${html(target)}" autocomplete="off" spellcheck="false"></label>`).join('');
  return `<p>当前值为 null；完整类型化 JSON 可在下方编辑。</p>`;
}

function readValue(form, original) {
  const copy = structuredClone(original);
  const at = path => path.reduce((item, key) => item[key], copy);
  form.querySelectorAll('[data-value-path]').forEach(input => { at(JSON.parse(input.dataset.valuePath)).value = input.value; });
  form.querySelectorAll('[data-reference-path]').forEach(input => {
    at(JSON.parse(input.dataset.referencePath)).target[input.dataset.referenceMember] = input.value;
  });
  return copy;
}

export function editField(field, value, apply) {
  let original = value;
  const modal = dialog(field.displayName || field.id,
    `<p class="dialog-intro">${html(field.id)}。控件保留正式类型；复合值可展开逐项修改。</p><div class="typed-value-editor">${valueControls(value, [], field.constraint?.AllowedValues || [])}</div><details class="raw-value-editor"><summary>完整类型化 JSON（用于空集合、新成员或 null）</summary><textarea spellcheck="false">${html(JSON.stringify(value, null, 2))}</textarea></details>`,
    async form => {
      const value = form.querySelector('.raw-value-editor').open
        ? JSON.parse(form.querySelector('textarea').value) : readValue(form, original);
      await apply(value);
    });
  modal.querySelector('.raw-value-editor').addEventListener('toggle', event => {
    if (event.currentTarget.open) modal.querySelector('textarea').value = JSON.stringify(readValue(modal, original), null, 2);
    else {
      try {
        original = JSON.parse(modal.querySelector('textarea').value);
        modal.querySelector('.typed-value-editor').innerHTML = valueControls(original, [], field.constraint?.AllowedValues || []);
        modal.querySelector('.dialog-error').textContent = '';
      } catch (error) {
        modal.querySelector('.dialog-error').textContent = error.message;
        event.currentTarget.open = true;
      }
    }
  });
}

export function renameElement(name, apply) {
  dialog('修改显示名称', `<label>名称<input name="displayName" value="${html(name)}" autocomplete="off"></label>`,
    form => apply(new FormData(form).get('displayName')));
}

export function createNode(capabilities, role, apply) {
  const allowed = [...capabilities.values()].filter(capability => capability.roles.includes(role) && !capability.systemOwned)
    .sort((left, right) => `${left.category}/${left.displayName}`.localeCompare(`${right.category}/${right.displayName}`));
  const modal = dialog('创建节点候选',
    '<label>查找节点<input name="search" type="search" placeholder="名称、分类或稳定能力 ID"></label><label>正式能力<select name="capability" size="12" required></select></label><p>初始字段取自原能力目录的版本化默认值；后续字段与引用规则仍需完整领域接收。</p>',
    form => {
      const definition = capabilities.get(new FormData(form).get('capability'));
      return apply({ id: `local:${crypto.randomUUID()}`, capability: definition.id, displayName: definition.displayName,
        fields: Object.fromEntries(definition.fields.map(field => [field.id, structuredClone(field.defaultValue)])), dynamicPorts: [] });
    });
  const fill = () => {
    const query = modal.querySelector('[name=search]').value.toLowerCase();
    modal.querySelector('select').innerHTML = allowed.filter(item => `${item.id} ${item.displayName} ${item.category}`.toLowerCase().includes(query))
      .map(item => `<option value="${html(item.id)}">${html(item.category)} / ${html(item.displayName)}</option>`).join('');
  };
  modal.querySelector('[name=search]').oninput = fill;
  fill();
}

export function createConnection(graph, shapes, apply) {
  const sources = graph.nodes.flatMap(node => shapes.get(node.id).ports.filter(port => port.direction === 'Output')
    .map(port => ({ node, port })));
  const targets = graph.nodes.flatMap(node => shapes.get(node.id).ports.filter(port => port.direction === 'Input')
    .map(port => ({ node, port })));
  const label = item => `${item.node.displayName || item.node.id} / ${item.port.displayName} (${item.port.valueType})`;
  const modal = dialog('创建连接候选', `<label>输出<select name="source" required>${sources.map((item, index) =>
    `<option value="${index}">${html(label(item))}</option>`).join('')}</select></label><label>输入<select name="target" required></select></label><p>只列出原端口投影中类型一致的输入。容量、顺序与环路由同一服务返回诊断。</p>`,
    form => {
      const data = new FormData(form), source = sources[Number(data.get('source'))], target = targets[Number(data.get('target'))];
      return apply({ id: `local:${crypto.randomUUID()}`, sourceNode: source.node.id, sourcePort: source.port.id,
        targetNode: target.node.id, targetPort: target.port.id,
        order: Math.max(-1, ...graph.edges.filter(edge => edge.sourceNode === source.node.id && edge.sourcePort === source.port.id).map(edge => edge.order)) + 1,
        fields: {} });
    });
  const fill = () => {
    const source = sources[Number(modal.querySelector('[name=source]').value)];
    modal.querySelector('[name=target]').innerHTML = targets.map((item, index) => ({ item, index }))
      .filter(({ item }) => source && item.node.id !== source.node.id && item.port.valueType === source.port.valueType)
      .map(({ item, index }) => `<option value="${index}">${html(label(item))}</option>`).join('');
  };
  modal.querySelector('[name=source]').onchange = fill;
  fill();
}

export function showGraphDiff(session) {
  const files = session.receipt?.files.filter(file => file.changed) || [];
  dialog('批量修改差异', `<p class="dialog-intro">${files.length} 个图文件发生变化。基线 ${html(session.context.rootRevision)}。以下候选尚未通过完整领域接收，未保存或发布。</p>${files.map(file =>
    `<section class="file-diff"><h3>${html(file.path)}</h3><small>${file.changedElements.length} 个相关对象 · ${file.topology.valid ? '图结构通过' : html(file.topology.message)}</small><div class="diff-columns"><label>磁盘原文<textarea readonly spellcheck="false">${html(file.beforeContent)}</textarea></label><label>候选原文<textarea readonly spellcheck="false">${html(file.content)}</textarea></label></div></section>`).join('') || '<p>当前候选与原始文件相同。</p>'}`, null);
}
