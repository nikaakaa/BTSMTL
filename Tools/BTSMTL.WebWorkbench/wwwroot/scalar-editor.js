const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function scalarControl(value, attributes, definition = null, allowedValues = []) {
  if (value.valueKind === 'String' && !allowedValues.length)
    return `<textarea class="scalar-text" ${attributes} rows="2" spellcheck="false">\n${html(value.value)}</textarea>`;
  const values = allowedValues.length ? allowedValues : value.valueKind === 'Boolean' ? ['true', 'false']
    : definition?.kind === 'Enumeration' ? definition.enumValues : [];
  const input = `<input ${attributes} value="${html(value.value)}" autocomplete="off" spellcheck="false"`;
  if (definition?.flags && !allowedValues.length) {
    const id = `enum-${crypto.randomUUID()}`;
    return `${input} list="${id}"><datalist id="${id}">${values.map(item => `<option value="${html(item)}"></option>`).join('')}</datalist><small>多个枚举名称用逗号分隔：${values.map(html).join('、')}</small>`;
  }
  if (!values.length) return `${input}>`;
  return `<select ${attributes}>${values.includes(value.value) ? '' : `<option value="${html(value.value)}" selected>当前值：${html(value.value)}</option>`}${values.map(item => `<option value="${html(item)}" ${item === value.value ? 'selected' : ''}>${html(item)}</option>`).join('')}</select>`;
}
