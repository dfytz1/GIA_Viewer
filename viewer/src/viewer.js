import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { RoomEnvironment } from "three/examples/jsm/environments/RoomEnvironment.js";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { DRACOLoader } from "three/examples/jsm/loaders/DRACOLoader.js";
import { EffectComposer } from "three/examples/jsm/postprocessing/EffectComposer.js";
import { OutputPass } from "three/examples/jsm/postprocessing/OutputPass.js";
import { RenderPass } from "three/examples/jsm/postprocessing/RenderPass.js";
import { SSAOPass } from "three/examples/jsm/postprocessing/SSAOPass.js";

const DRACO_DECODER =
  "https://www.gstatic.com/draco/versioned/decoders/1.5.6/";

export class GiaViewer {
  constructor(container) {
    this.container = container;
    const w = container.clientWidth || window.innerWidth;
    const h = container.clientHeight || window.innerHeight;

    this.renderer = new THREE.WebGLRenderer({
      antialias: true,
      alpha: false,
      powerPreference: "high-performance",
    });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.setSize(w, h);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.localClippingEnabled = true;
    container.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0a0b0d);

    this.camera = new THREE.PerspectiveCamera(45, w / h, 0.01, 1e7);
    this.camera.position.set(12, 8, 12);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.06;
    this.controls.screenSpacePanning = true;
    this.controls.minDistance = 0.1;
    this.controls.maxDistance = 1e6;
    this.controls.enableDblClickZoom = false;

    this.raycaster = new THREE.Raycaster();
    this._ndc = new THREE.Vector2();
    this._selectedMesh = null;
    this._bindPickHandlers();

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    const env = new RoomEnvironment(this.renderer);
    const envRT = pmrem.fromScene(env, 0.04);
    this.scene.environment = envRT.texture;
    this.scene.environmentIntensity = 0.85;
    pmrem.dispose();
    env.dispose();

    this.sun = new THREE.DirectionalLight(0xffffff, 1.35);
    this.sun.position.set(18, 28, 12);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(2048, 2048);
    this.sun.shadow.camera.near = 0.5;
    this.sun.shadow.camera.far = 200;
    this.sun.shadow.camera.left = -60;
    this.sun.shadow.camera.right = 60;
    this.sun.shadow.camera.top = 60;
    this.sun.shadow.camera.bottom = -60;
    this.scene.add(this.sun);
    this.scene.add(this.sun.target);

    const fill = new THREE.HemisphereLight(0x8fa3c4, 0x1a1c22, 0.45);
    this.scene.add(fill);

    this.grid = new THREE.GridHelper(200, 40, 0x2a3140, 0x1a1e28);
    this.grid.position.y = 0;
    this.grid.material.opacity = 0.35;
    this.grid.material.transparent = true;
    this.scene.add(this.grid);

    this.modelRoot = new THREE.Group();
    this.modelRoot.name = "ModelRoot";
    this.scene.add(this.modelRoot);

    this.clippingPlanes = {
      x: null,
      y: null,
      z: null,
    };

    this._clipEnabled = { x: false, y: false, z: false };
    this._clipPos = { x: 0, y: 0, z: 0 };
    this._bounds = new THREE.Box3();

    this.composer = new EffectComposer(this.renderer);
    this.renderPass = new RenderPass(this.scene, this.camera);
    this.composer.addPass(this.renderPass);

    this.ssaoPass = new SSAOPass(this.scene, this.camera, w, h);
    this.ssaoPass.kernelRadius = 12;
    this.ssaoPass.minDistance = 0.001;
    this.ssaoPass.maxDistance = 0.12;
    this.ssaoPass.output = SSAOPass.OUTPUT.Default;
    this.composer.addPass(this.ssaoPass);

    this.outputPass = new OutputPass();
    this.composer.addPass(this.outputPass);

    this.useSsao = true;

    this._gltfLoader = this._makeLoader();

    window.addEventListener("resize", () => this._onResize());
    this._animate = this._animate.bind(this);
    requestAnimationFrame(this._animate);
  }

  _makeLoader() {
    const draco = new DRACOLoader();
    draco.setDecoderPath(DRACO_DECODER);
    const loader = new GLTFLoader();
    loader.setDRACOLoader(draco);
    return loader;
  }

  setSsao(enabled) {
    this.useSsao = !!enabled;
    this.ssaoPass.enabled = this.useSsao;
  }

  setGridVisible(v) {
    this.grid.visible = v;
  }

  _onResize() {
    const w = this.container.clientWidth || window.innerWidth;
    const h = this.container.clientHeight || window.innerHeight;
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(w, h);
    this.composer.setSize(w, h);
    this.ssaoPass.setSize(w, h);
  }

  _animate() {
    requestAnimationFrame(this._animate);
    this.controls.update();
    if (this.useSsao) {
      this.composer.render();
    } else {
      this.renderer.render(this.scene, this.camera);
    }
  }

  _applyClippingToModel() {
    const planes = [];
    if (this._clipEnabled.x && this.clippingPlanes.x)
      planes.push(this.clippingPlanes.x);
    if (this._clipEnabled.y && this.clippingPlanes.y)
      planes.push(this.clippingPlanes.y);
    if (this._clipEnabled.z && this.clippingPlanes.z)
      planes.push(this.clippingPlanes.z);

    this.modelRoot.traverse((obj) => {
      if (!obj.isMesh) return;
      const mats = Array.isArray(obj.material)
        ? obj.material
        : [obj.material];
      mats.forEach((m) => {
        if (!m) return;
        m.clippingPlanes = planes;
        m.clipIntersection = false;
        m.needsUpdate = true;
      });
    });
  }

  setSectionAxis(axis, enabled, position) {
    const a = axis.toLowerCase();
    if (a === "x" || a === "y" || a === "z") {
      this._clipEnabled[a] = enabled;
      this._clipPos[a] = position;
      this._rebuildClipPlanes();
    }
  }

  _rebuildClipPlanes() {
    const { min, max } = this._bounds;
    const cx = (min.x + max.x) / 2;
    const cy = (min.y + max.y) / 2;
    const cz = (min.z + max.z) / 2;

    const px = THREE.MathUtils.clamp(this._clipPos.x, min.x, max.x);
    const py = THREE.MathUtils.clamp(this._clipPos.y, min.y, max.y);
    const pz = THREE.MathUtils.clamp(this._clipPos.z, min.z, max.z);

    this.clippingPlanes.x = new THREE.Plane(new THREE.Vector3(-1, 0, 0), px);
    this.clippingPlanes.y = new THREE.Plane(new THREE.Vector3(0, -1, 0), py);
    this.clippingPlanes.z = new THREE.Plane(new THREE.Vector3(0, 0, -1), pz);

    this._applyClippingToModel();
  }

  initSectionSlidersFromBounds() {
    const c = new THREE.Vector3();
    this._bounds.getCenter(c);
    this._clipPos.x = c.x;
    this._clipPos.y = c.y;
    this._clipPos.z = c.z;
    this._rebuildClipPlanes();
  }

  getSectionBounds() {
    const min = this._bounds.min.clone();
    const max = this._bounds.max.clone();
    return { min, max };
  }

  _bindPickHandlers() {
    const el = this.renderer.domElement;
    el.style.cursor = "default";

    el.addEventListener("click", (e) => {
      if (e.button !== 0) return;
      if (e.detail !== 1) return;
      const mesh = this._pickMesh(e.clientX, e.clientY);
      if (mesh) this._setSelectedMesh(mesh);
      else this._clearSelectionHighlight();
    });

    el.addEventListener("dblclick", (e) => {
      if (e.button !== 0) return;
      e.preventDefault();
      const mesh = this._pickMesh(e.clientX, e.clientY);
      if (!mesh) return;
      this._setSelectedMesh(mesh);
      this._focusOnMesh(mesh);
    });
  }

  _pickMesh(clientX, clientY) {
    const rect = this.renderer.domElement.getBoundingClientRect();
    this._ndc.x = ((clientX - rect.left) / rect.width) * 2 - 1;
    this._ndc.y = -((clientY - rect.top) / rect.height) * 2 + 1;
    this.raycaster.setFromCamera(this._ndc, this.camera);
    const hits = this.raycaster.intersectObject(this.modelRoot, true);
    for (let i = 0; i < hits.length; i++) {
      const o = hits[i].object;
      if (o.isMesh && o.visible) return o;
    }
    return null;
  }

  _collectMaterials(mesh) {
    const m = mesh.material;
    if (!m) return [];
    return Array.isArray(m) ? m : [m];
  }

  _clearSelectionHighlight() {
    if (!this._selectedMesh) return;
    const mats = this._collectMaterials(this._selectedMesh);
    mats.forEach((mat) => {
      if (!mat || !mat.userData.giaSelectionActive) return;
      if (mat.userData.giaPrevEmissive)
        mat.emissive.copy(mat.userData.giaPrevEmissive);
      if (mat.userData.giaPrevEmissiveIntensity != null)
        mat.emissiveIntensity = mat.userData.giaPrevEmissiveIntensity;
      delete mat.userData.giaPrevEmissive;
      delete mat.userData.giaPrevEmissiveIntensity;
      delete mat.userData.giaSelectionActive;
    });
    this._selectedMesh = null;
  }

  _setSelectedMesh(mesh) {
    this._clearSelectionHighlight();
    if (!mesh) return;
    this._selectedMesh = mesh;
    const mats = this._collectMaterials(mesh);
    mats.forEach((mat) => {
      if (!mat || mat.emissive == null) return;
      if (mat.userData.giaSelectionActive) return;
      mat.userData.giaPrevEmissive = mat.emissive.clone();
      mat.userData.giaPrevEmissiveIntensity = mat.emissiveIntensity;
      mat.emissive.setHex(0x3355aa);
      mat.emissiveIntensity = 0.42;
      mat.userData.giaSelectionActive = true;
    });
  }

  _focusOnMesh(mesh) {
    const box = new THREE.Box3().setFromObject(mesh);
    if (box.isEmpty()) return;

    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z, 0.001);
    const dist = maxDim / (2 * Math.tan((this.camera.fov * Math.PI) / 360));
    const offset = dist * 1.28;

    this.controls.target.copy(center);

    const prev = new THREE.Vector3().subVectors(
      this.camera.position,
      this.controls.target,
    );
    let dir = prev.lengthSq() > 1e-10 ? prev.normalize() : null;
    if (!dir) dir = new THREE.Vector3(1, 0.65, 1).normalize();

    this.camera.position.copy(center.clone().add(dir.multiplyScalar(offset)));
    this.camera.near = Math.max(0.001, maxDim / 2000);
    this.camera.far = Math.max(10000, maxDim * 50);
    this.camera.updateProjectionMatrix();

    this.sun.position.copy(center.clone().add(new THREE.Vector3(40, 80, 30)));
    this.sun.target.position.copy(center);

    this.controls.update();
  }

  _fitCameraToObject(object) {
    const box = new THREE.Box3().setFromObject(object);
    if (box.isEmpty()) return;
    this._bounds.copy(box);

    const size = box.getSize(new THREE.Vector3());
    const center = box.getCenter(new THREE.Vector3());
    const maxDim = Math.max(size.x, size.y, size.z, 0.001);
    const dist = maxDim / (2 * Math.tan((this.camera.fov * Math.PI) / 360));
    const offset = dist * 1.35;

    this.controls.target.copy(center);
    this.camera.near = Math.max(0.001, maxDim / 2000);
    this.camera.far = Math.max(10000, maxDim * 50);
    this.camera.updateProjectionMatrix();

    const dir = new THREE.Vector3(1, 0.65, 1).normalize();
    this.camera.position.copy(center.clone().add(dir.multiplyScalar(offset)));

    this.sun.position.copy(center.clone().add(new THREE.Vector3(40, 80, 30)));
    this.sun.target.position.copy(center);

    this.controls.update();
    this.initSectionSlidersFromBounds();
  }

  loadFromUrl(url) {
    return new Promise((resolve, reject) => {
      this._clearSelectionHighlight();
      while (this.modelRoot.children.length)
        this.modelRoot.remove(this.modelRoot.children[0]);

      this._gltfLoader.load(
        url,
        (gltf) => {
          const root = gltf.scene || gltf.scenes[0];
          root.rotation.x = -Math.PI / 2;
          root.updateMatrixWorld(true);

          root.traverse((c) => {
            if (!c.isMesh) return;
            c.castShadow = true;
            c.receiveShadow = true;
            const mats = Array.isArray(c.material) ? c.material : [c.material];
            mats.forEach((m) => {
              if (!m) return;
              if (m.transparent) m.depthWrite = false;
            });
          });

          this.modelRoot.add(root);
          this._fitCameraToObject(this.modelRoot);
          this._applyClippingToModel();
          resolve(gltf);
        },
        undefined,
        (err) => reject(err)
      );
    });
  }
}
