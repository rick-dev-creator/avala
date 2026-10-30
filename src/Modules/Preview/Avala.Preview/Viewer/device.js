import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { CSS3DRenderer, CSS3DObject } from 'three/addons/renderers/CSS3DRenderer.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const MODELS = {
  'iphone-se': { name: 'iPhone SE', body: [67.3, 138.4, 7.3], corner: 9.6, fit: [59, 105], front: 'classic', back: 'single', rim: 0x2b2b2f, glass: 0x0b0b0d, rear: 0x1c1c1f },
  'iphone-16': { name: 'iPhone 16', body: [71.6, 147.6, 7.8], corner: 11.6, fit: [67.6, 143.6], front: 'island', back: 'pill', rim: 0x3a3b40, glass: 0x050506, rear: 0x26272b },
  'iphone-16-pro-max': { name: 'iPhone 16 Pro Max', body: [77.6, 163.0, 8.25], corner: 12.6, fit: [74.8, 160.2], front: 'island', back: 'trio', rim: 0x9b958c, glass: 0x050506, rear: 0x8d8880 },
  'pixel-9': { name: 'Pixel 9', body: [72.0, 152.8, 8.5], corner: 10.2, fit: [67.2, 148.0], front: 'punch', back: 'visor', rim: 0x2e3034, glass: 0x050506, rear: 0x34363b },
  'ipad-air-11': { name: 'iPad Air 11"', body: [178.5, 247.6, 6.1], corner: 15.5, fit: [160.0, 229.6], front: 'tablet', back: 'single', rim: 0x6c6e74, glass: 0x070708, rear: 0x6c6e74 },
};
const DUO = { name: 'iPhone Duo', asset: './models/iphone-duo.glb', credit: { text: 'Model by PikkmeStudios · CC BY 4.0', link: 'https://sketchfab.com/3d-models/d2ca6bff92c141d1b1c5759b19d533e0' }, inner: 'screen-foldable', outer: 'screen', hinge: 'middle-block', half: 'right-side', sizes: { open: [772, 548], closed: [380, 548] }, insets: { open: 0.012, closed: 0.024 }, corners: { open: 0.045, closed: 0.07 } };
MODELS['iphone-duo'] = { ...DUO, folded: false };
MODELS['iphone-duo-closed'] = { ...DUO, folded: true };

const query = new URLSearchParams(location.search);
const model = MODELS[query.get('device')];
const page = query.get('page') ?? '';
const wide = Number(query.get('w'));
const tall = Number(query.get('h'));
const turned = query.get('turned') === '1';
const stage = document.getElementById('scene');
const facts = document.getElementById('facts');
const notice = document.getElementById('notice');
const hint = document.getElementById('hint');
const crisp = Math.min(devicePixelRatio, 2);
const FACING = Math.cos(THREE.MathUtils.degToRad(12));

const css = new CSS3DRenderer();
stage.appendChild(css.domElement);
const gl = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
gl.setPixelRatio(crisp);
gl.setClearColor(0x000000, 0);
gl.outputColorSpace = THREE.SRGBColorSpace;
gl.toneMapping = THREE.ACESFilmicToneMapping;
stage.appendChild(gl.domElement);
const viewpoint = css.domElement.firstChild.firstChild;
const shield = document.createElement('div');
shield.id = 'shield';
stage.appendChild(shield);

const scene = new THREE.Scene();
const pmrem = new THREE.PMREMGenerator(gl);
const room = new RoomEnvironment();
scene.environment = pmrem.fromScene(room, 0.04).texture;
const key = new THREE.DirectionalLight(0xffffff, 1.6);
key.position.set(120, 260, 320);
scene.add(key);

const camera = new THREE.PerspectiveCamera(30, 1, 1, 10000);
const controls = new OrbitControls(camera, shield);
controls.enableDamping = true;
controls.dampingFactor = 0.09;
controls.rotateSpeed = 0.85;
controls.zoomSpeed = 0.9;
controls.panSpeed = 0.9;
controls.zoomToCursor = true;
controls.screenSpacePanning = true;
controls.autoRotateSpeed = 1.6;
controls.mouseButtons = { LEFT: THREE.MOUSE.ROTATE, MIDDLE: THREE.MOUSE.PAN, RIGHT: THREE.MOUSE.PAN };

const device = new THREE.Group();
scene.add(device);
const screens = [];
const sheets = [];
const pages = [];
let flattened = null;
let frame = 0;
let radius = 100;
let center = new THREE.Vector3();
let dragging = false;
let live = false;
let tween = null;
let faceDir = new THREE.Vector3(0, 0, 1);
let folding = null;
let foldable = null;

function ask() {
  if (frame === 0) {
    frame = requestAnimationFrame(draw);
  }
}

function draw() {
  frame = 0;
  unflatten();
  const travelling = step() | bend();
  const damping = controls.update();
  follow();
  gl.render(scene, camera);
  css.render(scene, camera);
  if (travelling || damping || dragging || controls.autoRotate) {
    if (live) {
      use(false);
    }
    ask();
  } else {
    settle();
  }
}

function sharpness(ratio) {
  if (gl.getPixelRatio() !== ratio) {
    gl.setPixelRatio(ratio);
    gl.setSize(stage.clientWidth || 1, stage.clientHeight || 1, false);
  }
}

function looking() {
  return camera.position.clone().sub(controls.target).normalize().dot(faceDir);
}

function facing() {
  return looking() > FACING;
}

function settle() {
  sharpness(crisp);
  if (facing()) {
    if (looking() < 0.9999) {
      travel(controls.target.clone().addScaledVector(faceDir, camera.position.distanceTo(controls.target)), controls.target.clone(), 260);
      return;
    }
    use(true);
  } else {
    use(false);
  }
  follow();
  css.render(scene, camera);
  carve();
}

function use(on) {
  live = on;
  for (const sheet of sheets) {
    sheet.style.pointerEvents = on ? 'auto' : 'none';
  }
  hint.textContent = on ? 'The screen is live · drag the frame to turn the device' : 'Double-click to face the screen and use it';
  document.body.classList.toggle('live', on);
  carve();
}

function corners(mesh) {
  const { width, height } = mesh.userData;
  return [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([x, y]) => {
    const point = new THREE.Vector3(x * width / 2, y * height / 2, 0).applyMatrix4(mesh.matrixWorld).project(camera);
    return [(point.x + 1) / 2 * stage.clientWidth, (1 - point.y) / 2 * stage.clientHeight];
  });
}

function unflatten() {
  if (!flattened) {
    return;
  }
  for (const [element, transform, origin] of flattened.pages) {
    element.style.transform = transform;
    element.style.transformOrigin = origin;
  }
  viewpoint.style.transform = flattened.camera;
  viewpoint.style.transformStyle = 'preserve-3d';
  flattened = null;
}

function flatten() {
  unflatten();
  flattened = { camera: viewpoint.style.transform, pages: [] };
  scene.updateMatrixWorld();
  for (const object of pages.filter(shownPage)) {
    const element = object.element;
    const width = parseFloat(element.style.width);
    const height = parseFloat(element.style.height);
    const at = (u, v) => {
      const point = new THREE.Vector3(u - width / 2, height / 2 - v, 0).applyMatrix4(object.matrixWorld).project(camera);
      return [(point.x + 1) / 2 * stage.clientWidth, (1 - point.y) / 2 * stage.clientHeight];
    };
    const [x, y] = at(0, 0);
    const [ux, uy] = at(width, 0);
    const [vx, vy] = at(0, height);
    flattened.pages.push([element, element.style.transform, element.style.transformOrigin]);
    element.style.transformOrigin = '0 0';
    element.style.transform = `matrix(${(ux - x) / width}, ${(uy - y) / width}, ${(vx - x) / height}, ${(vy - y) / height}, ${x}, ${y})`;
  }
  viewpoint.style.transform = 'none';
  viewpoint.style.transformStyle = 'flat';
}

function shownPage(object) {
  let visible = true;
  object.traverseAncestors(parent => {
    visible = visible && parent.visible;
  });
  return visible && object.visible;
}

function carve() {
  if (!live) {
    unflatten();
    shield.style.clipPath = 'none';
    return;
  }
  flatten();
  const outer = `M0 0 H${stage.clientWidth} V${stage.clientHeight} H0 Z`;
  const holes = screens.map(mesh => 'M' + corners(mesh).map(([x, y]) => `${x.toFixed(1)} ${y.toFixed(1)}`).join(' L') + ' Z').join(' ');
  shield.style.clipPath = `path(evenodd, '${outer} ${holes}')`;
}

function framing() {
  const vertical = THREE.MathUtils.degToRad(camera.fov);
  const horizontal = 2 * Math.atan(Math.tan(vertical / 2) * camera.aspect);
  return 1.08 * radius / Math.sin(Math.min(vertical, horizontal) / 2);
}

function home() {
  return center.clone().addScaledVector(faceDir, framing());
}

function travel(position, target, ms = 520) {
  const from = camera.position.clone().sub(controls.target);
  const to = position.clone().sub(target);
  tween = {
    from, to,
    fromTarget: controls.target.clone(),
    toTarget: target.clone(),
    turn: new THREE.Quaternion().setFromUnitVectors(from.clone().normalize(), to.clone().normalize()),
    start: performance.now(),
    ms,
  };
  controls.enabled = false;
  use(false);
  ask();
}

function step() {
  if (!tween) {
    return false;
  }
  const t = Math.min((performance.now() - tween.start) / tween.ms, 1);
  const k = 1 - Math.pow(1 - t, 3);
  const turn = new THREE.Quaternion().slerp(tween.turn, k);
  const length = THREE.MathUtils.lerp(tween.from.length(), tween.to.length(), k);
  controls.target.lerpVectors(tween.fromTarget, tween.toTarget, k);
  camera.position.copy(controls.target).add(tween.from.clone().normalize().applyQuaternion(turn).multiplyScalar(length));
  camera.lookAt(controls.target);
  if (t >= 1) {
    tween = null;
    controls.enabled = true;
    controls.update();
    return false;
  }
  return true;
}

function resize() {
  const width = stage.clientWidth || 1;
  const height = stage.clientHeight || 1;
  gl.setSize(width, height, false);
  css.setSize(width, height);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  ask();
}

function rounded(width, height, corner) {
  const x = width / 2;
  const y = height / 2;
  const r = Math.max(0.01, Math.min(corner, x, y));
  const shape = new THREE.Shape();
  shape.moveTo(-x + r, -y);
  shape.lineTo(x - r, -y);
  shape.quadraticCurveTo(x, -y, x, -y + r);
  shape.lineTo(x, y - r);
  shape.quadraticCurveTo(x, y, x - r, y);
  shape.lineTo(-x + r, y);
  shape.quadraticCurveTo(-x, y, -x, y - r);
  shape.lineTo(-x, -y + r);
  shape.quadraticCurveTo(-x, -y, -x + r, -y);
  return shape;
}

function metal(color, roughness = 0.32) {
  return new THREE.MeshPhysicalMaterial({ color, metalness: 1, roughness, envMapIntensity: 1.1 });
}

function glass(color, roughness = 0.08, shine = 1, coat = 1) {
  return new THREE.MeshPhysicalMaterial({ color, metalness: 0, roughness, clearcoat: coat, clearcoatRoughness: 0.05, envMapIntensity: shine, specularIntensity: shine });
}

function slab(width, height, depth, corner) {
  const bevel = Math.min(depth * 0.34, corner * 0.5);
  const shape = rounded(width - 2 * bevel, height - 2 * bevel, Math.max(corner - bevel, 0.5));
  const geometry = new THREE.ExtrudeGeometry(shape, { depth: depth - 2 * bevel, bevelEnabled: true, bevelThickness: bevel, bevelSize: bevel, bevelSegments: 8, curveSegments: 32 });
  geometry.translate(0, 0, -(depth - 2 * bevel) / 2);
  return new THREE.Mesh(geometry, [glass(model.glass, 0.35, 0.12, 0), metal(model.rim)]);
}

function flat(width, height, corner, material, z) {
  const mesh = new THREE.Mesh(new THREE.ShapeGeometry(rounded(width, height, corner), 32), material);
  mesh.position.z = z;
  return mesh;
}

function hole(width, height, corner, z) {
  const material = new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0, blending: THREE.NoBlending });
  const mesh = flat(width, height, corner, material, z);
  mesh.renderOrder = 2;
  mesh.userData = { width, height };
  screens.push(mesh);
  return mesh;
}

function disc(diameter, depth, material) {
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(diameter / 2, diameter / 2, depth, 40), material);
  mesh.rotation.x = Math.PI / 2;
  return mesh;
}

function lens(x, y, z, diameter) {
  const ring = disc(diameter, 1.2, metal(model.rim, 0.25));
  ring.position.set(x, y, z - 0.6);
  const eye = disc(diameter * 0.72, 1.3, glass(0x0b0d16, 0.02));
  eye.position.set(x, y, z - 0.65);
  return [ring, eye];
}

function front(width, height, depth, screen) {
  const z = depth / 2 + 0.06;
  const black = new THREE.MeshBasicMaterial({ color: 0x000000 });
  const parts = [];
  if (model.front === 'island') {
    const island = flat(19.5, 5.6, 2.8, black, z + 0.02);
    island.position.y = screen.height / 2 - 4.6;
    parts.push(island);
  } else if (model.front === 'punch') {
    const punch = new THREE.Mesh(new THREE.CircleGeometry(1.7, 40), black);
    punch.position.set(0, screen.height / 2 - 4.2, z + 0.02);
    parts.push(punch);
  } else if (model.front === 'classic') {
    const ring = new THREE.Mesh(new THREE.RingGeometry(4.6, 5.2, 48), metal(0x5a5a5f, 0.3));
    ring.position.set(0, -height / 2 + 10.5, z);
    const slot = flat(9.5, 1.3, 0.65, new THREE.MeshStandardMaterial({ color: 0x3a3a3e, roughness: 0.7 }), z);
    slot.position.y = height / 2 - 10;
    const camera = new THREE.Mesh(new THREE.CircleGeometry(1, 32), black);
    camera.position.set(-9, height / 2 - 10, z);
    parts.push(ring, slot, camera);
  } else if (model.front === 'tablet') {
    const camera = new THREE.Mesh(new THREE.CircleGeometry(0.9, 32), new THREE.MeshStandardMaterial({ color: 0x1e2230, roughness: 0.3 }));
    camera.position.set(0, height / 2 - 4.2, z);
    parts.push(camera);
  }
  return parts;
}

function back(width, height, depth) {
  const z = -depth / 2 - 0.05;
  const parts = [flat(width - 1.6, height - 1.6, model.corner - 0.8, glass(model.rear, 0.45), z)];
  parts[0].rotation.y = Math.PI;
  const top = height / 2;
  const left = width / 2;
  if (model.back === 'pill') {
    const bump = flat(12.5, 26, 6.2, glass(model.rear, 0.2), z - 0.9);
    bump.rotation.y = Math.PI;
    bump.position.set(left - 13, top - 19, z - 0.9);
    parts.push(bump, ...lens(left - 13, top - 13.2, z - 1, 8.2), ...lens(left - 13, top - 24.8, z - 1, 8.2));
  } else if (model.back === 'trio') {
    const bump = flat(37, 39, 9, glass(model.rear, 0.25), z - 1);
    bump.rotation.y = Math.PI;
    bump.position.set(left - 23, top - 24, z - 1);
    parts.push(bump, ...lens(left - 14, top - 14, z - 1.2, 13), ...lens(left - 32, top - 24, z - 1.2, 13), ...lens(left - 14, top - 34, z - 1.2, 13));
  } else if (model.back === 'visor') {
    const visor = flat(width - 2, 12.5, 6, metal(model.rim, 0.3), z - 1.4);
    visor.rotation.y = Math.PI;
    visor.position.set(0, top - 26, z - 1.4);
    parts.push(visor, ...lens(left - 18, top - 26, z - 1.6, 7.5), ...lens(left - 28, top - 26, z - 1.6, 7.5));
  } else {
    parts.push(...lens(left - 11, top - 11, z, 7));
  }
  return parts;
}

function screenOf(spanWide, spanTall) {
  const scale = Math.min(model.fit[0] / spanWide, model.fit[1] / spanTall);
  return { scale, width: spanWide * scale, height: spanTall * scale };
}

function page3d(width, height, scale, corner, z) {
  const sheet = document.createElement('div');
  sheet.className = 'screen';
  sheet.style.width = `${width}px`;
  sheet.style.height = `${height}px`;
  sheet.style.borderRadius = `${corner / scale}px`;
  const view = document.createElement('iframe');
  view.src = page;
  view.title = model.name;
  view.allow = 'fullscreen';
  sheet.appendChild(view);
  sheets.push(sheet);
  const object = new CSS3DObject(sheet);
  object.scale.setScalar(scale);
  object.position.z = z;
  pages.push(object);
  return object;
}

function phone() {
  const [width, height, depth] = model.body;
  const portraitWide = turned ? tall : wide;
  const portraitTall = turned ? wide : tall;
  const screen = screenOf(portraitWide, portraitTall);
  const corner = model.front === 'classic' ? 0 : Math.max(model.corner - (width - screen.width) / 2, 1);
  const z = depth / 2 + 0.04;
  device.add(slab(width, height, depth, model.corner), hole(screen.width, screen.height, corner, z), ...front(width, height, depth, screen), ...back(width, height, depth));
  const sheet = page3d(wide, tall, screen.scale, corner, z);
  if (turned) {
    device.rotation.z = Math.PI / 2;
    sheet.rotation.z = -Math.PI / 2;
  }
  device.add(sheet);
}

function rectOf(mesh, deviceCenter, inset) {
  const box = new THREE.Box3().setFromObject(mesh, true);
  const size = box.getSize(new THREE.Vector3());
  const middle = box.getCenter(new THREE.Vector3());
  const thin = ['x', 'y', 'z'].reduce((least, axis) => (size[axis] < size[least] ? axis : least), 'x');
  const normal = new THREE.Vector3();
  normal[thin] = Math.sign(middle[thin] - deviceCenter[thin]) || 1;
  const up = new THREE.Vector3(0, 1, 0);
  const right = up.clone().cross(normal).normalize();
  const width = Math.abs(size.dot(right)) * (1 - 2 * inset);
  const height = Math.abs(size.dot(up)) * (1 - 2 * inset);
  const surface = middle.clone().addScaledVector(normal, size[thin] / 2 + 0.05);
  const basis = new THREE.Matrix4().makeBasis(right, up, normal);
  return { surface, quaternion: new THREE.Quaternion().setFromRotationMatrix(basis), width, height, normal };
}

function mount(parent, rect, size, cornerRatio) {
  const [cssWide, cssTall] = size;
  const scale = Math.min(rect.width / cssWide, rect.height / cssTall);
  const corner = Math.min(rect.width, rect.height) * cornerRatio;
  const window = new THREE.Mesh(new THREE.ShapeGeometry(rounded(cssWide * scale, cssTall * scale, corner), 32), new THREE.MeshBasicMaterial({ color: 0x000000, transparent: true, opacity: 0, blending: THREE.NoBlending }));
  window.renderOrder = 2;
  window.userData = { width: cssWide * scale, height: cssTall * scale, scale, corner, size };
  const local = parent.worldToLocal(rect.surface.clone());
  const parentTurn = parent.getWorldQuaternion(new THREE.Quaternion()).invert();
  window.position.copy(local);
  window.quaternion.copy(parentTurn.multiply(rect.quaternion));
  parent.add(window);
  return window;
}

async function fold() {
  const gltf = await new GLTFLoader().loadAsync(model.asset);
  const root = gltf.scene;
  const box = new THREE.Box3().setFromObject(root, true);
  const unit = 180 / Math.max(...box.getSize(new THREE.Vector3()).toArray());
  root.scale.setScalar(unit);
  root.position.copy(box.getCenter(new THREE.Vector3()).multiplyScalar(-unit));
  device.add(root);
  device.updateMatrixWorld(true);
  const middle = new THREE.Box3().setFromObject(root, true).getCenter(new THREE.Vector3());
  const inner = root.getObjectByName(model.inner);
  const outer = root.getObjectByName(model.outer);
  const half = root.getObjectByName(model.half);
  const innerRect = rectOf(inner, middle, model.insets.open);
  faceDir = innerRect.normal.clone();
  const hingeBox = new THREE.Box3().setFromObject(root.getObjectByName(model.hinge), true);
  const halfBox = new THREE.Box3().setFromObject(half, true);
  const halfThickness = Math.abs(halfBox.getSize(new THREE.Vector3()).dot(faceDir));
  const axis = new THREE.Vector3(0, 1, 0);
  const pivot = new THREE.Object3D();
  pivot.position.copy(hingeBox.getCenter(new THREE.Vector3()).addScaledVector(faceDir, halfThickness / 2));
  device.add(pivot);
  device.updateMatrixWorld(true);
  pivot.attach(half);
  const spine = new THREE.Object3D();
  spine.position.copy(pivot.position);
  device.add(spine);
  device.updateMatrixWorld(true);
  spine.attach(root.getObjectByName(model.hinge));
  const reach = halfBox.getCenter(new THREE.Vector3()).sub(pivot.position);
  const sign = Math.sign(axis.clone().cross(reach).dot(faceDir)) || 1;
  const outerRect = rectOf(outer, middle, model.insets.closed);
  const innerWindow = mount(device, innerRect, model.sizes.open, model.corners.open);
  const outerWindow = mount(pivot, { ...outerRect, quaternion: outerRect.quaternion }, model.sizes.closed, model.corners.closed);
  const sheet = page3d(...model.sizes.open, innerWindow.userData.scale, innerWindow.userData.corner, 0);
  sheet.matrixAutoUpdate = false;
  scene.add(sheet);
  foldable = { pivot, spine, axis, sign, inner, innerWindow, outerWindow, sheet, closed: false, angle: 0 };
  shape(false);
  if (model.folded) {
    turn(Math.PI);
    shape(true);
  }
  if (turned) {
    device.rotation.z = Math.PI / 2;
  }
  document.getElementById('fold').hidden = false;
}

function shape(closed) {
  const { innerWindow, outerWindow, sheet, inner } = foldable;
  const window = closed ? outerWindow : innerWindow;
  const other = closed ? innerWindow : outerWindow;
  const { size, scale, corner } = window.userData;
  const [across, along] = turned ? [size[1], size[0]] : size;
  sheet.element.style.width = `${across}px`;
  sheet.element.style.height = `${along}px`;
  sheet.element.style.borderRadius = `${corner / scale}px`;
  window.visible = true;
  other.visible = false;
  inner.visible = !closed;
  screens.length = 0;
  screens.push(window);
  foldable.closed = closed;
  sheet.visible = true;
  facts.textContent = `${model.name}${closed ? ' · closed' : ''} · ${across} × ${along}`;
  document.getElementById('fold').textContent = closed ? 'Unfold' : 'Fold';
}

function turn(angle) {
  foldable.angle = angle;
  foldable.pivot.setRotationFromAxisAngle(foldable.axis, foldable.sign * angle);
  foldable.spine.setRotationFromAxisAngle(foldable.axis, foldable.sign * angle / 2);
}

function follow() {
  if (!foldable) {
    return;
  }
  scene.updateMatrixWorld();
  const window = screens[0];
  const { scale } = window.userData;
  foldable.sheet.matrix.copy(window.matrixWorld).multiply(new THREE.Matrix4().makeRotationZ(turned ? Math.PI / 2 : 0)).multiply(new THREE.Matrix4().makeScale(scale, scale, scale));
}

function toggleFold() {
  if (!foldable || folding) {
    return;
  }
  const closing = !foldable.closed;
  folding = { from: foldable.angle, to: closing ? Math.PI : 0, start: performance.now(), ms: 1100, closing };
  foldable.sheet.visible = false;
  foldable.inner.visible = true;
  foldable.innerWindow.visible = false;
  foldable.outerWindow.visible = false;
  use(false);
  ask();
}

function bend() {
  if (!folding) {
    return false;
  }
  const t = Math.min((performance.now() - folding.start) / folding.ms, 1);
  const k = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
  turn(THREE.MathUtils.lerp(folding.from, folding.to, k));
  foldable.inner.visible = foldable.angle < 0.02;
  if (t >= 1) {
    shape(folding.closing);
    folding = null;
    const sphere = shown(device).getBoundingSphere(new THREE.Sphere());
    radius = sphere.radius;
    center = sphere.center.clone();
    travel(home(), center, 420);
    return false;
  }
  return true;
}

function shown(object) {
  const box = new THREE.Box3();
  object.updateMatrixWorld(true);
  object.traverseVisible(child => {
    if (child.isMesh && child.renderOrder !== 2) {
      box.expandByObject(child, true);
    }
  });
  return box;
}

function frontView() {
  const sphere = shown(device).getBoundingSphere(new THREE.Sphere());
  radius = sphere.radius;
  center = sphere.center.clone();
  const distance = framing();
  camera.near = distance / 50;
  camera.far = distance * 20;
  camera.updateProjectionMatrix();
  camera.position.copy(home());
  controls.target.copy(center);
  controls.minDistance = radius * 0.5;
  controls.maxDistance = radius * 8;
  controls.update();
  ask();
}

function face() {
  controls.autoRotate = false;
  document.getElementById('spin').setAttribute('aria-pressed', 'false');
  travel(home(), center);
}

function zoom(factor) {
  const offset = camera.position.clone().sub(controls.target);
  const length = THREE.MathUtils.clamp(offset.length() * factor, controls.minDistance, controls.maxDistance);
  travel(controls.target.clone().add(offset.setLength(length)), controls.target.clone(), 220);
}

function fit() {
  const direction = camera.position.clone().sub(controls.target).normalize();
  travel(center.clone().addScaledVector(direction, framing()), center);
}

function release() {
  cancelAnimationFrame(frame);
  device.traverse(child => {
    child.geometry?.dispose();
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const used of materials) {
      used?.dispose();
    }
  });
  scene.environment?.dispose();
  pmrem.dispose();
  controls.dispose();
  gl.dispose();
  gl.forceContextLoss();
}

function fail(message) {
  document.body.classList.add('failed');
  notice.textContent = message;
}

document.getElementById('front').addEventListener('click', face);
document.getElementById('fit').addEventListener('click', fit);
document.getElementById('spin').addEventListener('click', event => {
  controls.autoRotate = !controls.autoRotate;
  event.currentTarget.setAttribute('aria-pressed', String(controls.autoRotate));
  ask();
});
controls.addEventListener('change', ask);
controls.addEventListener('start', () => {
  dragging = true;
  tween = null;
  controls.enabled = true;
  sharpness(1);
  use(false);
  ask();
});
controls.addEventListener('end', () => {
  dragging = false;
  ask();
});
shield.addEventListener('dblclick', face);
document.getElementById('fold').addEventListener('click', toggleFold);
document.getElementById('closer').addEventListener('click', () => zoom(0.8));
document.getElementById('farther').addEventListener('click', () => zoom(1.25));
addEventListener('keydown', event => {
  if (event.key === 'f' || event.key === 'F') {
    face();
  }
  if (event.key === 'd' || event.key === 'D') {
    toggleFold();
  }
});
new ResizeObserver(resize).observe(stage);
addEventListener('pagehide', release);
resize();

if (!model || !(wide > 0) || !(tall > 0) || page.length === 0) {
  fail('This device or page could not be shown.');
} else {
  facts.textContent = `${model.name} · ${wide} × ${tall}`;
  if (model.credit) {
    const credit = document.getElementById('credit');
    credit.textContent = model.credit.text;
    credit.href = model.credit.link;
    credit.hidden = false;
  }
  if (model.asset) {
    hint.textContent = 'Loading the device…';
    fold().then(frontView).catch(problem => fail(`The device could not be loaded: ${problem && problem.message ? problem.message : problem}`));
  } else {
    phone();
    frontView();
  }
}
