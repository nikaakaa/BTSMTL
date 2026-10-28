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

function timeRangeFields(range) {
  const value = key => range === undefined ? '' : html(range[key]);
  return `${rawFields.map(([key, label]) => `<label>${label} · Raw<input name="${key}" value="${value(key)}" inputmode="numeric" pattern="-?[0-9]+" required></label>`).join('')}${['sourceStart', 'sourceEnd'].map(key => `<label>${key}<input name="${key}" value="${value(key)}" type="number" step="any" required></label>`).join('')}`;
}

export function editTimeRange(clip, scale, apply) {
  dialog('配置完整片段时间', `<p>时间以正式 Raw 整数提交，每秒 ${html(scale)} 单位。素材起止保持原 Single 协议。合法区间由共同领域接收。</p>${timeRangeFields(clip.timeRange)}`,
    form => apply(timelineRange(Object.fromEntries(new FormData(form)))));
}

export function editMarkerTime(marker, scale, apply) {
  dialog('配置标记时刻', `<p>每秒 ${html(scale)} 单位。原始整数不经过浮点转换。</p><label>时刻 · Raw<input name="time" value="${html(marker.timeRaw)}" inputmode="numeric" pattern="-?[0-9]+" required></label>`,
    form => apply(new FormData(form).get('time')));
}

function timelineFieldDescription(catalog, kind) {
  return ordinaryFields(catalog.find(item => item.kind === kind).description);
}

function ordinaryFields(description) {
  return { ...description, fields: description.fields.filter(field => field.documentMember === 'Field') };
}

export function timelineObjectDefinition(target, definitions) {
  if (definitions.timelineAuthoring === undefined) return null;
  if (Array.isArray(target.tracks)) return ordinaryFields(definitions.timelineAuthoring.document);
  if (Array.isArray(target.clips))
    return timelineFieldDescription(definitions.timelineAuthoring.tracks, target.contract);
  if (target.timeRange)
    return timelineFieldDescription(definitions.timelineAuthoring.clips, target.contract);
  if (Object.hasOwn(target, 'timeRaw')) return ordinaryFields(definitions.timelineAuthoring.marker.description);
  return ordinaryFields(definitions.timelineAuthoring.binding);
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
  let readFields;
  const modal = dialog('新建轨道', `<label>轨道类型<select name="contract">${contracts.map(contract =>
    `<option value="${html(contract.kind)}">${html(contract.kind)} · ${html(contract.allowedExecutionDomains)}</option>`).join('')}</select></label><label>轨道名称<input name="displayName" required autocomplete="off"></label><p>参数来自所选轨道的正式作者定义。切换类型会重新读取该类型的初始值；修改将插入当前时间轴末尾。</p><div data-track-fields></div>`,
    form => {
      const input = new FormData(form);
      const contractKind = input.get('contract');
      return apply([{ kind: 'insert-track', index: timeline.tracks.length, track: {
        id: `local:${crypto.randomUUID()}`, contractKind, displayName: input.get('displayName'),
        fields: readFields(), clips: [], markers: []
      } }]);
    });
  const fill = () => {
    const description = timelineFieldDescription(definitions.timelineAuthoring.tracks, modal.querySelector('select').value);
    readFields = bindNewTimelineFields(modal.querySelector('[data-track-fields]'), description, references);
  };
  modal.querySelector('select').onchange = fill;
  fill();
}

function bindNewTimelineFields(surface, description, references) {
  const fields = description.fields.filter(field => field.visible && field.writable);
  const editors = new Map();
  surface.innerHTML = fields.map(field => `<fieldset><legend>${html(field.displayName || field.id)}</legend><div data-new-timeline-field="${html(field.id)}"></div></fieldset>`).join('');
  surface.querySelectorAll('[data-new-timeline-field]').forEach(container => {
    const field = fields.find(field => field.id === container.dataset.newTimelineField);
    editors.set(field.id, bindValueEditor(container, field.defaultValue, field.constraint.AllowedValues,
      references, field.valueDefinition));
  });
  return () => Object.fromEntries(description.fields.map(field => [field.id, editors.has(field.id)
    ? editors.get(field.id).read() : structuredClone(field.defaultValue)]));
}

export function createClip(track, definitions, references, scale, apply) {
  const kinds = definitions.timelineContracts.tracks.find(contract => contract.kind === track.contract).allowedClipKinds;
  let readFields;
  const modal = dialog('在当前轨道新建片段', `<p>目标轨道：${html(track.displayName)}。片段类型来自该轨道的正式合同，参数初始值来自共同作者定义。</p><label>片段类型<select name="contract">${kinds.map(kind =>
    `<option value="${html(kind)}">${html(kind)}</option>`).join('')}</select></label><label>片段名称<input name="displayName" required autocomplete="off"></label><fieldset><legend>完整时间与素材范围</legend><p>每秒 ${html(scale)} 个 Raw 单位。正式描述未提供新片段时间模板，以下九项须明确填写；本页不复制已有片段的范围。</p>${timeRangeFields()}</fieldset><div data-clip-fields></div><p>修改将通过共同服务插入轨道末尾。需要私有图的片段仍须满足正式 owner 合同，不能借用其它片段的可变私有图。</p>`,
    form => {
      const input = new FormData(form);
      return apply([{ kind: 'insert-clip', trackId: track.id, index: track.clips.length, clip: {
        id: `local:${crypto.randomUUID()}`, contractKind: input.get('contract'), displayName: input.get('displayName'),
        range: timelineRange(Object.fromEntries(input)), fields: readFields()
      } }]);
    });
  const fill = () => {
    const description = timelineFieldDescription(definitions.timelineAuthoring.clips, modal.querySelector('select').value);
    readFields = bindNewTimelineFields(modal.querySelector('[data-clip-fields]'), description, references);
  };
  modal.querySelector('select').onchange = fill;
  fill();
}

export function createMarker(track, definitions, references, scale, apply) {
  const contract = definitions.timelineAuthoring.marker;
  const time = contract.description.fields.find(field => field.documentMember === 'Time');
  let readFields;
  const modal = dialog('在当前轨道新建标记', `<p>目标轨道：${html(track.displayName)}。类型与参数来自正式标记定义。每秒 ${html(scale)} 个 Raw 单位；时刻由文档结构保存。</p><label>标记名称<input name="displayName" required autocomplete="off"></label><label>时刻 · Raw<input name="timeRaw" value="${html(time.defaultValue.value)}" inputmode="numeric" pattern="-?[0-9]+" required></label><div data-marker-fields></div>`,
    form => {
      const input = new FormData(form);
      return apply([{ kind: 'insert-marker', trackId: track.id, index: track.markers.length, marker: {
        id: `local:${crypto.randomUUID()}`, contractKind: contract.kind,
        displayName: input.get('displayName'), timeRaw: input.get('timeRaw'), fields: readFields()
      } }]);
    });
  readFields = bindNewTimelineFields(modal.querySelector('[data-marker-fields]'),
    ordinaryFields(contract.description), references);
}

export function createBinding(timeline, definitions, references, apply) {
  let readFields;
  const modal = dialog('新建时间轴绑定', '<p>绑定字段、枚举和初始值来自正式绑定描述。确认后加入当前时间轴末尾，由共同领域接收。</p><div data-binding-fields></div>',
    () => apply([{ kind: 'insert-binding', index: timeline.bindings.length, binding: {
      id: `local:${crypto.randomUUID()}`, fields: readFields()
    } }]));
  readFields = bindNewTimelineFields(modal.querySelector('[data-binding-fields]'),
    ordinaryFields(definitions.timelineAuthoring.binding), references);
}

function clipTrackChoices(timeline, clip, contracts) {
  return timeline.tracks.filter(item => contracts.tracks.find(contract => contract.kind === item.contract)
    .allowedClipKinds.includes(clip.contract));
}

export function moveClip(timeline, track, clip, contracts, apply) {
  const targets = clipTrackChoices(timeline, clip, contracts);
  const modal = dialog('移动片段及调整顺序', `<label>目标轨道<select name="track">${targets.map(item => `<option value="${html(item.id)}" ${item.id === track.id ? 'selected' : ''}>${html(item.displayName)} · ${html(item.contract)}</option>`).join('')}</select></label><label>目标轨道内的顺序<select name="index"></select></label><p>菜单按共同轨道合同列出可接收此片段的轨道。可调整同轨顺序或跨轨插入位置；默认放在目标轨道末尾。片段身份、图引用和时间保持原值，修改交给同一领域候选。</p>`,
    form => {
      const input = new FormData(form);
      return apply({ kind: 'move-clip', sourceTrackId: track.id, targetTrackId: input.get('track'),
        clipId: clip.id, index: Number(input.get('index')) });
    });
  const fillPositions = () => {
    const target = targets.find(item => item.id === modal.querySelector('[name=track]').value);
    const others = target.clips.filter(item => item.id !== clip.id);
    modal.querySelector('[name=index]').innerHTML = Array.from({ length: others.length + 1 }, (_, index) =>
      `<option value="${index}" ${index === others.length ? 'selected' : ''}>${index + 1} · ${index === others.length ? '末尾' : `位于 ${html(others[index].displayName)} 之前`}</option>`).join('');
  };
  modal.querySelector('[name=track]').onchange = fillPositions;
  fillPositions();
}

export function bindTimelineDrag(surface, timeline, contracts, scale, pixels, apply, onError) {
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
      pointerId: event.pointerId, x: event.clientX, y: event.clientY, delta: 0n, translate: element.style.translate,
      left: element.style.left, width: element.style.width, zIndex: element.style.zIndex, targetTrack: track,
      targets: clip ? clipTrackChoices(timeline, clip, contracts) : null, lane: null };
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
    else {
      drag.element.style.translate = `${distance}px ${drag.clip ? event.clientY - drag.y : 0}px`;
      if (drag.clip) {
        drag.element.style.zIndex = '6';
        drag.lane?.classList.remove('timeline-drop-target', 'timeline-drop-blocked');
        drag.lane = surface.ownerDocument.elementsFromPoint(event.clientX, event.clientY)
          .find(element => {
            if (!element.matches('.timeline-lane')) return false;
            const bounds = element.getBoundingClientRect();
            return event.clientX >= bounds.left && event.clientX < bounds.right &&
              event.clientY >= bounds.top && event.clientY < bounds.bottom;
          });
        drag.targetTrack = drag.lane ? timeline.tracks.find(track => track.id === drag.lane.dataset.timelineTrack) : null;
        drag.lane?.classList.add(drag.targets.includes(drag.targetTrack) ? 'timeline-drop-target' : 'timeline-drop-blocked');
      }
    }
  });
  const restore = gesture => {
    gesture.element.style.translate = gesture.translate;
    gesture.element.style.left = gesture.left;
    gesture.element.style.width = gesture.width;
    gesture.element.style.zIndex = gesture.zIndex;
    gesture.lane?.classList.remove('timeline-drop-target', 'timeline-drop-blocked');
  };
  surface.addEventListener('pointerup', async event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    const { element, track, clip, marker, boundary, delta, targetTrack, targets } = drag;
    restore(drag);
    drag = null;
    if (delta === 0n && targetTrack === track) return;
    suppressClick = element;
    const destination = clip && !boundary ? targetTrack : track;
    if (clip && !boundary && !targets.includes(destination)) {
      onError(new Error(destination ? `轨道“${destination.displayName}”不接收此片段类型，本次拖动未提交。`
        : '请将片段放到时间轴的轨道区域内，本次拖动未提交。'));
      return;
    }
    const edits = [];
    if (destination !== track) edits.push({ kind: 'move-clip', sourceTrackId: track.id,
      targetTrackId: destination.id, clipId: clip.id, index: destination.clips.length });
    if (delta !== 0n) edits.push(clip ? { kind: 'set-clip-range', trackId: destination.id, clipId: clip.id,
      range: timelineRange({ ...clip.timeRange,
        startRaw: boundary === 'end' ? clip.timeRange.startRaw : String(BigInt(clip.timeRange.startRaw) + delta),
        endRaw: boundary === 'start' ? clip.timeRange.endRaw : String(BigInt(clip.timeRange.endRaw) + delta) }) }
      : { kind: 'configure-marker', trackId: track.id, markerId: marker.id, displayName: marker.displayName,
        timeRaw: String(BigInt(marker.timeRaw) + delta), fields: marker.fields });
    try { await apply(edits); } catch (error) { onError(error); }
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
