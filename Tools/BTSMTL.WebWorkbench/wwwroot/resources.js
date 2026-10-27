import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const state = { library: null, model: null, animations: [], mixer: null, action: null, clip: null, cursor: 0, playing: false, query: '' };
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x111c25);
const camera = new THREE.PerspectiveCamera(36, 1, 0.01, 200);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
$('viewport').append(renderer.domElement);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
scene.add(new THREE.HemisphereLight(0xffffff, 0x334451, 2));
scene.add(new THREE.GridHelper(12, 24, 0x426473, 0x223541));
const axes = new THREE.AxesHelper(0.6);
axes.position.set(-0.8, 0.005, -0.8);
scene.add(axes);

function resize() {
  const width = $('viewport').clientWidth, height = $('viewport').clientHeight;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}
const resizeObserver = new ResizeObserver(resize);
resizeObserver.observe($('viewport'));

function fit() {
  state.model.updateMatrixWorld(true);
  state.model.traverse(object => { if (object.isSkinnedMesh) object.computeBoundingBox(); });
  const bounds = new THREE.Box3().setFromObject(state.model);
  const center = bounds.getCenter(new THREE.Vector3());
  const size = bounds.getSize(new THREE.Vector3());
  const distance = Math.max(size.x, size.y, size.z) / (2 * Math.tan(THREE.MathUtils.degToRad(camera.fov * 0.5)));
  camera.position.copy(center).add(new THREE.Vector3(distance * 0.55, distance * 0.15, distance * 1.45));
  controls.target.copy(center);
  controls.update();
}

function renderList() {
  $('animation-list').innerHTML = state.library.clips.filter(clip => `${clip.name} ${clip.category}`.toLowerCase().includes(state.query)).map(clip => `<button data-animation="${clip.animationIndex}" class="${state.clip === clip ? 'selected' : ''}">${escapeHtml(clip.name)}<small>${escapeHtml(clip.category)} · ${clip.duration.toFixed(3)} s · ${clip.frameRate} fps</small></button>`).join('');
}

function showClip(index) {
  const clip = state.library.clips.find(item => item.animationIndex === index);
  const animation = state.animations[index];
  if (animation.name !== clip.name) throw new Error('glb 动画索引与同版资源清单不匹配。');
  state.mixer.stopAllAction();
  state.clip = clip;
  state.cursor = 0;
  state.playing = false;
  state.action = state.mixer.clipAction(animation);
  state.action.reset().setLoop(THREE.LoopOnce, 1).play();
  state.action.clampWhenFinished = true;
  state.mixer.update(0);
  $('play-resource').textContent = '播放';
  $('clip-name').textContent = clip.name;
  $('clip-provenance').textContent = clip.provenance.description;
  $('resource-time').max = clip.exportedDuration;
  $('resource-details').innerHTML = [
    ['片段', clip.name], ['资源身份', clip.resource.identity], ['Unity 来源', clip.unityAsset],
    ['GUID', clip.resource.guid], ['Local file ID', clip.resource.localFileId],
    ['来源资产 SHA-256', clip.resource.contentRevision],
    ['素材时长', clip.duration + ' s'], ['glb 时长', clip.exportedDuration + ' s'],
    ['源帧率', clip.frameRate + ' fps'], ['来源 Blend SHA-256', state.library.sourceSha256],
    ['glb SHA-256', state.library.sha256]
  ].map(([key, value]) => `<dt>${escapeHtml(key)}</dt><dd>${escapeHtml(value)}</dd>`).join('');
  renderList();
  fit();
}

function setTime() {
  state.action.enabled = true;
  state.action.paused = false;
  state.action.time = state.cursor;
  state.mixer.update(0);
  $('resource-time').value = state.cursor;
  $('resource-time-output').textContent = `${state.cursor.toFixed(3)} s`;
}

let previous = performance.now();
function frame(now) {
  const elapsed = (now - previous) / 1000;
  previous = now;
  if (state.playing) {
    state.cursor = Math.min(state.clip.exportedDuration, state.cursor + elapsed);
    if (state.cursor === state.clip.exportedDuration) { state.playing = false; $('play-resource').textContent = '重播'; }
  }
  if (state.action) setTime();
  controls.update();
  renderer.render(scene, camera);
  if (state.clip) {
    Object.assign(document.documentElement.dataset, {
      resourceImageReady: 'true', resourceIdentity: state.clip.resource.identity,
      resourceRevision: state.clip.resource.contentRevision, resourceLibrary: state.library.sha256,
      resourceSource: state.library.sourceSha256, resourceTime: String(state.cursor)
    });
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

$('clip-search').oninput = event => { state.query = event.target.value.trim().toLowerCase(); if (state.library) renderList(); };
$('animation-list').onclick = event => { const target = event.target.closest('[data-animation]'); if (target) showClip(Number(target.dataset.animation)); };
$('resource-time').oninput = event => { state.cursor = Number(event.target.value); state.playing = false; $('play-resource').textContent = '播放'; };
$('play-resource').onclick = () => {
  if (state.cursor >= state.clip.exportedDuration) state.cursor = 0;
  state.playing = !state.playing;
  $('play-resource').textContent = state.playing ? '暂停' : '播放';
};
$('frame-resource').onclick = fit;

async function load() {
  const response = await fetch('/api/resources/animations');
  const manifest = await response.json();
  if (!response.ok) throw new Error(manifest.message);
  state.library = manifest;
  const query = new URLSearchParams(location.search);
  const resource = query.get('resource');
  const selected = resource === null ? manifest.clips[0] :
    manifest.clips.find(clip => clip.resource.identity === resource);
  if (!selected) throw new Error(`该版本资源清单不包含所选资源 ${resource}。`);
  $('source-version').textContent = `${manifest.clips.length} 段 · 来源 ${manifest.sourceSha256.slice(0, 12)}`;
  $('resource-state').textContent = '正在加载同版模型与动画…';
  const gltf = await new GLTFLoader().loadAsync(`/api/resources/animations/file/${manifest.sha256}`);
  state.model = gltf.scene;
  state.animations = gltf.animations;
  scene.add(state.model);
  state.mixer = new THREE.AnimationMixer(state.model);
  showClip(selected.animationIndex);
  if (query.has('time')) {
    const time = Number(query.get('time'));
    if (!Number.isFinite(time) || time < 0 || time > selected.exportedDuration)
      throw new Error('请求的素材秒数超出该片段的实际导出范围。');
    state.cursor = time;
    setTime();
    fit();
  }
  $('play-resource').disabled = false;
  $('resource-time').disabled = false;
  $('frame-resource').disabled = false;
  $('resource-state').textContent = '素材片段预览 · 来源与 glb 版本已匹配';
}

load().catch(error => {
  $('resource-state').textContent = error.message;
  $('resource-state').classList.add('error');
});
window.addEventListener('pagehide', () => {
  resizeObserver.disconnect();
  controls.dispose();
  const textures = new Set();
  scene.traverse(object => {
    if (object.geometry) object.geometry.dispose();
    const materials = Array.isArray(object.material) ? object.material : object.material ? [object.material] : [];
    materials.forEach(material => {
      Object.values(material).forEach(value => { if (value?.isTexture) textures.add(value); });
      material.dispose();
    });
  });
  textures.forEach(texture => { if (texture.source.data instanceof ImageBitmap) texture.source.data.close(); texture.dispose(); });
  renderer.dispose();
});
