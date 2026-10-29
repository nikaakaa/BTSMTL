import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import type { AnimationClip } from '../../api/types';

// 动画资源面板的独立 three 场景（沿用 resources.js）：同版 glb 模型与动画、按素材秒数摆姿势、适配角色。

export class ResourceScene {
  private renderer: THREE.WebGLRenderer;
  private scene = new THREE.Scene();
  private camera = new THREE.PerspectiveCamera(36, 1, 0.01, 200);
  private controls: OrbitControls;
  private resize: ResizeObserver;
  private model: THREE.Object3D | null = null;
  private animations: THREE.AnimationClip[] = [];
  private mixer: THREE.AnimationMixer | null = null;
  private action: THREE.AnimationAction | null = null;
  private scheduled = 0;
  onRendered: (() => void) | null = null;

  constructor(private host: HTMLElement, private view: Window) {
    const style = view.getComputedStyle(host);
    const color = (name: string) => new THREE.Color(style.getPropertyValue(`--resource-${name}`).trim());
    this.scene.background = color('bg');
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(view.devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    host.append(this.renderer.domElement);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.addEventListener('change', () => this.requestRender());
    this.scene.add(new THREE.HemisphereLight(color('sky'), color('ground'), 2));
    this.scene.add(new THREE.GridHelper(12, 24, color('grid'), color('grid-minor')));
    const axes = new THREE.AxesHelper(0.6);
    axes.position.set(-0.8, 0.005, -0.8);
    this.scene.add(axes);
    const Observer = (view as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    this.resize = new Observer(() => {
      const width = host.clientWidth, height = host.clientHeight;
      if (!width || !height) return;
      this.renderer.setSize(width, height, false);
      this.camera.aspect = width / height;
      this.camera.updateProjectionMatrix();
      this.requestRender();
    });
    this.resize.observe(host);
  }

  async load(data: ArrayBuffer) {
    const gltf = await new GLTFLoader().parseAsync(data, '');
    this.model = gltf.scene;
    this.animations = gltf.animations;
    this.scene.add(this.model);
    this.mixer = new THREE.AnimationMixer(this.model);
  }

  // 片段按清单的动画序号取用；名称不一致说明 glb 与清单不是同版。
  showClip(clip: AnimationClip) {
    const animation = this.animations[clip.animationIndex];
    if (!this.mixer || !animation) throw new Error('glb 中没有清单所列的动画序号');
    if (animation.name !== clip.name) throw new Error('glb 动画索引与同版资源清单不匹配');
    this.mixer.stopAllAction();
    this.action = this.mixer.clipAction(animation);
    this.action.reset().setLoop(THREE.LoopOnce, 1).play();
    this.action.clampWhenFinished = true;
    this.mixer.update(0);
  }

  setTime(seconds: number) {
    if (!this.action || !this.mixer) return;
    this.action.enabled = true;
    this.action.paused = false;
    this.action.time = seconds;
    this.mixer.update(0);
    this.requestRender();
  }

  fit() {
    if (!this.model) return;
    this.model.updateMatrixWorld(true);
    this.model.traverse(object => { if ((object as THREE.SkinnedMesh).isSkinnedMesh) (object as THREE.SkinnedMesh).computeBoundingBox(); });
    const bounds = new THREE.Box3().setFromObject(this.model);
    const center = bounds.getCenter(new THREE.Vector3());
    const size = bounds.getSize(new THREE.Vector3());
    const distance = Math.max(size.x, size.y, size.z) / (2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov * 0.5)));
    this.camera.position.copy(center).add(new THREE.Vector3(distance * 0.55, distance * 0.15, distance * 1.45));
    this.controls.target.copy(center);
    this.controls.update();
    this.requestRender();
  }

  requestRender() {
    if (this.scheduled) return;
    this.scheduled = this.view.requestAnimationFrame(() => {
      this.scheduled = 0;
      this.renderer.render(this.scene, this.camera);
      this.onRendered?.();
    });
  }

  dispose() {
    if (this.scheduled) this.view.cancelAnimationFrame(this.scheduled);
    this.resize.disconnect();
    this.controls.dispose();
    this.mixer?.stopAllAction();
    const textures = new Set<THREE.Texture>();
    this.scene.traverse(object => {
      const mesh = object as THREE.Mesh;
      mesh.geometry?.dispose();
      const materials = Array.isArray(mesh.material) ? mesh.material : mesh.material ? [mesh.material] : [];
      for (const material of materials) {
        for (const value of Object.values(material)) if ((value as THREE.Texture | null)?.isTexture) textures.add(value as THREE.Texture);
        material.dispose();
      }
    });
    textures.forEach(texture => {
      const data = texture.source.data as unknown;
      if (typeof ImageBitmap !== 'undefined' && data instanceof ImageBitmap) data.close();
      texture.dispose();
    });
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}
