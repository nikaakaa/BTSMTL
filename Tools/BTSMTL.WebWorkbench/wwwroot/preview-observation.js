import { html } from './preview-records.js';
import { PreviewGraph } from './preview-graph.js';

const inputKey = root => JSON.stringify([root.actorId, root.programId, root.programRevision,
  root.planId, root.planRevision, root.schemeIndex]);
const fieldsText = fields => fields.map(field => `${field.displayName}=${field.value}`).join(' · ');
const writesText = writes => writes.map(write => `${write.seconds.toFixed(3)} s · ${write.keyId} · ${fieldsText(write.fields)}`).join('\n');

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
    this.metadataAvailability = [];
    this.definitions = [];
    this.inputTimeline = null;
    this.selectedInput = '';
    this.inputsExpanded = true;
    this.timelines = new Map();
    this.container.innerHTML = '<section class="observed-timeline" aria-label="技能 Timeline 预览"><div class="observed-timeline-scroll"><div class="observed-timeline-content"></div></div></section><section class="observed-graph" aria-label="节点图实际运行观察"></section>';
    this.graph = new PreviewGraph(this.container.querySelector('.observed-graph'), () => this.reportAvailability());
    this.mountTimeline();
    this.availabilityChanged([]);
  }

  async load(runId) {
    this.runId = runId;
    const loading = this.loading = {};
    const [graphs, inputs, timelines] = await Promise.all([
      this.readPages(`/api/preview/runs/${runId}/graphs`, loading),
      this.request(`/api/preview/runs/${runId}/input-timeline`),
      this.readPages(`/api/preview/runs/${runId}/timelines`, loading)
    ]);
    if (this.loading !== loading) return;
    this.definitions = graphs.items;
    this.availability = graphs.availability;
    this.inputTimeline = inputs;
    this.selectedInput = inputs.roots.length ? inputKey(inputs.roots[0]) : '';
    this.timelines = new Map(timelines.items.map(item => [item.timeline.documentId, item.timeline]));
    this.metadataAvailability = [...inputs.availability, ...timelines.availability];
    this.container.dataset.graphProtocol = graphs.protocol;
    this.container.dataset.inputProtocol = inputs.protocol;
    this.container.dataset.timelineProtocol = timelines.protocol;
    this.container.dataset.observationContent = graphs.contentRevision;
    this.graph.setDefinitions(this.definitions, this.availability);
    this.mountTimeline();
  }

  async readPages(path, loading) {
    const items = [];
    let offset = 0;
    let page;
    do {
      page = await this.request(`${path}?offset=${offset}&limit=64`);
      if (this.loading !== loading) return;
      items.push(...page.items);
      offset = page.nextOffset;
    } while (offset !== null);
    return { ...page, items };
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
    this.availabilityChanged([...this.availability, ...this.metadataAvailability, ...this.graph.availability,
      ...(this.frame ? this.frame.route.availability : [])]);
  }

  inputRows() {
    if (!this.inputTimeline || !this.inputTimeline.roots.length) {
      const reason = this.inputTimeline ? this.inputTimeline.availability[0].reason : '正在读取本次运行的输入方案';
      return `<section class="observed-timeline-row observed-input-group" data-availability="unavailable"><div class="observed-track-name"><strong>预览输入</strong><button class="input-scheme" disabled title="${html(reason)}">方案 ▾</button></div><div class="observed-timeline-lane"></div></section>`;
    }
    const roots = this.inputTimeline.roots;
    const root = roots.find(root => inputKey(root) === this.selectedInput);
    const identity = `${root.actorId} · ${root.programId}\n${root.programRevision}\n${root.planId}\n${root.planRevision}`;
    return `<section class="observed-timeline-row observed-input-group" data-availability="available" data-input-actor="${html(root.actorId)}" data-input-program="${html(root.programId)}" data-input-revision="${html(root.programRevision)}" data-input-plan="${html(root.planId)}" data-input-plan-revision="${html(root.planRevision)}"><div class="observed-track-name"><button class="observed-track-toggle" data-input-toggle aria-expanded="${this.inputsExpanded}">${this.inputsExpanded ? '▾' : '▸'} 预览输入</button><select class="input-scheme" data-input-choice aria-label="已采用输入方案" title="${html(identity)}" ${roots.length < 2 ? 'disabled' : ''}>${roots.map(item => `<option value="${html(inputKey(item))}" ${inputKey(item) === this.selectedInput ? 'selected' : ''}>${html(item.displayName)}</option>`).join('')}</select></div><div class="observed-timeline-lane"></div></section>
      ${this.inputsExpanded ? root.tracks.map(track => `<div class="observed-timeline-row observed-input-track" data-input-track="${html(track.id)}"><div class="observed-track-name" title="${html(`${track.displayName}\n${track.id}`)}"><strong>${html(track.displayName)}</strong></div><div class="observed-timeline-lane" data-recorded-lane>${track.clips.map(clip => `<div class="observed-timeline-segment observed-input-clip" data-input-clip="${html(clip.id)}" data-input-start="${clip.startSeconds}" data-input-end="${clip.endSeconds}" style="left:${this.position(clip.startSeconds)}%;width:${this.position(clip.endSeconds) - this.position(clip.startSeconds)}%" title="${html(`${clip.startSeconds.toFixed(3)} — ${clip.endSeconds.toFixed(3)} s\n${writesText(clip.writes)}\n${clip.graphId}`)}">${html([...new Set(clip.writes.map(write => write.keyId))].join(' / '))}</div>`).join('')}${track.markers.map(marker => `<span class="observed-input-marker" data-input-marker="${html(marker.id)}" data-input-seconds="${marker.seconds}" style="left:${this.position(marker.seconds)}%" title="${html(`${marker.seconds.toFixed(3)} s\n${writesText(marker.writes)}\n${marker.graphId}`)}"></span>`).join('')}</div></div>`).join('') : ''}`;
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
    this.container.querySelector('.observed-timeline-content').innerHTML = `<div class="observed-time-ruler"><span>轨道</span><div role="slider" tabindex="${this.routeFrames.length ? 0 : -1}" aria-label="记录游标" aria-disabled="${!this.routeFrames.length}" aria-valuemin="0" aria-valuemax="${Math.max(0, this.routeFrames.length - 1)}" aria-valuenow="0">${Array.from({ length: 9 }, (_, index) => `<span style="left:${index * 12.5}%">${(this.firstSeconds + this.duration * index / 8).toFixed(2)}</span>`).join('')}</div></div>
      ${this.inputRows()}
      ${[...timelines].map(([id, segments]) => `<div class="observed-timeline-row"><div class="observed-track-name" title="${html(id)}"><strong>${html(this.timelines.has(id) ? this.timelines.get(id).displayName : id.slice(0, 8))}</strong><output data-timeline-id="${html(id)}"></output></div><div class="observed-timeline-lane" data-recorded-lane>${segments.map(segment => `<div class="observed-timeline-segment" style="left:${this.position(segment.first.seconds)}%;width:${this.position(segment.last.seconds) - this.position(segment.first.seconds)}%" title="${html(id)} · 实际记录 ${html(segment.first.hostFrame)} — ${html(segment.last.hostFrame)}"></div>`).join('')}</div></div>`).join('')}
      <div class="observed-cursor" hidden><i></i></div>`;
    if (this.routeFrames.length) {
      const ruler = this.container.querySelector('[role=slider]');
      ruler.onkeydown = event => {
        let index = Number(ruler.getAttribute('aria-valuenow'));
        switch (event.key) {
          case 'ArrowLeft': index--; break;
          case 'ArrowRight': index++; break;
          case 'Home': index = 0; break;
          case 'End': index = this.routeFrames.length - 1; break;
          default: return;
        }
        event.preventDefault();
        this.seek(Math.max(0, Math.min(this.routeFrames.length - 1, index)));
      };
      this.container.querySelectorAll('.observed-time-ruler > div, [data-recorded-lane]').forEach(lane => {
        lane.onpointerdown = event => { lane.setPointerCapture(event.pointerId); this.seekRuler(event, lane); };
        lane.onpointermove = event => { if (lane.hasPointerCapture(event.pointerId)) this.seekRuler(event, lane); };
        lane.onpointerup = event => lane.releasePointerCapture(event.pointerId);
      });
    }
    const inputToggle = this.container.querySelector('[data-input-toggle]');
    if (inputToggle) inputToggle.onclick = () => { this.inputsExpanded = !this.inputsExpanded; this.mountTimeline(); };
    const inputChoice = this.container.querySelector('[data-input-choice]');
    if (inputChoice) inputChoice.onchange = event => { this.selectedInput = event.target.value; this.mountTimeline(); };
    if (this.frame) this.showTimelineFrame(this.routeFrames.findIndex(frame => frame.hostFrame === this.frame.hostFrame));
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
    this.showTimelineFrame(index);
    this.graph.showFrame(frame);
    this.container.dataset.observationRun = this.runId;
    this.container.dataset.observationFrame = frame.hostFrame;
  }

  showTimelineFrame(index) {
    const routeFrame = this.routeFrames[index];
    const ruler = this.container.querySelector('[role=slider]');
    ruler.setAttribute('aria-valuenow', String(index));
    ruler.setAttribute('aria-valuetext', `${routeFrame.hostFrame} f · ${routeFrame.seconds.toFixed(3)} s`);
    const cursor = this.container.querySelector('.observed-cursor');
    cursor.hidden = false;
    cursor.style.setProperty('--cursor-position', `${this.position(routeFrame.seconds)}%`);
    this.container.querySelectorAll('[data-timeline-id]').forEach(output => {
      const playbacks = routeFrame.playbacks.filter(playback => playback.documentId === output.dataset.timelineId);
      output.textContent = playbacks.length ? playbacks.map(playback => `${playback.cursorSeconds.toFixed(3)} s`).join(' / ') : '';
      output.closest('.observed-timeline-row').classList.toggle('active', playbacks.length > 0);
    });
  }
}
