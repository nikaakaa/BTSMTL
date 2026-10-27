const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export class ObservationQuery {
  constructor(container, request) {
    this.container = container;
    this.request = request;
    this.runId = null;
    this.stream = null;
    this.reading = null;
    this.query = null;
    container.innerHTML = `<h2>按角色与来源查询确认帧</h2><form class="observation-query"><label>起始 SDK 帧<input name="firstFrame" type="number" min="0" step="1" required value="0"></label><label>结束 SDK 帧<input name="lastFrame" type="number" min="0" step="1" required value="0"></label><label>角色 Roster 序号<input name="rosterIndex" type="number" min="0" step="1" placeholder="全部角色"></label><label>来源身份<input name="source" placeholder="图、节点、连接、声明或来源 ID"></label><button type="submit">读取记录</button><button type="button" data-stream>跟随此范围的帧流</button><button type="button" data-stop hidden>停止读取流</button></form><p class="observation-query-status" role="status">选择运行后可查询其实际记录。</p><div class="observation-query-items"></div><button type="button" data-more hidden>下一页</button>`;
    this.form = container.querySelector('form');
    this.items = container.querySelector('.observation-query-items');
    this.status = container.querySelector('.observation-query-status');
    this.more = container.querySelector('[data-more]');
    this.stop = container.querySelector('[data-stop]');
    this.form.onsubmit = event => { event.preventDefault(); this.read(false).catch(error => this.failure(error)); };
    this.more.onclick = () => this.read(true).catch(error => this.failure(error));
    container.querySelector('[data-stream]').onclick = () => this.follow();
    this.stop.onclick = () => { this.close(); this.status.textContent = '帧流已停止，已读取记录保留。'; };
    this.enable(false);
  }

  bind(runId, summary) {
    this.close();
    this.runId = runId;
    this.query = null;
    this.items.replaceChildren();
    this.more.hidden = true;
    this.form.elements.firstFrame.value = summary.initialSdkFrame;
    this.form.elements.lastFrame.value = summary.lastSdkFrame;
    this.status.textContent = `运行 ${runId}。结果来自该运行的实际记录，未采集的通道不会补造。`;
    this.enable(true);
  }

  reset() {
    this.close(); this.runId = null; this.query = null;
    this.items.replaceChildren(); this.more.hidden = true;
    this.status.textContent = '等待读取所选运行的确认帧。'; this.enable(false);
  }

  enable(value) { this.form.querySelectorAll('input, button').forEach(element => { element.disabled = !value; }); }

  parameters() {
    const query = new URLSearchParams();
    for (const [key, value] of new FormData(this.form)) if (value.trim()) query.set(key, value.trim());
    return query;
  }

  async read(more) {
    this.close();
    if (!this.form.reportValidity()) return;
    const runId = this.runId;
    const reading = new AbortController();
    this.reading = reading;
    const query = more ? new URLSearchParams(this.query) : this.parameters();
    query.set('offset', more ? this.more.dataset.offset : '0'); query.set('limit', '64');
    this.more.disabled = true;
    try {
      const page = await this.request(`/api/preview/runs/${runId}/observations?${query}`, undefined, reading.signal);
      if (this.reading !== reading) return;
      this.query = query;
      if (!more) this.items.replaceChildren();
      this.append(page.items);
      this.status.textContent = `${page.total} 个实际匹配帧 · 已读取至 ${Math.min(page.offset + page.items.length, page.total)} · 场景修订 ${page.sceneRevision}`;
      this.more.hidden = page.nextOffset === null;
      this.more.dataset.offset = page.nextOffset;
    } catch (error) {
      if (!reading.signal.aborted) throw error;
    } finally {
      if (this.reading === reading) { this.reading = null; this.more.disabled = false; }
    }
  }

  follow() {
    if (!this.form.reportValidity()) return;
    this.close(); this.items.replaceChildren(); this.more.hidden = true;
    const runId = this.runId, query = this.parameters();
    const stream = new EventSource(`/api/preview/runs/${runId}/observations/stream?${query}`);
    this.stream = stream; this.stop.hidden = false;
    stream.onopen = () => {
      if (this.stream === stream) this.status.textContent = '帧流已连接，显示本范围最近512个实际匹配帧。';
    };
    stream.addEventListener('frame', event => {
      if (this.stream !== stream) return;
      const item = JSON.parse(event.data);
      this.append([item.observation]);
      while (this.items.children.length > 512) this.items.firstElementChild.remove();
      this.status.textContent = `实际 SDK 帧 ${event.lastEventId} · 场景修订 ${item.sceneRevision} · 断线按此事件身份续读`;
    });
    stream.addEventListener('end', () => {
      if (this.stream !== stream) return;
      this.close(); this.status.textContent = '该范围的帧流已结束，实际记录保留在下方。';
    });
    stream.onerror = () => {
      if (this.stream !== stream) return;
      if (stream.readyState === EventSource.CLOSED) {
        this.close(); this.status.textContent = '帧流已关闭。可读取同范围分页查看服务返回的具体原因。';
      } else this.status.textContent = '帧流连接中断，浏览器正在按最后事件身份重连；可停止后读取同范围分页。';
    };
  }

  append(items) {
    this.items.insertAdjacentHTML('beforeend', items.map(item => `<details><summary>SDK ${item.frame.frame} · ${item.frame.actors.length} 个角色 · ${item.operations.length} 条操作 · ${item.variables.length} 个变量 · ${item.outputEvents.length} 条表现事件</summary><pre>${html(JSON.stringify(item, null, 2))}</pre></details>`).join(''));
  }

  close() {
    this.stream?.close(); this.stream = null; this.stop.hidden = true;
    this.reading?.abort(); this.reading = null; this.more.disabled = false;
  }
  failure(error) { this.status.textContent = error.message; }
}
