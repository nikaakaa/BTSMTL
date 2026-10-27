const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

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
