import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { ConvexGeometry } from 'three/addons/geometries/ConvexGeometry.js';
import type { Frame, Geometry, Pose, SweepSegment, Vec3 } from '../../api/types';

// 逻辑视图的 three 场景。
// 世界几何只在几何数据变化时建一次；按帧只改角色与查询对象的变换，查询对象从对象池取用。

const vector = (value: Vec3) => new THREE.Vector3(value[0], value[1], value[2]);

function pose(object: THREE.Object3D, value: Pose) {
  object.position.set(value.position[0], value.position[1], value.position[2]);
  object.quaternion.set(value.rotation[0], value.rotation[1], value.rotation[2], value.rotation[3]);
}

function primitive(kind: string, radius: number, halfLength: number, extents: Vec3 | []): THREE.BufferGeometry {
  switch (kind) {
    case 'Sphere': return new THREE.SphereGeometry(radius, 14, 10);
    case 'Capsule': return new THREE.CapsuleGeometry(radius, halfLength * 2, 6, 14);
    case 'Box': return new THREE.BoxGeometry(extents[0]! * 2, extents[1]! * 2, extents[2]! * 2);
    default: throw new Error(`逻辑几何 ${kind} 尚无对应网页表示。`);
  }
}

export interface CameraView {
  position: Vec3;
  target: Vec3;
}

type Role = 'start' | 'end' | 'target';

export class LogicScene {
  private renderer: THREE.WebGLRenderer;
  private scene = new THREE.Scene();
  private camera = new THREE.PerspectiveCamera(42, 1, 0.01, 5000);
  private controls: OrbitControls;
  private world = new THREE.Group();
  private actors = new THREE.Group();
  private queries = new THREE.Group();
  private grid: THREE.GridHelper;
  private resize: ResizeObserver;
  private colors: Record<string, THREE.Color> = {};
  private materials = new Map<string, THREE.Material>();
  private shapeGeometries = new Map<number, THREE.BufferGeometry>();
  private pools = new Map<string, THREE.Object3D[]>();
  private actorRoots = new Map<string, THREE.Group>();
  private sweepLines: THREE.LineSegments;
  private contactLines: THREE.LineSegments;
  private contactMarkers: THREE.Mesh[] = [];
  private contactGeometry = new THREE.SphereGeometry(0.045, 10, 8);
  private contactMaterial: THREE.Material | null = null;
  private geometry: Geometry | null = null;
  private scheduled = 0;
  onRendered: (() => void) | null = null;
  onCameraChanged: ((view: CameraView) => void) | null = null;

  constructor(private host: HTMLElement, private view: Window) {
    const style = view.getComputedStyle(host);
    for (const name of ['bg', 'grid', 'grid-minor', 'world', 'trigger', 'receiver', 'sweep-start', 'sweep-end', 'target', 'contact'])
      this.colors[name] = new THREE.Color(style.getPropertyValue(`--logic-${name}`).trim());
    this.scene.background = this.colors.bg;
    this.camera.position.set(4, 3, 5);
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(view.devicePixelRatio, 2));
    host.append(this.renderer.domElement);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.target.set(0, 0.6, 0);
    this.controls.addEventListener('change', () => this.requestRender());
    this.controls.addEventListener('end', () => this.onCameraChanged?.(this.cameraView()));
    this.grid = new THREE.GridHelper(40, 40, this.colors.grid, this.colors['grid-minor']);
    this.sweepLines = new THREE.LineSegments(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: this.colors['sweep-end'] }));
    this.contactLines = new THREE.LineSegments(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: this.colors.contact }));
    this.queries.add(this.sweepLines, this.contactLines);
    this.scene.add(this.grid, this.world, this.actors, this.queries);
    const Observer = (view as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    this.resize = new Observer(() => {
      const width = host.clientWidth, height = host.clientHeight;
      if (!width || !height) return;
      this.renderer.setSize(width, height);
      this.camera.aspect = width / height;
      this.camera.updateProjectionMatrix();
      this.requestRender();
    });
    this.resize.observe(host);
  }

  private material(color: string, opacity: number): THREE.Material {
    const key = `${color}:${opacity}`;
    let material = this.materials.get(key);
    if (!material) {
      material = new THREE.MeshBasicMaterial({
        color: this.colors[color], transparent: opacity < 1, opacity, wireframe: true, depthWrite: false, side: THREE.DoubleSide,
      });
      this.materials.set(key, material);
    }
    return material;
  }

  setGeometry(geometry: Geometry | null) {
    if (geometry === this.geometry) return;
    this.geometry = geometry;
    this.disposeGroup(this.world);
    for (const pool of this.pools.values()) pool.forEach(object => this.queries.remove(object));
    this.pools.clear();
    this.shapeGeometries.forEach(item => item.dispose());
    this.shapeGeometries.clear();
    for (const root of this.actorRoots.values()) this.disposeGroup(root);
    this.actors.clear();
    this.actorRoots.clear();
    this.grid.visible = !geometry;
    if (!geometry) return;
    for (const collider of geometry.world) {
      if (collider.kind === 'Mesh' || collider.kind === 'Terrain') continue;
      const object = new THREE.Mesh(primitive(collider.kind, collider.radius, collider.halfLength, collider.halfExtents),
        this.material(collider.isTrigger ? 'trigger' : 'world', 0.45));
      pose(object, collider.pose);
      object.userData.identity = collider.identity;
      this.world.add(object);
    }
    if (geometry.worldTriangles.length) {
      const vertices = geometry.worldTriangles.flatMap(triangle => [...triangle.a, ...triangle.b, ...triangle.c]);
      const surface = new THREE.BufferGeometry();
      surface.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
      this.world.add(new THREE.Mesh(surface, this.material('world', 0.45)));
    }
  }

  private shapeGeometry(index: number): THREE.BufferGeometry {
    let geometry = this.shapeGeometries.get(index);
    if (!geometry) {
      const definition = this.geometry!.shapes[index];
      geometry = definition.kind === 'ConvexHull' ? new ConvexGeometry(definition.vertices.map(vector))
        : primitive(definition.kind, definition.radius, definition.halfLength, definition.halfExtents);
      this.shapeGeometries.set(index, geometry);
    }
    return geometry;
  }

  private buildShape(index: number, color: string, opacity: number): THREE.Object3D {
    const definition = this.geometry!.shapes[index];
    if (definition.kind === 'ConvexMesh') {
      const group = new THREE.Group();
      for (const part of definition.parts) {
        const object = this.buildShape(part.shapeIndex, color, opacity);
        pose(object, part.localPose);
        group.add(object);
      }
      return group;
    }
    return new THREE.Mesh(this.shapeGeometry(index), this.material(color, opacity));
  }

  // 对象池：同一形状、同一用途的查询对象跨帧复用，只改变换与可见性。
  private take(role: Role, index: number, used: Map<string, number>): THREE.Object3D {
    const key = `${role}:${index}`;
    let pool = this.pools.get(key);
    if (!pool) this.pools.set(key, pool = []);
    const count = used.get(key) ?? 0;
    used.set(key, count + 1);
    if (count < pool.length) return pool[count];
    const [color, opacity] = role === 'start' ? ['sweep-start', 0.3] as const : role === 'end' ? ['sweep-end', 0.9] as const : ['target', 0.55] as const;
    const object = this.buildShape(index, color, opacity);
    pool.push(object);
    this.queries.add(object);
    return object;
  }

  private actorRoot(id: string): THREE.Group {
    let root = this.actorRoots.get(id);
    if (!root) {
      root = new THREE.Group();
      root.add(new THREE.AxesHelper(0.7));
      const receiver = this.geometry?.actors.find(item => item.actorId === id);
      if (receiver) {
        const collider = new THREE.Mesh(primitive('Capsule', receiver.radius, receiver.halfLength, []), this.material('receiver', 0.8));
        collider.position.copy(vector(receiver.centerOffset));
        root.add(collider);
      }
      this.actorRoots.set(id, root);
      this.actors.add(root);
    }
    return root;
  }

  draw(frame: Frame) {
    const present = new Set<string>();
    for (const actor of frame.actors) {
      const root = this.actorRoot(actor.id);
      pose(root, actor.root);
      root.visible = true;
      present.add(actor.id);
    }
    for (const [id, root] of this.actorRoots) if (!present.has(id)) root.visible = false;

    const used = new Map<string, number>();
    const sweepPoints: number[] = [];
    if (this.geometry) {
      const place = (role: Role, segment: SweepSegment, value: Pose) => pose(this.take(role, segment.shapeIndex, used), value);
      for (const query of frame.geometry.sweeps) {
        for (const segment of query.segments) {
          place('start', segment, segment.start);
          place('end', segment, segment.end);
          sweepPoints.push(...segment.start.position, ...segment.end.position);
        }
        for (const target of query.targets) place('target', target, target.end);
      }
    }
    for (const [key, pool] of this.pools) {
      const count = used.get(key) ?? 0;
      pool.forEach((object, index) => { object.visible = index < count; });
    }
    this.setLines(this.sweepLines, sweepPoints);

    const contactPoints: number[] = [];
    frame.geometry.contacts.forEach((hit, index) => {
      let marker = this.contactMarkers[index];
      if (!marker) {
        this.contactMaterial ??= new THREE.MeshBasicMaterial({ color: this.colors.contact });
        marker = new THREE.Mesh(this.contactGeometry, this.contactMaterial);
        this.contactMarkers.push(marker);
        this.queries.add(marker);
      }
      marker.position.set(hit.point[0], hit.point[1], hit.point[2]);
      marker.visible = true;
      contactPoints.push(...hit.point, hit.point[0] + hit.normal[0] * 0.4, hit.point[1] + hit.normal[1] * 0.4, hit.point[2] + hit.normal[2] * 0.4);
    });
    for (let index = frame.geometry.contacts.length; index < this.contactMarkers.length; index++) this.contactMarkers[index].visible = false;
    this.setLines(this.contactLines, contactPoints);
    this.requestRender();
  }

  private setLines(lines: THREE.LineSegments, points: number[]) {
    const geometry = lines.geometry;
    const attribute = geometry.getAttribute('position') as THREE.BufferAttribute | undefined;
    if (!attribute || attribute.array.length < points.length) {
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(new Float32Array(Math.max(points.length, 48)), 3));
    }
    const target = geometry.getAttribute('position') as THREE.BufferAttribute;
    (target.array as Float32Array).set(points);
    target.needsUpdate = true;
    geometry.setDrawRange(0, points.length / 3);
    geometry.computeBoundingSphere();
    lines.visible = points.length > 0;
  }

  fit() {
    const bounds = new THREE.Box3();
    for (const root of this.actorRoots.values()) if (root.visible) bounds.expandByObject(root);
    for (const object of this.queries.children) if (object.visible && object !== this.sweepLines && object !== this.contactLines) bounds.expandByObject(object);
    if (bounds.isEmpty()) return;
    const center = bounds.getCenter(new THREE.Vector3());
    const size = Math.max(2, bounds.getSize(new THREE.Vector3()).length());
    this.controls.target.copy(center);
    this.camera.position.copy(center).add(new THREE.Vector3(size, size * 0.7, size));
    this.controls.update();
    this.requestRender();
    this.onCameraChanged?.(this.cameraView());
  }

  setCamera(view: CameraView) {
    this.camera.position.set(...view.position);
    this.controls.target.set(...view.target);
    this.controls.update();
    this.requestRender();
  }

  private cameraView(): CameraView {
    return { position: this.camera.position.toArray() as Vec3, target: this.controls.target.toArray() as Vec3 };
  }

  rendered(): 'world-receivers-sweeps-contacts' | 'root-axes' {
    return this.geometry ? 'world-receivers-sweeps-contacts' : 'root-axes';
  }

  // 按需绘制：在面板所在窗口的下一帧画一次。
  requestRender() {
    if (this.scheduled) return;
    this.scheduled = this.view.requestAnimationFrame(() => {
      this.scheduled = 0;
      this.renderer.render(this.scene, this.camera);
      this.onRendered?.();
    });
  }

  private disposeGroup(group: THREE.Object3D) {
    group.traverse(object => {
      const mesh = object as THREE.Mesh;
      if (mesh.geometry && !this.isShared(mesh.geometry)) mesh.geometry.dispose();
    });
    group.clear();
  }

  private isShared(geometry: THREE.BufferGeometry) {
    return geometry === this.contactGeometry || [...this.shapeGeometries.values()].includes(geometry);
  }

  dispose() {
    if (this.scheduled) this.view.cancelAnimationFrame(this.scheduled);
    this.resize.disconnect();
    this.controls.dispose();
    this.scene.traverse(object => {
      const item = object as THREE.Mesh;
      item.geometry?.dispose();
      const material = item.material;
      if (Array.isArray(material)) material.forEach(entry => entry.dispose());
      else material?.dispose();
    });
    this.materials.forEach(material => material.dispose());
    this.shapeGeometries.forEach(geometry => geometry.dispose());
    this.contactGeometry.dispose();
    this.renderer.dispose();
    this.renderer.domElement.remove();
  }
}
