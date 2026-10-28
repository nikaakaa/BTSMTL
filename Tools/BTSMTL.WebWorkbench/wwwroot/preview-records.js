export const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function fieldsHtml(fields) {
  return fields.map(field => `<dt>${html(field.displayName)}</dt><dd>${html(field.value)}</dd>`).join('');
}

export function sectionsHtml(sections) {
  return sections.map(section => `<section><h2>${html(section.displayName)}</h2>${section.records.length ?
    section.records.map(record => `<article class="actor-fact"><strong>${html(record.displayName)}</strong><dl>${fieldsHtml(record.fields)}</dl><div class="source-links">${record.locations.map(location =>
      `<code class="source-identity" title="${html(location.documentId)}">${html(location.displayName)} · ${html(location.elementId)}</code>`).join('')}</div><details><summary>宿主记录</summary><pre>${html(record.rawJson)}</pre></details></article>`).join('') :
    `<p class="muted">${html(section.emptyMessage)}</p>`}</section>`).join('');
}
