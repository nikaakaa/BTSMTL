import { scalarControl } from './scalar-editor.js';

const html = value => String(value).replace(/[&<>"']/g,
  char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function bindCurveEditor(container, curve, definition, defaultCurve) {
  const keys = curve.members.keys.items;
  const initialKeys = defaultCurve?.kind === 'object' ? defaultCurve.members.keys.items : [];
  let selected = keys.length ? 0 : -1, drag = null, view;
  container.innerHTML = `<div class="curve-tools"><button type="button" data-curve-fit>适应关键点</button><button type="button" data-curve-copy>复制当前关键点</button><button type="button" data-curve-delete>删除当前关键点</button></div>${initialKeys.length ? `<div class="curve-tools"><label>原字段定义中的关键点<select data-curve-initial-key>${initialKeys.map((key, index) => `<option value="${index}">第 ${index + 1} 点 · 时间 ${html(key.members.time.value)} · 值 ${html(key.members.value.value)}</option>`).join('')}</select></label><button type="button" data-curve-create>添加所选关键点</button></div>` : '<p class="muted">原字段定义没有提供关键点初始值；已有关键点可复制、编辑或删除。</p>'}<svg class="curve-plot" viewBox="0 0 720 260" role="img" aria-label="曲线关键点、切线与权重"></svg><p class="curve-status" role="status"></p><div class="curve-selected"></div><div class="curve-wrap"></div>`;
  const svg = container.querySelector('svg'), controls = container.querySelector('.curve-selected');
  const number = (key, field) => Number(key.members[field].value);
  const weighted = (key, side) => key.members.weightedMode.value.split(',')
    .some(value => ['Both', side === 'in' ? 'In' : 'Out'].includes(value.trim()));
  const weight = (key, side) => weighted(key, side) ? number(key, `${side}Weight`) : 1 / 3;
  const x = time => 40 + (time - view.left) / view.width * 650;
  const y = value => 225 - (value - view.bottom) / view.height * 195;
  const point = key => `${x(number(key, 'time'))},${y(number(key, 'value'))}`;
  const memberDefinition = (definition, field) => definition?.members.find(member => member.id === field)?.definition;
  const keyDefinition = memberDefinition(definition, 'keys')?.element;

  function fit() {
    const times = keys.map(key => number(key, 'time')), values = keys.map(key => number(key, 'value'));
    const left = times.length ? Math.min(...times) : 0, right = times.length ? Math.max(...times) : 1;
    const bottom = values.length ? Math.min(...values) : 0, top = values.length ? Math.max(...values) : 1;
    const width = right - left || 1, height = top - bottom || 1;
    view = { left: left - width * 0.05, width: width * 1.1, bottom: bottom - height * 0.1, height: height * 1.2 };
  }

  function plot() {
    if (keys.some(key => !Number.isFinite(number(key, 'time')) || !Number.isFinite(number(key, 'value')))) {
      svg.replaceChildren();
      container.querySelector('.curve-status').textContent = '当前时间或取值无法绘图，原文本保留在字段中。';
      return;
    }
    const ordered = keys.map((key, index) => ({ key, index })).sort((a, b) => number(a.key, 'time') - number(b.key, 'time'));
    const paths = ordered.slice(1).map(({ key }, index) => {
      const previous = ordered[index].key;
      const time = number(previous, 'time'), value = number(previous, 'value');
      const duration = number(key, 'time') - time;
      if (!Number.isFinite(number(previous, 'outTangent')) || !Number.isFinite(number(key, 'inTangent')))
        return `M${point(previous)} H${x(number(key, 'time'))} V${y(number(key, 'value'))}`;
      const outgoing = duration * weight(previous, 'out'), incoming = duration * weight(key, 'in');
      return `M${point(previous)} C${x(time + outgoing)},${y(value + outgoing * number(previous, 'outTangent'))} ${x(number(key, 'time') - incoming)},${y(number(key, 'value') - incoming * number(key, 'inTangent'))} ${point(key)}`;
    });
    let handles = '';
    if (selected >= 0) {
      const position = ordered.findIndex(item => item.index === selected), key = keys[selected];
      for (const [side, neighbor] of [['in', ordered[position - 1]], ['out', ordered[position + 1]]]) {
        if (!neighbor || !Number.isFinite(number(key, `${side}Tangent`))) continue;
        const delta = (number(neighbor.key, 'time') - number(key, 'time')) * weight(key, side);
        const time = number(key, 'time') + delta, value = number(key, 'value') + delta * number(key, `${side}Tangent`);
        handles += `<path class="curve-tangent" d="M${point(key)} L${x(time)},${y(value)}"/><circle class="curve-handle" data-tangent="${side}" cx="${x(time)}" cy="${y(value)}" r="5"/>`;
      }
    }
    svg.innerHTML = `<path class="curve-axis" d="M40,30 V225 H690"/><text x="40" y="248">${html(view.left.toPrecision(4))} s</text><text x="610" y="248">${html((view.left + view.width).toPrecision(4))} s</text><text x="42" y="20">${html((view.bottom + view.height).toPrecision(4))}</text><path class="curve-line" d="${paths.join(' ')}"/>${handles}${keys.map((key, index) => `<circle class="curve-key ${index === selected ? 'selected' : ''}" data-curve-key="${index}" cx="${x(number(key, 'time'))}" cy="${y(number(key, 'value'))}" r="5"><title>第 ${index + 1} 点 · ${html(key.members.time.value)} / ${html(key.members.value.value)}</title></circle>`).join('')}`;
    container.querySelector('.curve-status').textContent = `${keys.length} 个关键点。拖动关键点修改时间与值；拖动切线端点修改原切线，权重及循环方式保持独立字段。`;
  }

  function fields() {
    container.querySelector('[data-curve-copy]').disabled = container.querySelector('[data-curve-delete]').disabled = selected < 0;
    controls.innerHTML = selected < 0 ? `<p>当前曲线没有关键点。${initialKeys.length ? '从上方选择原定义中的关键点添加后，可填写时间、值、切线和权重。' : '此字段尚缺新关键点的正式初始形状。'}修改只保留在本表的曲线副本中。</p>`
      : `<label>选择关键点<select data-curve-index>${keys.map((key, index) => `<option value="${index}" ${selected === index ? 'selected' : ''}>${index + 1} · ${html(key.members.time.value)} s</option>`).join('')}</select></label><div class="curve-key-fields">${Object.entries(keys[selected].members).map(([field, value]) => `<label>${html(field)}${scalarControl(value, `data-curve-field="${html(field)}"`, memberDefinition(keyDefinition, field))}<small>${html(value.type)}</small></label>`).join('')}</div>`;
    container.querySelector('.curve-wrap').innerHTML = Object.entries(curve.members).filter(([field]) => field !== 'keys')
      .map(([field, value]) => `<label>${html(field)}${scalarControl(value, `data-curve-wrap="${html(field)}"`, memberDefinition(definition, field))}</label>`).join('');
  }

  container.addEventListener('input', event => {
    if (event.target.hasAttribute('data-curve-field')) {
      keys[selected].members[event.target.dataset.curveField].value = event.target.value;
      plot();
    } else if (event.target.hasAttribute('data-curve-wrap'))
      curve.members[event.target.dataset.curveWrap].value = event.target.value;
  });
  container.addEventListener('change', event => {
    if (event.target.hasAttribute('data-curve-index')) { selected = Number(event.target.value); fields(); plot(); }
  });
  container.addEventListener('click', event => {
    if (event.target.closest('[data-curve-fit]')) { fit(); plot(); }
    else if (event.target.closest('[data-curve-create]')) {
      keys.push(structuredClone(initialKeys[Number(container.querySelector('[data-curve-initial-key]').value)]));
      selected = keys.length - 1;
      fit(); fields(); plot();
    } else if (event.target.closest('[data-curve-copy]')) {
      keys.splice(selected + 1, 0, structuredClone(keys[selected])); selected++; fields(); plot();
    } else if (event.target.closest('[data-curve-delete]')) {
      keys.splice(selected, 1); selected = Math.min(selected, keys.length - 1); fields(); plot();
    }
  });
  svg.addEventListener('pointerdown', event => {
    if (drag || event.button !== 0) return;
    const key = event.target.closest('[data-curve-key]'), tangent = event.target.closest('[data-tangent]');
    if (!key && !tangent) return;
    if (key) selected = Number(key.dataset.curveKey);
    drag = { index: selected, pointerId: event.pointerId, original: structuredClone(keys[selected]),
      side: tangent?.dataset.tangent };
    event.preventDefault();
    svg.setPointerCapture(event.pointerId); fields(); plot();
  });
  svg.addEventListener('pointermove', event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    const coordinates = new DOMPoint(event.clientX, event.clientY).matrixTransform(svg.getScreenCTM().inverse());
    const time = view.left + (coordinates.x - 40) / 650 * view.width;
    const value = view.bottom + (225 - coordinates.y) / 195 * view.height;
    const key = keys[drag.index];
    if (drag.side) {
      const delta = time - number(key, 'time');
      if (delta === 0) return;
      key.members[`${drag.side}Tangent`].value = Math.fround((value - number(key, 'value')) / delta).toPrecision(9);
    } else {
      key.members.time.value = Math.fround(time).toPrecision(9);
      key.members.value.value = Math.fround(value).toPrecision(9);
    }
    fields(); plot();
  });
  svg.addEventListener('pointerup', event => {
    if (drag?.pointerId === event.pointerId) drag = null;
  });
  const cancel = event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    keys[drag.index] = drag.original;
    drag = null;
    fields(); plot();
  };
  svg.addEventListener('pointercancel', cancel);
  svg.addEventListener('lostpointercapture', cancel);
  fit(); fields(); plot();
}
