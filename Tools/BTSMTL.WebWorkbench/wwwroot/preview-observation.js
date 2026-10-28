import { html, fieldsHtml } from './preview-records.js';

export class PreviewObservation {
  constructor(container, request, seek) {
    this.container = container;
    this.request = request;
    this.seek = seek;
    this.reset();
  }

  reset() {
    this.loading = null;
    this.runId = null;
    this.route = null;
    this.routeFrame = null;
    this.frame = null;
    this.documents = new Map();
    this.selectedDocument = '';
    this.selectedElement = '';
    this.container.innerHTML = '<div class="observation-empty">选择运行</div>';
  }

  async load(runId) {
    this.runId = runId;
    const loading = this.loading = {};
    const route = await this.request(`/api/preview/runs/${runId}/route`);
    if (this.loading !== loading) return;
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
      <div class="observed-timeline-scroll"><div class="observed-timeline-content">
        <div class="observed-time-ruler"><span>轨道</span><div>${Array.from({ length: 9 }, (_, index) => `<span style="left:${index * 12.5}%">${(this.firstSeconds + this.duration * index / 8).toFixed(2)}</span>`).join('')}</div></div>
        <section class="observed-timeline-row observed-input-group" data-availability="unavailable"><div class="observed-track-name"><strong>▾ 预览输入</strong></div><div class="observed-timeline-lane"><button class="input-scheme" disabled title="宿主尚未提供正式输入方案与按键轨">方案 ▾</button></div></section>
        ${[...timelines].map(([id, samples], group) => `<div class="observed-timeline-row"><div class="observed-track-name" title="${html(id)}"><strong>▾ Timeline ${group + 1}</strong><output data-timeline-id="${html(id)}"></output></div><div class="observed-timeline-lane">${samples.map(({ frame, index, playback }) => `<button type="button" class="observed-timeline-sample" data-record-index="${index}" style="left:${this.position(frame.seconds)}%" aria-label="查看帧 ${html(frame.hostFrame)} 的 Timeline 记录" title="帧 ${html(frame.hostFrame)} · 游标 ${playback.cursorSeconds.toFixed(4)} 秒 · ${html(playback.status)}"></button>`).join('')}</div></div>`).join('')}
        <div class="observed-cursor" hidden><i></i></div>
      </div></div></section>
      <section class="observed-sources" aria-label="操作状态观察">
        <header><strong>操作状态</strong><select data-source-document aria-label="来源文档">${[...this.documents.keys()].map((id, index) => `<option value="${html(id)}" title="${html(id)}">来源 ${index + 1}</option>`).join('')}</select><output data-source-count title="本帧有状态记录的来源"></output></header>
        <div class="observed-source-body"><div class="observed-source-grid"></div>
        <section class="observed-source-values" aria-label="所选来源的本帧记录" hidden></section></div>
      </section>`;
    this.container.querySelectorAll('[data-record-index]').forEach(button => {
      button.onclick = () => this.seek(Number(button.dataset.recordIndex));
    });
    if (route.frames.length) {
      const ruler = this.container.querySelector('.observed-time-ruler > div');
      ruler.onpointerdown = event => { ruler.setPointerCapture(event.pointerId); this.seekRuler(event, ruler); };
      ruler.onpointermove = event => { if (ruler.hasPointerCapture(event.pointerId)) this.seekRuler(event, ruler); };
      ruler.onpointerup = event => ruler.releasePointerCapture(event.pointerId);
    }
    this.container.querySelector('[data-source-document]').onchange = event => {
      this.selectedDocument = event.target.value;
      this.selectedElement = '';
      this.mountSources();
      this.showSources();
    };
    this.mountSources();
  }

  position(seconds) { return this.duration ? (seconds - this.firstSeconds) / this.duration * 100 : 0; }

  seekRuler(event, ruler) {
    const bounds = ruler.getBoundingClientRect();
    const seconds = this.firstSeconds + Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width)) * this.duration;
    const frames = this.route.frames;
    let first = 0, last = frames.length - 1;
    while (first < last) {
      const middle = Math.floor((first + last) / 2);
      if (frames[middle].seconds < seconds) first = middle + 1;
      else last = middle;
    }
    this.seek(first > 0 && seconds - frames[first - 1].seconds < frames[first].seconds - seconds ? first - 1 : first);
  }

  mountSources() {
    const locations = this.documents.get(this.selectedDocument);
    this.container.querySelector('.observed-source-grid').innerHTML = locations ? [...locations.values()].map((location, index) => `<button type="button" class="observed-source" data-source-element="${html(location.elementId)}" title="${html(location.elementId)}">${html(location.displayName)} ${index + 1}</button>`).join('') : '<div class="observation-empty">没有节点记录</div>';
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
    this.container.querySelectorAll('[data-timeline-id]').forEach(output => {
      const playbacks = routeFrame.playbacks.filter(playback => playback.documentId === output.dataset.timelineId);
      output.textContent = playbacks.length ? playbacks.map(playback => `${playback.cursorSeconds.toFixed(3)} s`).join(' / ') : '';
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
      if (current) count++;
    });
    this.container.querySelector('[data-source-count]').textContent = `${count} 条记录`;
    this.container.querySelector('.observed-source-values').hidden = !this.selectedElement;
    if (!this.selectedElement) {
      this.container.querySelector('.observed-source-values').replaceChildren();
      return;
    }
    const records = this.frame.sections.flatMap(section => section.records.filter(record => record.locations.some(location => location.documentId === this.selectedDocument && location.elementId === this.selectedElement)).map(record => ({ section: section.displayName, record })));
    this.container.querySelector('.observed-source-values').innerHTML = `<h3>帧 ${html(this.frame.hostFrame)}</h3>${records.map(({ record }) => `<article><strong>${html(record.displayName)}</strong><dl>${fieldsHtml(record.fields)}</dl></article>`).join('') || '<p>无记录</p>'}`;
  }
}
