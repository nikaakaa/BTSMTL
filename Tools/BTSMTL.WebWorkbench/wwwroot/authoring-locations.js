const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function authoringSourceLinks(sources) {
  const targets = sources.flatMap(source => [
    [source.graphId, source.graphId, '图'], [source.graphId, source.nodeId, '节点'],
    [source.graphId, source.edgeId, '连接'], [source.graphId, source.declarationId, '变量声明'],
    [source.timelineId, source.timelineId, '时间轴'], [source.timelineId, source.trackId, '轨道'],
    [source.timelineId, source.clipId, '片段']
  ]).filter(([document, element]) => document && element);
  return [...new Map(targets.map(target => [JSON.stringify(target.slice(0, 2)), target])).values()].map(([document, element, label]) =>
    `<button type="button" data-authoring-document="${html(document)}" data-authoring-element="${html(element)}">定位${label}</button>`).join('');
}

export async function showAuthoringLocation(documentId, elementId, request, rootId) {
  const modal = document.createElement('dialog');
  modal.className = 'source-location-dialog';
  modal.innerHTML = `<header><h2>定位作者来源</h2><button type="button" data-close aria-label="关闭">×</button></header><p>按记录中的文档和元素身份查找当前文件。当前作者版本可能已改变，原记录仍保留原版本。</p><label>查询目录<select><option value="workspace">正式作者工作区</option><option value="migrations">迁移候选 · 只读核对</option></select></label><dl>${rootId === undefined ? '' : `<dt>根身份</dt><dd><code>${html(rootId)}</code></dd>`}<dt>文档身份</dt><dd><code>${html(documentId)}</code></dd><dt>元素身份</dt><dd><code>${html(elementId)}</code></dd></dl><p data-status role="status"></p><div data-problems></div><div data-locations></div><button type="button" data-more hidden>继续读取位置</button>`;
  document.body.append(modal);
  let reading = null, offset = 0;
  const select = modal.querySelector('select'), more = modal.querySelector('[data-more]');
  modal.addEventListener('close', () => { reading?.abort(); modal.remove(); });
  modal.querySelector('[data-close]').onclick = () => modal.close();
  const load = async reset => {
    reading?.abort();
    const current = new AbortController(); reading = current;
    const source = select.value;
    if (reset) { offset = 0; modal.querySelector('[data-locations]').replaceChildren(); }
    more.disabled = true;
    modal.querySelector('[data-status]').textContent = '正在按稳定身份查询…';
    try {
      const query = new URLSearchParams({ documentId, elementId, offset, limit: 100,
        ...(rootId === undefined ? {} : { rootId }) });
      const page = await request(`/api/${source}/locations?${query}`, undefined, current.signal);
      if (reading !== current) return;
      modal.querySelector('[data-status]').textContent = `${page.total} 个实际位置 · ${page.complete ? '查询完整' : '部分来源未能读取'}`;
      modal.querySelector('[data-problems]').innerHTML = page.problems.map(problem => `<p class="error">${html(problem.path)}：${html(problem.message)}</p>`).join('');
      modal.querySelector('[data-locations]').insertAdjacentHTML('beforeend', page.items.map(item => {
        const query = new URLSearchParams({ source, root: item.rootPath, file: item.documentPath, element: item.elementId });
        return `<article><a href="/?${html(query)}" target="_blank" rel="noopener">${html(item.rootPath)} / ${html(item.documentPath)} ↗</a><dl><dt>当前根修订</dt><dd><code>${html(item.rootRevision)}</code></dd><dt>当前文件修订</dt><dd><code>${html(item.fileRevision)}</code></dd></dl></article>`;
      }).join(''));
      if (reset && !page.items.length) modal.querySelector('[data-locations]').textContent = '所选目录没有此身份的作者内容。可切换目录核对，原运行记录未改变。';
      offset = page.nextOffset; more.hidden = offset === null;
    } catch (error) {
      if (!current.signal.aborted) modal.querySelector('[data-status]').textContent = error.message;
    } finally {
      if (reading === current) { reading = null; more.disabled = false; }
    }
  };
  select.onchange = () => load(true); more.onclick = () => load(false);
  modal.showModal();
  await load(true);
}
