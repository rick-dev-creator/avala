import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { CSS3DRenderer, CSS3DObject } from 'three/addons/renderers/CSS3DRenderer.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

const MODELS = {
  'iphone-se': { name: 'iPhone SE', body: [67.3, 138.4, 7.3], corner: 9.6, fit: [59, 105], front: 'classic', back: 'single', rim: 0x2b2b2f, glass: 0x0b0b0d, rear: 0x1c1c1f },
  'iphone-16': { name: 'iPhone 16', body: [71.6, 147.6, 7.8], corner: 11.6, fit: [67.6, 143.6], front: 'island', back: 'pill', rim: 0x3a3b40, glass: 0x050506, rear: 0x26272b },
  'iphone-16-pro-max': { name: 'iPhone 16 Pro Max', body: [77.6, 163.0, 8.25], corner: 12.6, fit: [74.8, 160.2], front: 'island', back: 'trio', rim: 0x9b958c, glass: 0x050506, rear: 0x8d8880 },
  'pixel-9': { name: 'Pixel 9', body: [72.0, 152.8, 8.5], corner: 10.2, fit: [67.2, 148.0], front: 'punch', back: 'visor', rim: 0x2e3034, glass: 0x050506, rear: 0x34363b },
  'ipad-air-11': { name: 'iPad Air 11"', body: [178.5, 247.6, 6.1], corner: 15.5, fit: [160.0, 229.6], front: 'tablet', back: 'single', rim: 0x6c6e74, glass: 0x070708, rear: 0x6c6e74 },
  'surface-duo': { name: 'Surface Duo', body: [93.3, 145.2, 4.8], corner: 6.5, fit: [86.0, 114.6], front: 'duo', back: 'single', rim: 0xd8d9dc, glass: 0x0a0a0c, rear: 0xe4e5e8, hinge: 34 },
};

const query = new URLSearchParams(location.search);
const model = MODELS[query.get('device')];
const page = query.get('page') ?? '';
const wide = Number(query.get('w'));
const tall = Number(query.get('h'));
const turned = query.get('turned') === '1';
const stage = document.getElementById('scene');
const facts = document.getElementById('facts');
const notice = document.getElementById('notice');

const css = new CSS3DRenderer();
css.domElement.style.touchAction = 'none';
stage.appendChild(css.domElement);
const gl = new THREE.WebGLRenderer({ antialias: true, alpha: true });
gl.setPixelRatio(Math.min(devicePixelRatio, 2));
gl.setClearColor(0x000000, 0);
gl.outputColorSpace = THREE.SRGBColorSpace;
gl.toneMapping = THREE.ACESFilmicToneMapping;
stage.appendChild(gl.domElement);

const scene = new THREE.Scene();
const pmrem = new THREE.PMREMGenerator(gl);
const room = new RoomEnvironment();
scene.environment = pmrem.fromScene(room, 0.04).texture;
const key = new THREE.DirectionalLight(0xffffff, 1.6);
key.position.set(120, 260, 320);
scene.add(key);

const camera = new THREE.PerspectiveCamera(30, 1, 1, 10000);
const controls = new OrbitControls(camera, css.domElement);
controls.enableDamping = true;
controls.autoRotateSpeed = 1.2;

const device = new THREE.Group();
scene.add(device);
let frame = 0;
let radius = 100;

function ask() {
  if (frame === 0) {
    frame = requestAnimationFrame(draw);
  }
}

function draw() {
  frame = 0;
  const moving = controls.update();
  gl.render(scene, camera);
  css.render(scene, camera);
  if (moving || controls.autoRotate) {
    ask();
  }
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
  const object = new CSS3DObject(sheet);
  object.scale.setScalar(scale);
  object.position.z = z;
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

function duo() {
  const [half, height, depth] = model.body;
  const spanWide = turned ? tall : wide;
  const spanTall = turned ? wide : tall;
  const each = (spanWide - model.hinge) / 2;
  const screen = screenOf(each, spanTall);
  const gap = model.hinge * screen.scale;
  const inner = Math.max((gap - 2.4) / 2, 0.6);
  const offset = 1.2 + half / 2;
  const z = depth / 2 + 0.04;
  for (const side of [-1, 1]) {
    const body = slab(half, height, depth, model.corner);
    body.position.x = side * offset;
    const window = hole(screen.width, screen.height, 1.2, z);
    window.position.x = side * (1.2 + inner + screen.width / 2);
    device.add(body, window);
  }
  const hinge = new THREE.Mesh(new THREE.CylinderGeometry(depth / 2, depth / 2, height - 6, 32), metal(0xbfc1c5, 0.35));
  device.add(hinge);
  for (const side of [-1, 1]) {
    const parts = back(half, height, depth);
    for (const part of side < 0 ? parts.slice(0, 1) : parts) {
      part.position.x += side * offset;
      device.add(part);
    }
  }
  const sheet = page3d(wide, tall, screen.scale, 1.2, z);
  if (turned) {
    device.rotation.z = Math.PI / 2;
    sheet.rotation.z = -Math.PI / 2;
  }
  device.add(sheet);
}

function frontView() {
  const sphere = new THREE.Box3().setFromObject(device).getBoundingSphere(new THREE.Sphere());
  radius = sphere.radius;
  const distance = 1.12 * radius / Math.sin(THREE.MathUtils.degToRad(camera.fov / 2));
  camera.near = distance / 50;
  camera.far = distance * 20;
  camera.updateProjectionMatrix();
  camera.position.set(sphere.center.x, sphere.center.y, sphere.center.z + distance);
  controls.target.copy(sphere.center);
  controls.minDistance = radius * 0.6;
  controls.maxDistance = radius * 8;
  controls.update();
  ask();
}

function fit() {
  const direction = camera.position.clone().sub(controls.target).normalize();
  const distance = 1.12 * radius / Math.sin(THREE.MathUtils.degToRad(camera.fov / 2));
  camera.position.copy(controls.target).addScaledVector(direction, distance);
  controls.update();
  ask();
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

document.getElementById('front').addEventListener('click', frontView);
document.getElementById('fit').addEventListener('click', fit);
document.getElementById('spin').addEventListener('click', event => {
  controls.autoRotate = !controls.autoRotate;
  event.currentTarget.setAttribute('aria-pressed', String(controls.autoRotate));
  ask();
});
controls.addEventListener('change', ask);
new ResizeObserver(resize).observe(stage);
addEventListener('pagehide', release);
resize();

if (!model || !(wide > 0) || !(tall > 0) || page.length === 0) {
  fail('This device or page could not be shown.');
} else {
  if (model.front === 'duo') {
    duo();
  } else {
    phone();
  }
  facts.textContent = `${model.name} · ${wide} × ${tall}`;
  frontView();
}
