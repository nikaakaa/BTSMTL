export const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function fieldsHtml(fields) {
  return fields.map(field => `<dt>${html(field.displayName)}</dt><dd>${html(field.value)}</dd>`).join('');
}

export function sectionsHtml(sections) {
  return sections.map(section => `<section><h2>${html(section.displayName)}</h2>${section.records.length ?
    section.records.map(record => `<article class="actor-fact"><strong>${html(record.displayName)}</strong><dl>${fieldsHtml(record.fields)}</dl><div class="source-links">${record.locations.map(location =>
      `<button type="button" data-authoring-document="${html(location.documentId)}" data-authoring-element="${html(location.elementId)}">定位${html(location.displayName)}</button>`).join('')}</div><details><summary>原始记录</summary><pre>${html(record.rawJson)}</pre></details></article>`).join('') :
    `<p class="muted">${html(section.emptyMessage)}</p>`}</section>`).join('');
}
