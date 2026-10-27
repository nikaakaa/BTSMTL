const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export async function showReferenceResource(snapshot, target, request, renderValue) {
  const same = value => value.kind === 'reference' && value.target.rootId === target.rootId &&
    value.target.documentId === target.documentId && value.target.elementId === target.elementId;
  const mapping = snapshot.root.unityOutputs.find(item => item.documentId === target.documentId &&
    item.elementId === target.elementId);
  const entries = snapshot.root.files.filter(file => file.kind === 'References').flatMap(declaration => {
    const file = snapshot.files.find(file => file.path === declaration.path);
    return file.document.entries.filter(entry => same(entry.source)).map(entry => ({ entry, file }));
  });
  const modal = document.createElement('dialog');
  modal.className = 'edit-dialog resource-dialog';
  modal.innerHTML = `<header><h2>引用资源的只读详情</h2><button type="button" aria-label="关闭">×</button></header><p>外部配置由原资源维护。下方字段来自本次文档保存的引用记录，不表示当前 Unity 资源已经重新读取。</p><dl><dt>根身份</dt><dd><code>${html(target.rootId)}</code></dd><dt>文档身份</dt><dd><code>${html(target.documentId)}</code></dd><dt>对象身份</dt><dd><code>${html(target.elementId)}</code></dd>${mapping ? `<dt>Unity 来源</dt><dd>${html(mapping.assetPath)}</dd><dt>GUID / local file ID</dt><dd><code>${html(mapping.guid)} / ${html(mapping.localFileId)}</code></dd>` : ''}<dt>本次根修订</dt><dd><code>${html(snapshot.revision)}</code></dd></dl><div class="resource-reference-fields">${entries.map(({ entry, file }) => `<section><h3>${html(entry.kind)}</h3><small>${html(file.path)} · ${html(file.revision)}${file.draftChanged ? ' · 当前候选尚未保存' : ''}</small>${Object.entries(entry.fields).map(([key, value]) => `<div class="field"><label>${html(key)}</label>${renderValue(value)}</div>`).join('')}</section>`).join('') || '<p>本次文档未记录此引用的详情字段；原引用身份保持不变。</p>'}</div><div class="resource-preview" role="status">正在查询已导出的网页素材…</div>`;
  document.body.append(modal);
  modal.addEventListener('close', () => modal.remove());
  modal.querySelector('header button').onclick = () => modal.close();
  modal.showModal();
  const preview = modal.querySelector('.resource-preview');
  try {
    const library = await request('/api/resources/animations');
    const clip = library.clips.find(item => item.resource.guid === target.rootId &&
      item.resource.localFileId === target.elementId);
    preview.innerHTML = clip ? `<a href="/resources.html?${html(new URLSearchParams({ resource: clip.resource.identity }))}" target="_blank" rel="noopener">浏览已导出的动画片段 ↗</a><small>素材库中的资源修订 ${html(clip.resource.contentRevision)}</small><small>预览使用素材库的实际导出版本，文档引用保持不变。</small>`
      : '当前网页素材库未导出此引用。上方仍可查看原资源身份与文档记录。';
  } catch (error) { preview.textContent = error.message; }
}

export async function showReferences(title, endpoint, source, request) {
  const dialog = document.createElement('dialog');
  dialog.className = 'edit-dialog reference-dialog';
  dialog.innerHTML = `<header><h2>${html(title)}</h2><button type="button" aria-label="关闭">×</button></header><p>引用来自磁盘保存版本，当前会话未保存的候选不参与查询。位置附带原根及文件修订。</p><div class="reference-query-state" role="status">正在读取…</div><div class="reference-problems"></div><div class="reference-uses"></div><button class="reference-more" hidden>继续加载</button>`;
  document.body.append(dialog);
  dialog.addEventListener('close', () => dialog.remove());
  dialog.querySelector('header button').onclick = () => dialog.close();
  dialog.showModal();
  let offset = 0;
  const more = dialog.querySelector('.reference-more');
  const load = async () => {
    more.disabled = true;
    try {
      const query = new URL(endpoint, location.origin);
      query.searchParams.set('offset', offset);
      query.searchParams.set('limit', '100');
      const page = await request(query.pathname + query.search);
      dialog.querySelector('.reference-query-state').textContent = `${page.total} 个引用位置 · ${page.complete ? '本次来源读取完整' : '有来源未能读取，结果不完整'}`;
      dialog.querySelector('.reference-problems').innerHTML = page.problems.map(problem =>
        `<p class="error">${html(problem.path)}：${html(problem.message)}</p>`).join('');
      dialog.querySelector('.reference-uses').insertAdjacentHTML('beforeend', page.items.map(use => {
        const query = new URLSearchParams({ source, root: use.source.rootPath, file: use.source.documentPath,
          element: use.source.elementId, pointer: use.source.pointer, tab: 'document' });
        return `<article><a href="/?${html(query.toString())}" target="_blank" rel="noopener">${html(use.source.rootPath)} / ${html(use.source.documentPath)} ↗</a><code>${html(use.source.pointer)}</code><small>${html(use.relation)} · 对象 ${html(use.source.elementId)}</small><div class="reference-target"><span>引用目标</span><code>${html(use.target.rootId)} / ${html(use.target.documentId)} / ${html(use.target.elementId)}</code></div><details><summary>原始来源修订</summary><code>根 ${html(use.source.rootRevision)}</code><code>文件 ${html(use.source.fileRevision)}</code>${use.valueType ? `<code>${html(use.valueType)}</code>` : ''}</details></article>`;
      }).join(''));
      offset = page.nextOffset;
      more.hidden = offset === null;
    } catch (error) {
      dialog.querySelector('.reference-query-state').textContent = error.message;
      dialog.querySelector('.reference-query-state').classList.add('error');
    } finally { more.disabled = false; }
  };
  more.onclick = load;
  await load();
}
