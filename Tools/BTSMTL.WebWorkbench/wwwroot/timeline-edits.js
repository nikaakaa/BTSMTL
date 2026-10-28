import { bindValueEditor, dialog } from './graph-edits.js';

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const rawFields = [
  ['startRaw', '起点'], ['endRaw', '终点'], ['easeInRaw', '淡入'], ['easeOutRaw', '淡出'],
  ['clipInRaw', '片段偏移'], ['otherEaseInRaw', '相邻淡入'], ['otherEaseOutRaw', '相邻淡出']
];

export function timelineRange(range) {
  return { start: range.startRaw, end: range.endRaw, easeIn: range.easeInRaw, easeOut: range.easeOutRaw,
    clipIn: range.clipInRaw, sourceStart: Number(range.sourceStart), sourceEnd: Number(range.sourceEnd),
    otherEaseIn: range.otherEaseInRaw, otherEaseOut: range.otherEaseOutRaw };
}

export function editTimeRange(clip, scale, apply) {
  dialog('配置完整片段时间', `<p>时间以正式 Raw 整数提交，每秒 ${html(scale)} 单位。素材起止保持原 Single 协议。合法区间由共同领域接收。</p>${rawFields.map(([key, label]) => `<label>${label} · Raw<input name="${key}" value="${html(clip.timeRange[key])}" inputmode="numeric" pattern="-?[0-9]+" required></label>`).join('')}${['sourceStart', 'sourceEnd'].map(key => `<label>${key}<input name="${key}" value="${html(clip.timeRange[key])}" required></label>`).join('')}`,
    form => apply(timelineRange(Object.fromEntries(new FormData(form)))));
}

export function editMarkerTime(marker, scale, apply) {
  dialog('配置标记时刻', `<p>每秒 ${html(scale)} 单位。原始整数不经过浮点转换。</p><label>时刻 · Raw<input name="time" value="${html(marker.timeRaw)}" inputmode="numeric" pattern="-?[0-9]+" required></label>`,
    form => apply(new FormData(form).get('time')));
}

function timelineFieldDescription(catalog, kind) {
  const description = catalog.find(item => item.kind === kind).description;
  return { ...description, fields: description.fields.filter(field => field.documentMember === 'Field') };
}

export function timelineObjectDefinition(target, definitions) {
  if (definitions.timelineAuthoring === undefined) return null;
  if (Array.isArray(target.clips))
    return timelineFieldDescription(definitions.timelineAuthoring.tracks, target.contract);
  if (target.timeRange)
    return timelineFieldDescription(definitions.timelineAuthoring.clips, target.contract);
  return null;
}

export function editTimelineFields(target, definition, references, apply) {
  const fields = definition.fields.filter(field => field.visible && field.writable);
  const initial = field => target.fields[field.id] || field.defaultValue;
  const editors = new Map();
  const modal = dialog('同时配置时间轴参数', `<p>字段、类型、选项和默认值来自此类型的正式作者定义。可同时填写模式及配套参数；只提交实际修改的字段，其余字段保留。</p>${fields.map(field =>
    `<fieldset><legend>${html(field.displayName || field.id)}${target.fields[field.id] ? '' : ' · 领域默认值'}</legend><div data-timeline-field-editor="${html(field.id)}"></div></fieldset>`).join('')}`,
    () => {
      const changed = fields.flatMap(field => {
        const value = editors.get(field.id).read();
        return JSON.stringify(value) === JSON.stringify(initial(field)) ? [] : [[field.id, value]];
      });
      if (changed.length) return apply({ ...target.fields, ...Object.fromEntries(changed) });
    });
  modal.querySelectorAll('[data-timeline-field-editor]').forEach(container => {
    const field = fields.find(field => field.id === container.dataset.timelineFieldEditor);
    editors.set(field.id, bindValueEditor(container, initial(field), field.constraint.AllowedValues,
      references, field.valueDefinition));
  });
}

export function createTrack(timeline, definitions, references, apply) {
  const contracts = definitions.timelineContracts.tracks;
  const editors = new Map();
  const modal = dialog('新建轨道', `<label>轨道类型<select name="contract">${contracts.map(contract =>
    `<option value="${html(contract.kind)}">${html(contract.kind)} · ${html(contract.allowedExecutionDomains)}</option>`).join('')}</select></label><label>轨道名称<input name="displayName" required autocomplete="off"></label><p>参数来自所选轨道的正式作者定义。切换类型会重新读取该类型的初始值；修改将插入当前时间轴末尾。</p><div data-track-fields></div>`,
    form => {
      const input = new FormData(form);
      const contractKind = input.get('contract');
      const description = timelineFieldDescription(definitions.timelineAuthoring.tracks, contractKind);
      return apply([{ kind: 'insert-track', index: timeline.tracks.length, track: {
        id: `local:${crypto.randomUUID()}`, contractKind, displayName: input.get('displayName'),
        fields: Object.fromEntries(description.fields.map(field => [field.id, editors.has(field.id)
          ? editors.get(field.id).read() : structuredClone(field.defaultValue)])), clips: [], markers: []
      } }]);
    });
  const fill = () => {
    const description = timelineFieldDescription(definitions.timelineAuthoring.tracks, modal.querySelector('select').value);
    const fields = description.fields.filter(field => field.visible && field.writable);
    editors.clear();
    const surface = modal.querySelector('[data-track-fields]');
    surface.innerHTML = fields.map(field => `<fieldset><legend>${html(field.displayName || field.id)}</legend><div data-track-field="${html(field.id)}"></div></fieldset>`).join('');
    surface.querySelectorAll('[data-track-field]').forEach(container => {
      const field = fields.find(field => field.id === container.dataset.trackField);
      editors.set(field.id, bindValueEditor(container, field.defaultValue, field.constraint.AllowedValues,
        references, field.valueDefinition));
    });
  };
  modal.querySelector('select').onchange = fill;
  fill();
}

export function moveClip(timeline, track, clip, contracts, apply) {
  const targets = timeline.tracks.filter(item => contracts.tracks.find(contract => contract.kind === item.contract)
    .allowedClipKinds.includes(clip.contract));
  dialog('移动片段到轨道', `<label>目标轨道<select name="track">${targets.map(item => `<option value="${html(item.id)}" ${item.id === track.id ? 'selected' : ''}>${html(item.displayName)} · ${html(item.contract)}</option>`).join('')}</select></label><p>菜单按共同轨道合同列出可接收此片段的轨道。保留片段身份、图引用和时间，插入目标轨道末尾，形成同一领域候选。</p>`,
    form => {
      const target = timeline.tracks.find(item => item.id === new FormData(form).get('track'));
      return apply({ kind: 'move-clip', sourceTrackId: track.id, targetTrackId: target.id,
        clipId: clip.id, index: target.clips.length - (target.id === track.id ? 1 : 0) });
    });
}

export function bindTimelineDrag(surface, timeline, scale, pixels, apply, onError) {
  let drag = null, suppressClick = false;
  surface.addEventListener('pointerdown', event => {
    if (drag || event.button !== 0) return;
    const element = event.target.closest('.timeline-clip, .timeline-marker');
    if (!element) return;
    const track = timeline.tracks.find(track => [...track.clips, ...track.markers]
      .some(item => item.id === element.dataset.timeline));
    const clip = track.clips.find(item => item.id === element.dataset.timeline);
    const marker = track.markers.find(item => item.id === element.dataset.timeline);
    const boundary = event.target.closest('[data-timeline-boundary]');
    drag = { element, track, clip, marker, boundary: boundary?.dataset.timelineBoundary,
      pointerId: event.pointerId, x: event.clientX, delta: 0n, translate: element.style.translate,
      left: element.style.left, width: element.style.width };
    element.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  surface.addEventListener('pointermove', event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    drag.delta = BigInt(Math.round((event.clientX - drag.x) * Number(scale) / pixels));
    const distance = Number(drag.delta) * pixels / Number(scale);
    if (drag.boundary === 'start') {
      drag.element.style.left = `${parseFloat(drag.left) + distance}px`;
      drag.element.style.width = `${Math.max(4, parseFloat(drag.width) - distance)}px`;
    } else if (drag.boundary === 'end') drag.element.style.width = `${Math.max(4, parseFloat(drag.width) + distance)}px`;
    else drag.element.style.translate = `${distance}px 0`;
  });
  const restore = gesture => {
    gesture.element.style.translate = gesture.translate;
    gesture.element.style.left = gesture.left;
    gesture.element.style.width = gesture.width;
  };
  surface.addEventListener('pointerup', async event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    const { element, track, clip, marker, boundary, delta } = drag;
    restore(drag);
    drag = null;
    if (delta === 0n) return;
    suppressClick = element;
    const edit = clip ? { kind: 'set-clip-range', trackId: track.id, clipId: clip.id,
      range: timelineRange({ ...clip.timeRange,
        startRaw: boundary === 'end' ? clip.timeRange.startRaw : String(BigInt(clip.timeRange.startRaw) + delta),
        endRaw: boundary === 'start' ? clip.timeRange.endRaw : String(BigInt(clip.timeRange.endRaw) + delta) }) }
      : { kind: 'configure-marker', trackId: track.id, markerId: marker.id, displayName: marker.displayName,
        timeRaw: String(BigInt(marker.timeRaw) + delta), fields: marker.fields };
    try { await apply([edit]); } catch (error) { onError(error); }
  });
  const cancel = event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    restore(drag);
    drag = null;
  };
  surface.addEventListener('pointercancel', cancel);
  surface.addEventListener('lostpointercapture', cancel);
  surface.addEventListener('click', event => {
    if (suppressClick) {
      if (suppressClick.contains(event.target)) event.stopPropagation();
      suppressClick = false;
    }
  }, true);
}
