import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { ConvexGeometry } from 'three/addons/geometries/ConvexGeometry.js';

export function fixed(value) {
  const raw = BigInt(value);
  return Number(raw / 65536n) + Number(raw % 65536n) / 65536;
}

const position = raw => new THREE.Vector3(fixed(raw[0]), fixed(raw[1]), -fixed(raw[2]));
function pose(object, value) {
  object.position.copy(position(value.positionRaw));
  const [x, y, z, w] = value.rotationRaw.map(fixed);
  object.quaternion.set(-x, -y, z, w);
}

function clear(group) {
  group.traverse(object => {
    object.geometry?.dispose();
    if (Array.isArray(object.material)) object.material.forEach(material => material.dispose());
    else object.material?.dispose();
  });
  group.clear();
}

function material(color, opacity) {
  return new THREE.MeshBasicMaterial({ color, transparent: opacity < 1, opacity,
    wireframe: true, depthWrite: false, side: THREE.DoubleSide });
}

function primitive(kind, radius, halfLength, extents) {
  switch (kind) {
    case 'Sphere': return new THREE.SphereGeometry(radius, 14, 10);
    case 'Capsule': return new THREE.CapsuleGeometry(radius, halfLength * 2, 6, 14);
    case 'Box': return new THREE.BoxGeometry(extents[0] * 2, extents[1] * 2, extents[2] * 2);
    default: throw new Error(`逻辑几何 ${kind} 尚无对应网页表示。`);
  }
}

export class LogicViewport {
  constructor(element) {
    this.element = element;
    this.geometry = null;
    this.scene = new THREE.Scene(); this.scene.background = new THREE.Color('#10171d');
    this.camera = new THREE.PerspectiveCamera(42, 1, 0.01, 5000); this.camera.position.set(4, 3, 5);
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    element.append(this.renderer.domElement);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.target.set(0, 0.6, 0);
    this.world = new THREE.Group(); this.actors = new THREE.Group(); this.queries = new THREE.Group();
    this.scene.add(this.world, this.actors, this.queries);
    this.grid = new THREE.GridHelper(40, 40, '#39505b', '#24323a'); this.scene.add(this.grid);
    this.resize = new ResizeObserver(() => {
      this.renderer.setSize(element.clientWidth, element.clientHeight);
      this.camera.aspect = element.clientWidth / element.clientHeight; this.camera.updateProjectionMatrix();
    });
    this.resize.observe(element);
    this.renderer.setAnimationLoop(() => this.render());
    window.addEventListener('pagehide', () => this.dispose(), { once: true });
  }

  reset() {
    clear(this.world); clear(this.actors); clear(this.queries);
    this.geometry = null; this.grid.visible = true;
  }

  setGeometry(geometry) {
    clear(this.world);
    this.geometry = geometry;
    if (geometry.coordinateSystem !== 'quantum-y-up-left-handed' || geometry.lengthUnit !== 'metre' ||
        geometry.fixedPointScale !== '65536') throw new Error('本次运行几何坐标或定点单位与视口合同不同。');
    geometry.world.forEach(collider => {
      if (['Mesh', 'Terrain'].includes(collider.kind)) return;
      const radius = fixed(collider.radiusRaw);
      const shape = primitive(collider.kind, radius, (fixed(collider.heightRaw) - radius * 2) / 2,
        collider.halfExtentsRaw.map(fixed));
      const object = new THREE.Mesh(shape, material(collider.isTrigger ? '#8164ad' : '#426b7c', 0.45));
      pose(object, collider.pose); object.userData.identity = collider.identity; this.world.add(object);
    });
    if (geometry.worldTriangles.length) {
      const vertices = geometry.worldTriangles.flatMap(triangle => [triangle.a, triangle.c, triangle.b]
        .flatMap(raw => position(raw).toArray()));
      const surface = new THREE.BufferGeometry();
      surface.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
      this.world.add(new THREE.Mesh(surface, material('#426b7c', 0.45)));
    }
    this.grid.visible = false;
  }

  shape(index, color, opacity) {
    const definition = this.geometry.shapes[index];
    if (definition.kind === 'ConvexMesh') {
      const group = new THREE.Group();
      definition.parts.forEach(part => {
        const object = this.shape(part.shapeIndex, color, opacity); pose(object, part.localPose); group.add(object);
      });
      return group;
    }
    const geometry = definition.kind === 'ConvexHull' ? new ConvexGeometry(definition.verticesRaw.map(position)) :
      primitive(definition.kind, fixed(definition.radiusRaw), fixed(definition.halfLengthRaw), definition.halfExtentsRaw.map(fixed));
    return new THREE.Mesh(geometry, material(color, opacity));
  }

  draw(frame, queries) {
    clear(this.actors); clear(this.queries);
    frame.actors.forEach(actor => {
      const root = new THREE.Group(); pose(root, actor);
      root.add(new THREE.AxesHelper(0.7));
      const receiver = this.geometry?.actors.find(item => item.rosterIndex === actor.rosterIndex);
      if (receiver) {
        const geometry = primitive('Capsule', fixed(receiver.radiusRaw), fixed(receiver.halfLengthRaw), []);
        const collider = new THREE.Mesh(geometry, material('#87cfc0', 0.8));
        collider.position.copy(position(receiver.centerOffsetRaw)); root.add(collider);
      }
      this.actors.add(root);
    });
    queries.forEach(query => {
      query.segments.forEach(segment => {
        const start = this.shape(segment.shapeIndex, '#d49651', 0.3); pose(start, segment.start);
        const end = this.shape(segment.shapeIndex, '#f4b963', 0.9); pose(end, segment.end);
        const path = new THREE.BufferGeometry().setFromPoints([position(segment.start.positionRaw), position(segment.end.positionRaw)]);
        this.queries.add(start, end, new THREE.Line(path, new THREE.LineBasicMaterial({ color: '#f4b963' })));
      });
      query.targets.forEach(target => {
        const object = this.shape(target.shapeIndex, '#719ce0', 0.55); pose(object, target.end); this.queries.add(object);
      });
    });
    (frame.hits || []).forEach(hit => {
      const point = position(hit.pointRaw);
      const marker = new THREE.Mesh(new THREE.SphereGeometry(0.045, 10, 8), new THREE.MeshBasicMaterial({ color: '#fc7980' }));
      marker.position.copy(point);
      const normal = new THREE.BufferGeometry().setFromPoints([point, point.clone().addScaledVector(position(hit.normalRaw), 0.4)]);
      this.queries.add(marker, new THREE.Line(normal, new THREE.LineBasicMaterial({ color: '#fc7980' })));
    });
    this.render();
  }

  fit() {
    if (!this.actors.children.length) return;
    const bounds = new THREE.Box3().setFromObject(this.actors);
    if (this.queries.children.length) bounds.union(new THREE.Box3().setFromObject(this.queries));
    const center = bounds.getCenter(new THREE.Vector3());
    const size = Math.max(2, bounds.getSize(new THREE.Vector3()).length());
    this.controls.target.copy(center); this.camera.position.copy(center).add(new THREE.Vector3(size, size * 0.7, size));
    this.render();
  }

  render() { this.controls.update(); this.renderer.render(this.scene, this.camera); }
  dispose() {
    this.renderer.setAnimationLoop(null); this.resize.disconnect(); this.controls.dispose();
    clear(this.world); clear(this.actors); clear(this.queries);
    this.grid.geometry.dispose(); this.grid.material.dispose(); this.renderer.dispose();
  }
}
