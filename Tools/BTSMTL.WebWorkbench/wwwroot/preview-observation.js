import { html } from './preview-records.js';
import { PreviewGraph } from './preview-graph.js';

export class PreviewObservation {
  constructor(container, request, seek, availabilityChanged) {
    this.container = container;
    this.request = request;
    this.seek = seek;
    this.availabilityChanged = availabilityChanged;
    this.reset();
  }

  reset() {
    this.loading = null;
    this.runId = null;
    this.routeFrames = [];
    this.frame = null;
    this.availability = [];
    this.definitions = [];
    this.container.innerHTML = '<section class="observed-timeline" aria-label="技能 Timeline 预览"><div class="observed-timeline-scroll"><div class="observed-timeline-content"></div></div></section><section class="observed-graph" aria-label="节点图实际运行观察"></section>';
    this.graph = new PreviewGraph(this.container.querySelector('.observed-graph'), () => this.reportAvailability());
    this.mountTimeline();
    this.availabilityChanged([]);
  }

  async load(runId) {
    this.runId = runId;
    const loading = this.loading = {};
    let offset = 0;
    do {
      const page = await this.request(`/api/preview/runs/${runId}/graphs?offset=${offset}&limit=64`);
      if (this.loading !== loading) return;
      this.definitions.push(...page.items);
      this.availability = page.availability;
      this.container.dataset.graphProtocol = page.protocol;
      this.container.dataset.observationContent = page.contentRevision;
      offset = page.nextOffset;
    } while (offset !== null);
    this.graph.setDefinitions(this.definitions, this.availability);
  }

  async sync(summary) {
    const loading = this.loading;
    const before = this.routeFrames.length;
    while (this.routeFrames.length < summary.frameCount) {
      const page = await this.request(`/api/preview/runs/${this.runId}/route/frames?firstFrame=${encodeURIComponent(summary.firstFrame)}&lastFrame=${encodeURIComponent(summary.lastFrame)}&offset=${this.routeFrames.length}&limit=${Math.min(128, summary.frameCount - this.routeFrames.length)}`);
      if (this.loading !== loading) return;
      this.routeFrames.push(...page.items.map(({ hostFrame, seconds, playbacks }) => ({ hostFrame, seconds, playbacks })));
      this.availability = page.availability;
      this.container.dataset.routeProtocol = page.protocol;
    }
    if (before !== this.routeFrames.length) this.mountTimeline();
  }

  reportAvailability() {
    this.availabilityChanged([...this.availability, ...this.graph.availability,
      ...(this.frame ? this.frame.route.availability : [])]);
  }

  mountTimeline() {
    const timelines = new Map();
    this.routeFrames.forEach((frame, index) => frame.playbacks.forEach(playback => {
      if (!timelines.has(playback.documentId)) timelines.set(playback.documentId, []);
      const segments = timelines.get(playback.documentId);
      const previous = segments.at(-1);
      if (previous && previous.lastIndex === index - 1) {
        previous.lastIndex = index; previous.last = frame;
      } else segments.push({ firstIndex: index, lastIndex: index, first: frame, last: frame });
    }));
    this.firstSeconds = this.routeFrames[0]?.seconds ?? 0;
    this.duration = this.routeFrames.length > 1 ? this.routeFrames.at(-1).seconds - this.firstSeconds : 0;
    this.container.querySelector('.observed-timeline-content').innerHTML = `<div class="observed-time-ruler"><span>轨道</span><div>${Array.from({ length: 9 }, (_, index) => `<span style="left:${index * 12.5}%">${(this.firstSeconds + this.duration * index / 8).toFixed(2)}</span>`).join('')}</div></div>
      <section class="observed-timeline-row observed-input-group" data-availability="unavailable"><div class="observed-track-name"><strong>▾ 预览输入</strong></div><div class="observed-timeline-lane"><button class="input-scheme" disabled title="宿主尚未提供正式输入方案与按键轨">方案 ▾</button></div></section>
      ${[...timelines].map(([id, segments]) => `<div class="observed-timeline-row"><div class="observed-track-name" title="${html(id)}"><strong>▾ ${html(id.slice(0, 8))}</strong><output data-timeline-id="${html(id)}"></output></div><div class="observed-timeline-lane" data-recorded-lane>${segments.map(segment => `<div class="observed-timeline-segment" style="left:${this.position(segment.first.seconds)}%;width:${this.position(segment.last.seconds) - this.position(segment.first.seconds)}%" title="${html(id)} · 实际记录 ${html(segment.first.hostFrame)} — ${html(segment.last.hostFrame)}"></div>`).join('')}</div></div>`).join('')}
      <div class="observed-cursor" hidden><i></i></div>`;
    if (this.routeFrames.length) {
      this.container.querySelectorAll('.observed-time-ruler > div, [data-recorded-lane]').forEach(lane => {
        lane.onpointerdown = event => { lane.setPointerCapture(event.pointerId); this.seekRuler(event, lane); };
        lane.onpointermove = event => { if (lane.hasPointerCapture(event.pointerId)) this.seekRuler(event, lane); };
        lane.onpointerup = event => lane.releasePointerCapture(event.pointerId);
      });
    }
  }

  position(seconds) { return this.duration ? (seconds - this.firstSeconds) / this.duration * 100 : 0; }

  seekRuler(event, ruler) {
    const bounds = ruler.getBoundingClientRect();
    const seconds = this.firstSeconds + Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width)) * this.duration;
    const frames = this.routeFrames;
    let first = 0, last = frames.length - 1;
    while (first < last) {
      const middle = Math.floor((first + last) / 2);
      if (frames[middle].seconds < seconds) first = middle + 1;
      else last = middle;
    }
    this.seek(first > 0 && seconds - frames[first - 1].seconds < frames[first].seconds - seconds ? first - 1 : first);
  }

  showFrame(frame, index) {
    this.frame = frame;
    const routeFrame = this.routeFrames[index];
    const cursor = this.container.querySelector('.observed-cursor');
    cursor.hidden = false;
    cursor.style.setProperty('--cursor-position', `${this.position(routeFrame.seconds)}%`);
    this.container.querySelectorAll('[data-timeline-id]').forEach(output => {
      const playbacks = routeFrame.playbacks.filter(playback => playback.documentId === output.dataset.timelineId);
      output.textContent = playbacks.length ? playbacks.map(playback => `${playback.cursorSeconds.toFixed(3)} s`).join(' / ') : '';
      output.closest('.observed-timeline-row').classList.toggle('active', playbacks.length > 0);
    });
    this.graph.showFrame(frame);
    this.container.dataset.observationRun = this.runId;
    this.container.dataset.observationFrame = frame.hostFrame;
  }
}
