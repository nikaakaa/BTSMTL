import { html, fieldsHtml } from './preview-records.js';

export class PreviewObservation {
  constructor(container, request, seek) {
    this.container = container;
    this.request = request;
    this.seek = seek;
    this.reset();
  }

  reset() {
    this.reading?.abort();
    this.runId = null;
    this.route = null;
    this.routeFrame = null;
    this.frame = null;
    this.documents = new Map();
    this.selectedDocument = '';
    this.selectedElement = '';
    this.container.innerHTML = '<p class="observation-empty">选择已封存运行后，读取实际 Timeline 播放帧与活动操作来源。</p>';
  }

  async load(runId) {
    this.runId = runId;
    const reading = new AbortController();
    this.reading = reading;
    const route = await this.request(`/api/preview/runs/${runId}/route`, undefined, reading.signal);
    if (this.reading !== reading) return;
    this.route = route;
    for (const frame of route.frames) {
      for (const location of frame.active) {
        if (location.documentId === location.elementId) continue;
        if (!this.documents.has(location.documentId)) this.documents.set(location.documentId, new Map());
        this.documents.get(location.documentId).set(location.elementId, location);
      }
    }
    this.selectedDocument = this.documents.keys().next().value ?? '';
    this.mount();
  }

  mount() {
    const route = this.route;
    const timelines = new Map();
    route.frames.forEach((frame, index) => frame.playbacks.forEach(playback => {
      if (!timelines.has(playback.documentId)) timelines.set(playback.documentId, []);
      timelines.get(playback.documentId).push({ frame, index, playback });
    }));
    this.firstSeconds = route.frames[0]?.seconds ?? 0;
    this.duration = route.frames.length > 1 ? route.frames.at(-1).seconds - this.firstSeconds : 0;
    this.container.innerHTML = `<section class="observed-timeline" aria-label="技能 Timeline 预览">
      <header><h2>技能 Timeline · 已记录播放</h2><output data-timeline-frame></output></header>
      <p class="observation-note">竖线表示该确认帧存在的播放记录。空白表示没有该播放记录；不补算帧间执行。</p>
      <div class="observed-timeline-scroll"><div class="observed-timeline-content">
        <div class="observed-time-ruler"><span>运行相对时间</span><div>${Array.from({ length: 5 }, (_, index) => `<span style="left:${index * 25}%">${(this.firstSeconds + this.duration * index / 4).toFixed(2)} s</span>`).join('')}</div></div>
        <section class="observed-input-group" data-availability="unavailable"><h3>输入轨组 · 方案按键层</h3><p>宿主记录未提供本次选定方案与按键起止区间，无法显示按键轨。</p></section>
        ${[...timelines].map(([id, samples]) => `<div class="observed-timeline-row"><div class="observed-track-name"><strong>Timeline</strong><code title="${html(id)}">${html(id)}</code><output data-timeline-id="${html(id)}">本帧未活动</output></div><div class="observed-timeline-lane">${samples.map(({ frame, index, playback }) => `<button type="button" class="observed-timeline-sample" data-record-index="${index}" style="left:${this.position(frame.seconds)}%" aria-label="查看帧 ${html(frame.hostFrame)} 的 Timeline 记录" title="帧 ${html(frame.hostFrame)} · 游标 ${playback.cursorSeconds.toFixed(4)} 秒 · ${html(playback.status)}"></button>`).join('')}</div></div>`).join('') || '<p class="observation-empty">本次封存记录没有活动 Timeline。</p>'}
        <div class="observed-cursor" hidden><i></i></div>
      </div></div></section>
      <section class="observed-sources" aria-label="活动操作来源">
        <header><h2>活动操作来源</h2><output data-source-count></output></header>
        <p class="observation-note">高亮来自该确认帧的操作状态记录。宿主未提供图连接端点、逐次执行路线和端口采样值。</p>
        <label>来源文档<select data-source-document>${[...this.documents.keys()].map(id => `<option value="${html(id)}">${html(id)}</option>`).join('')}</select></label>
        <div class="observed-source-grid"></div>
        <section class="observed-source-values" aria-label="所选来源的本帧记录"></section>
      </section>`;
    this.container.querySelectorAll('[data-record-index]').forEach(button => {
      button.onclick = () => this.seek(Number(button.dataset.recordIndex));
    });
    this.container.querySelector('[data-source-document]').onchange = event => {
      this.selectedDocument = event.target.value;
      this.selectedElement = '';
      this.mountSources();
      this.showSources();
    };
    this.mountSources();
  }

  position(seconds) { return this.duration ? (seconds - this.firstSeconds) / this.duration * 100 : 0; }

  mountSources() {
    const locations = this.documents.get(this.selectedDocument);
    this.container.querySelector('.observed-source-grid').innerHTML = locations ? [...locations.values()].map(location => `<button type="button" class="observed-source" data-source-element="${html(location.elementId)}" title="${html(location.elementId)}"><span>${html(location.displayName)}</span><code>${html(location.elementId)}</code><small>本帧未活动</small></button>`).join('') : '<p class="observation-empty">本次记录没有可定位的活动操作来源。</p>';
    this.container.querySelectorAll('[data-source-element]').forEach(button => {
      button.onclick = () => { this.selectedElement = button.dataset.sourceElement; this.showSources(); };
    });
  }

  showFrame(frame, index) {
    this.frame = frame;
    if (!this.route) return;
    const routeFrame = this.route.frames[index];
    const cursor = this.container.querySelector('.observed-cursor');
    cursor.hidden = false;
    cursor.style.setProperty('--cursor-position', `${this.position(routeFrame.seconds)}%`);
    this.container.querySelector('[data-timeline-frame]').textContent = `确认帧 ${frame.hostFrame} · ${frame.seconds.toFixed(3)} 秒`;
    this.container.querySelectorAll('[data-timeline-id]').forEach(output => {
      const playbacks = routeFrame.playbacks.filter(playback => playback.documentId === output.dataset.timelineId);
      output.textContent = playbacks.length ? playbacks.map(playback => `${playback.cursorSeconds.toFixed(4)} 秒 · ${playback.status}`).join(' / ') : '本帧未活动';
      output.closest('.observed-timeline-row').classList.toggle('active', playbacks.length > 0);
    });
    this.routeFrame = routeFrame;
    this.showSources();
    this.container.dataset.observationRun = this.runId;
    this.container.dataset.observationFrame = frame.hostFrame;
  }

  showSources() {
    if (!this.frame) return;
    const active = new Set(this.routeFrame.active.filter(location => location.documentId === this.selectedDocument).map(location => location.elementId));
    let count = 0;
    this.container.querySelectorAll('[data-source-element]').forEach(button => {
      const current = active.has(button.dataset.sourceElement);
      button.classList.toggle('active', current);
      button.setAttribute('aria-pressed', String(button.dataset.sourceElement === this.selectedElement));
      button.querySelector('small').textContent = current ? '本帧活动' : '本帧未活动';
      if (current) count++;
    });
    this.container.querySelector('[data-source-count]').textContent = `帧 ${this.frame.hostFrame} · 此文档 ${count} 个活动来源`;
    const records = this.frame.sections.flatMap(section => section.records.filter(record => record.locations.some(location => location.documentId === this.selectedDocument && (!this.selectedElement || location.elementId === this.selectedElement))).map(record => ({ section: section.displayName, record })));
    this.container.querySelector('.observed-source-values').innerHTML = `<h3>本帧关联记录${this.selectedElement ? ` · ${html(this.selectedElement)}` : ''}</h3>${records.map(({ section, record }) => `<article><strong>${html(section)} · ${html(record.displayName)}</strong><dl>${fieldsHtml(record.fields)}</dl></article>`).join('') || '<p class="observation-note">该来源在本帧没有关联记录。</p>'}<p class="observation-note">端口采样值不可用：宿主尚未交付该通道。</p>`;
  }
}
