import { dialog } from './graph-edits.js';

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

export function moveClip(timeline, track, clip, apply) {
  dialog('移动片段到轨道', `<label>目标轨道<select name="track">${timeline.tracks.map(item => `<option value="${html(item.id)}" ${item.id === track.id ? 'selected' : ''}>${html(item.displayName)} · ${html(item.contract)}</option>`).join('')}</select></label><p>保留片段身份、图引用和时间，插入目标轨道末尾。轨道兼容性仍由共同领域接收。</p>`,
    form => {
      const target = timeline.tracks.find(item => item.id === new FormData(form).get('track'));
      return apply({ kind: 'move-clip', sourceTrackId: track.id, targetTrackId: target.id,
        clipId: clip.id, index: target.clips.length - (target.id === track.id ? 1 : 0) });
    });
}

export function bindTimelineDrag(surface, timeline, scale, pixels, apply, onError) {
  let drag = null, suppressClick = false;
  surface.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    const element = event.target.closest('.timeline-clip, .timeline-marker');
    if (!element) return;
    const track = timeline.tracks.find(track => [...track.clips, ...track.markers]
      .some(item => item.id === element.dataset.timeline));
    const clip = track.clips.find(item => item.id === element.dataset.timeline);
    const marker = track.markers.find(item => item.id === element.dataset.timeline);
    drag = { element, track, clip, marker, x: event.clientX, delta: 0n, translate: element.style.translate };
    element.setPointerCapture(event.pointerId);
  });
  surface.addEventListener('pointermove', event => {
    if (!drag) return;
    drag.delta = BigInt(Math.round((event.clientX - drag.x) * Number(scale) / pixels));
    drag.element.style.translate = `${event.clientX - drag.x}px 0`;
  });
  surface.addEventListener('pointerup', async () => {
    if (!drag) return;
    const { element, track, clip, marker, delta, translate } = drag;
    drag = null;
    element.style.translate = translate;
    if (delta === 0n) return;
    suppressClick = true;
    const edit = clip ? { kind: 'set-clip-range', trackId: track.id, clipId: clip.id,
      range: timelineRange({ ...clip.timeRange, startRaw: String(BigInt(clip.timeRange.startRaw) + delta),
        endRaw: String(BigInt(clip.timeRange.endRaw) + delta) }) }
      : { kind: 'configure-marker', trackId: track.id, markerId: marker.id, displayName: marker.displayName,
        timeRaw: String(BigInt(marker.timeRaw) + delta), fields: marker.fields };
    try { await apply([edit]); } catch (error) { onError(error); }
  });
  surface.addEventListener('pointercancel', () => {
    if (drag) drag.element.style.translate = drag.translate;
    drag = null;
  });
  surface.addEventListener('click', event => {
    if (suppressClick) { event.stopPropagation(); suppressClick = false; }
  }, true);
}
