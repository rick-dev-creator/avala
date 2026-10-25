import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';

const stage = document.getElementById('stage');
const facts = document.getElementById('facts');
const notice = document.getElementById('notice');
const asked = new URLSearchParams(location.search).get('model') ?? '';
const name = asked.split('/').pop();
const dark = matchMedia('(prefers-color-scheme: dark)');

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
stage.appendChild(renderer.domElement);

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 1000);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;

scene.add(new THREE.HemisphereLight(0xffffff, 0x444450, 2.2));
const key = new THREE.DirectionalLight(0xffffff, 2.4);
key.position.set(3, 5, 4);
scene.add(key);

const holder = new THREE.Group();
scene.add(holder);
let grid = null;
let floor = { span: 1, at: new THREE.Vector3() };

const clock = new THREE.Clock();
let mixer = null;
let frame = 0;
let fitted = { center: new THREE.Vector3(), distance: 2 };

function lay() {
  const visible = document.getElementById('grid').getAttribute('aria-pressed') === 'true';
  if (grid) {
    scene.remove(grid);
    grid.geometry.dispose();
    grid.material.dispose();
  }
  const line = dark.matches ? 0x48484a : 0xc7c7cc;
  grid = new THREE.GridHelper(floor.span, 20, line, line);
  grid.position.copy(floor.at);
  grid.visible = visible;
  scene.add(grid);
}

function paint() {
  const page = getComputedStyle(document.documentElement).getPropertyValue('--page').trim();
  scene.background = new THREE.Color(page || (dark.matches ? '#1c1c1e' : '#f4f3ef'));
  lay();
  ask();
}

function ask() {
  if (frame === 0) {
    frame = requestAnimationFrame(draw);
  }
}

function draw() {
  frame = 0;
  const moving = controls.update();
  if (mixer) {
    mixer.update(clock.getDelta());
  }
  renderer.render(scene, camera);
  if (moving || mixer) {
    ask();
  }
}

function resize() {
  const width = stage.clientWidth || 1;
  const height = stage.clientHeight || 1;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  ask();
}

function material() {
  return new THREE.MeshStandardMaterial({ color: 0xb8bcc8, metalness: 0.1, roughness: 0.6 });
}

function load(url, extension) {
  return new Promise((resolve, reject) => {
    if (extension === 'glb' || extension === 'gltf') {
      new GLTFLoader().load(url, gltf => resolve({ object: gltf.scene, clips: gltf.animations }), undefined, reject);
    } else if (extension === 'stl') {
      new STLLoader().load(url, geometry => {
        geometry.computeVertexNormals();
        const mesh = new THREE.Mesh(geometry, material());
        mesh.rotation.x = -Math.PI / 2;
        resolve({ object: mesh, clips: [] });
      }, undefined, reject);
    } else if (extension === 'obj') {
      new OBJLoader().load(url, object => {
        object.traverse(child => {
          if (child.isMesh && !(child.material && child.material.map)) {
            child.material = material();
          }
        });
        resolve({ object, clips: [] });
      }, undefined, reject);
    } else {
      reject(new Error(`Avala cannot show .${extension} models.`));
    }
  });
}

function measure(object) {
  let triangles = 0;
  object.traverse(child => {
    if (child.isMesh && child.geometry) {
      const index = child.geometry.index;
      const position = child.geometry.attributes.position;
      triangles += Math.floor((index ? index.count : position ? position.count : 0) / 3);
    }
  });
  return triangles;
}

function frameModel(object) {
  const box = new THREE.Box3().setFromObject(object);
  const size = box.getSize(new THREE.Vector3());
  const center = box.getCenter(new THREE.Vector3());
  const radius = Math.max(size.length() / 2, 0.001);
  const distance = 1.2 * radius / Math.sin(THREE.MathUtils.degToRad(camera.fov / 2));
  fitted = { center, distance };
  camera.near = distance / 100;
  camera.far = distance * 100;
  camera.updateProjectionMatrix();
  floor = { span: Math.max(size.x, size.z) * 2 || 1, at: new THREE.Vector3(center.x, box.min.y, center.z) };
  paint();
  fit();
  return size;
}

function fit() {
  const direction = new THREE.Vector3(1, 0.7, 1).normalize();
  camera.position.copy(fitted.center).addScaledVector(direction, fitted.distance);
  controls.target.copy(fitted.center);
  controls.update();
  ask();
}

function toggle(button, apply) {
  const on = button.getAttribute('aria-pressed') !== 'true';
  button.setAttribute('aria-pressed', String(on));
  apply(on);
  ask();
}

function release() {
  cancelAnimationFrame(frame);
  holder.traverse(child => {
    child.geometry?.dispose();
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const used of materials) {
      if (!used) {
        continue;
      }
      for (const value of Object.values(used)) {
        if (value && value.isTexture) {
          value.dispose();
        }
      }
      used.dispose();
    }
  });
  grid?.geometry.dispose();
  grid?.material.dispose();
  controls.dispose();
  renderer.dispose();
  renderer.forceContextLoss();
}

function fail(message) {
  document.body.classList.add('failed');
  notice.textContent = message;
}

document.getElementById('fit').addEventListener('click', fit);
document.getElementById('wireframe').addEventListener('click', event => toggle(event.currentTarget, on => {
  holder.traverse(child => {
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const used of materials) {
      if (used && 'wireframe' in used) {
        used.wireframe = on;
      }
    }
  });
}));
document.getElementById('grid').addEventListener('click', event => toggle(event.currentTarget, on => {
  grid.visible = on;
}));
controls.addEventListener('change', ask);
dark.addEventListener('change', paint);
new ResizeObserver(resize).observe(stage);
addEventListener('pagehide', release);

resize();
paint();

if (asked.length === 0) {
  fail('No model was named.');
} else {
  const extension = asked.split('.').pop().toLowerCase();
  const url = '/' + asked.split('/').map(encodeURIComponent).join('/');
  load(url, extension).then(({ object, clips }) => {
    holder.add(object);
    if (clips.length > 0) {
      mixer = new THREE.AnimationMixer(object);
      for (const clip of clips) {
        mixer.clipAction(clip).play();
      }
      clock.start();
    }
    const size = frameModel(object);
    const triangles = measure(object).toLocaleString();
    facts.textContent = `${name} · ${triangles} triangles · ${size.x.toFixed(2)} × ${size.y.toFixed(2)} × ${size.z.toFixed(2)}`;
  }).catch(problem => fail(`${name} could not be shown: ${String(problem && problem.message ? problem.message : problem).slice(0, 300)}`));
}
