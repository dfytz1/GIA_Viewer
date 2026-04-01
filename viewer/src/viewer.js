import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { RoomEnvironment } from "three/examples/jsm/environments/RoomEnvironment.js";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { DRACOLoader } from "three/examples/jsm/loaders/DRACOLoader.js";
import { EffectComposer } from "three/examples/jsm/postprocessing/EffectComposer.js";
import { OutputPass } from "three/examples/jsm/postprocessing/OutputPass.js";
import { RenderPass } from "three/examples/jsm/postprocessing/RenderPass.js";
import { SSAOPass } from "three/examples/jsm/postprocessing/SSAOPass.js";
import { mergeIdenticalMeshesToInstanced } from "./instancing.js";

const DRACO_DECODER =
  "https://www.gstatic.com/draco/versioned/decoders/1.5.6/";

/** Defaults and URL keys for lighting / tone / SSAO tuning (see ui panel + query string). */
export const LOOK_DEFAULTS = Object.freeze({
  toneMappingExposure: 1.05,
  environmentIntensity: 0.85,
  sunIntensity: 1.35,
  fillIntensity: 0.55,
  ssaoKernelRadius: 12,
  ssaoMinDistance: 0.001,
  ssaoMaxDistance: 0.12,
});

function clamp(n, lo, hi) {
  return Math.min(hi, Math.max(lo, n));
}

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
    this.renderer.toneMappingExposure = LOOK_DEFAULTS.toneMappingExposure;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.localClippingEnabled = true;
    container.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this._cornerScratch = new THREE.Vector3();

    this.camera = new THREE.PerspectiveCamera(45, w / h, 0.02, 1e7);
    this.camera.position.set(12, 8, 12);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.06;
    this.controls.screenSpacePanning = true;
    this.controls.minDistance = 0.1;
    this.controls.maxDistance = 1e6;
    this.controls.enableDblClickZoom = false;
    this.controls.addEventListener("change", () => this._syncCameraFrustum());

    this.raycaster = new THREE.Raycaster();
    this._ndc = new THREE.Vector2();
    this._selectedMesh = null;
    this._touchStart = null;
    this._lastTap = null;
    this._lastTapClearT = 0;
    this._bindPickHandlers();

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    const env = new RoomEnvironment(this.renderer);
    const envRT = pmrem.fromScene(env, 0.04);
    this.scene.environment = envRT.texture;
    this.scene.environmentIntensity = LOOK_DEFAULTS.environmentIntensity;
    pmrem.dispose();
    env.dispose();

    this.sun = new THREE.DirectionalLight(
      0xffffff,
      LOOK_DEFAULTS.sunIntensity,
    );
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

    this.fill = new THREE.HemisphereLight(
      0xffffff,
      0xb8c0d0,
      LOOK_DEFAULTS.fillIntensity,
    );
    this.scene.add(this.fill);

    this.grid = new THREE.GridHelper(200, 40, 0x8899aa, 0xb8c4d0);
    this.grid.position.y = 0;
    this.grid.material.opacity = 0.35;
    this.grid.material.transparent = true;
    this.scene.add(this.grid);
    this.setBackgroundColor("#e8eaf0");

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
    this.ssaoPass.kernelRadius = LOOK_DEFAULTS.ssaoKernelRadius;
    this.ssaoPass.minDistance = LOOK_DEFAULTS.ssaoMinDistance;
    this.ssaoPass.maxDistance = LOOK_DEFAULTS.ssaoMaxDistance;
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

  /**
   * Read optional lighting / exposure / SSAO overrides from URLSearchParams.
   * Keys: exp, env, sun, fill, ssaoK, ssaoMin, ssaoMax (all optional).
   */
  static parseLookFromUrl(searchParams) {
    const sp = searchParams;
    const out = {};
    const read = (key, lo, hi) => {
      if (!sp.has(key)) return;
      const n = parseFloat(sp.get(key));
      if (!Number.isFinite(n)) return;
      out[key] = clamp(n, lo, hi);
    };
    read("exp", 0.05, 4);
    read("env", 0, 3);
    read("sun", 0, 5);
    read("fill", 0, 3);
    read("ssaoK", 1, 64);
    read("ssaoMin", 1e-6, 0.1);
    read("ssaoMax", 0.005, 1);
    const map = {
      exp: "toneMappingExposure",
      env: "environmentIntensity",
      sun: "sunIntensity",
      fill: "fillIntensity",
      ssaoK: "ssaoKernelRadius",
      ssaoMin: "ssaoMinDistance",
      ssaoMax: "ssaoMaxDistance",
    };
    const settings = {};
    for (const [k, prop] of Object.entries(map)) {
      if (out[k] !== undefined) settings[prop] = out[k];
    }
    return settings;
  }

  /**
   * Parse camera + orbit target from URL (cx,cy,cz,tx,ty,tz). All six required.
   */
  static parseCameraViewFromUrl(searchParams) {
    const sp = searchParams;
    const keys = ["cx", "cy", "cz", "tx", "ty", "tz"];
    if (!keys.every((k) => sp.has(k))) return null;
    const n = (k) => {
      const v = parseFloat(sp.get(k));
      return Number.isFinite(v) ? v : null;
    };
    const cx = n("cx");
    const cy = n("cy");
    const cz = n("cz");
    const tx = n("tx");
    const ty = n("ty");
    const tz = n("tz");
    if (
      cx == null ||
      cy == null ||
      cz == null ||
      tx == null ||
      ty == null ||
      tz == null
    )
      return null;
    return {
      position: { x: cx, y: cy, z: cz },
      target: { x: tx, y: ty, z: tz },
    };
  }

  /** Background (bg) + camera position + orbit target for share / sync URL. */
  static sceneViewToUrlEntries(viewer) {
    const f = (n) => String(Number(Number(n).toFixed(4)));
    const p = viewer.camera.position;
    const t = viewer.controls.target;
    let bg = viewer.getBackgroundColorHex().replace(/^#/, "");
    return [
      ["bg", bg],
      ["cx", f(p.x)],
      ["cy", f(p.y)],
      ["cz", f(p.z)],
      ["tx", f(t.x)],
      ["ty", f(t.y)],
      ["tz", f(t.z)],
    ];
  }

  /** Apply saved camera view (after model load so fit-to-model does not overwrite). */
  applyCameraView(view) {
    if (!view?.position || !view.target) return;
    const { x: px, y: py, z: pz } = view.position;
    const { x: tx, y: ty, z: tz } = view.target;
    if (![px, py, pz, tx, ty, tz].every(Number.isFinite)) return;
    this.camera.position.set(px, py, pz);
    this.controls.target.set(tx, ty, tz);
    this.controls.update();
    this._syncCameraFrustum();
  }

  /** Serialize current look to URLSearchParams keys (exp, env, sun, …). */
  static lookSettingsToUrlEntries(settings) {
    const f = (n, d = 4) => String(Number(Number(n).toFixed(d)));
    const entries = [];
    if (settings.toneMappingExposure != null)
      entries.push(["exp", f(settings.toneMappingExposure, 4)]);
    if (settings.environmentIntensity != null)
      entries.push(["env", f(settings.environmentIntensity, 4)]);
    if (settings.sunIntensity != null)
      entries.push(["sun", f(settings.sunIntensity, 4)]);
    if (settings.fillIntensity != null)
      entries.push(["fill", f(settings.fillIntensity, 4)]);
    if (settings.ssaoKernelRadius != null)
      entries.push(["ssaoK", f(settings.ssaoKernelRadius, 2)]);
    if (settings.ssaoMinDistance != null)
      entries.push(["ssaoMin", f(settings.ssaoMinDistance, 5)]);
    if (settings.ssaoMaxDistance != null)
      entries.push(["ssaoMax", f(settings.ssaoMaxDistance, 4)]);
    return entries;
  }

  getLookSettings() {
    return {
      toneMappingExposure: this.renderer.toneMappingExposure,
      environmentIntensity: this.scene.environmentIntensity,
      sunIntensity: this.sun.intensity,
      fillIntensity: this.fill.intensity,
      ssaoKernelRadius: this.ssaoPass.kernelRadius,
      ssaoMinDistance: this.ssaoPass.minDistance,
      ssaoMaxDistance: this.ssaoPass.maxDistance,
    };
  }

  /**
   * Apply partial look settings (clamped). Omits null/undefined keys.
   */
  applyLookSettings(s) {
    if (!s) return;
    if (s.toneMappingExposure != null)
      this.renderer.toneMappingExposure = clamp(
        Number(s.toneMappingExposure),
        0.05,
        4,
      );
    if (s.environmentIntensity != null)
      this.scene.environmentIntensity = clamp(
        Number(s.environmentIntensity),
        0,
        3,
      );
    if (s.sunIntensity != null)
      this.sun.intensity = clamp(Number(s.sunIntensity), 0, 5);
    if (s.fillIntensity != null)
      this.fill.intensity = clamp(Number(s.fillIntensity), 0, 3);
    if (s.ssaoKernelRadius != null)
      this.ssaoPass.kernelRadius = clamp(
        Number(s.ssaoKernelRadius),
        1,
        64,
      );
    if (s.ssaoMinDistance != null)
      this.ssaoPass.minDistance = clamp(
        Number(s.ssaoMinDistance),
        1e-6,
        0.1,
      );
    if (s.ssaoMaxDistance != null) {
      const max = clamp(Number(s.ssaoMaxDistance), 0.005, 1);
      this.ssaoPass.maxDistance = max;
      if (this.ssaoPass.minDistance >= max)
        this.ssaoPass.minDistance = max * 0.05;
    }
    if (this.ssaoPass.minDistance >= this.ssaoPass.maxDistance)
      this.ssaoPass.minDistance = this.ssaoPass.maxDistance * 0.05;
  }

  resetLookSettings() {
    this.applyLookSettings({ ...LOOK_DEFAULTS });
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

  getBackgroundColorHex() {
    const c = this.scene.background;
    return c && c.isColor ? `#${c.getHexString()}` : "#e8eaf0";
  }

  /**
   * @param {string} cssHex e.g. "#e8eaf0" or "e8eaf0"
   */
  setBackgroundColor(cssHex) {
    let s = String(cssHex || "").trim();
    if (!s) s = "#e8eaf0";
    if (!s.startsWith("#")) s = `#${s}`;
    const col = new THREE.Color();
    try {
      col.setStyle(s);
    } catch {
      col.setHex(0xe8eaf0);
      s = "#e8eaf0";
    }
    this.scene.background = col;
    document.body.style.background = s;
    const lum = col.r * 0.299 + col.g * 0.587 + col.b * 0.114;
    const dark = lum < 0.35;
    if (this.grid?.material) {
      const mats = Array.isArray(this.grid.material)
        ? this.grid.material
        : [this.grid.material];
      if (dark) {
        mats[0]?.color?.setHex(0x2a3140);
        mats[1]?.color?.setHex(0x1a1e28);
      } else {
        mats[0]?.color?.setHex(0x8899aa);
        mats[1]?.color?.setHex(0xb8c4d0);
      }
    }
  }

  /** Keep near/far valid for the full model after zoom-to-detail (do not use focused mesh only). */
  _syncCameraFrustum() {
    if (this._bounds.isEmpty()) {
      this.camera.near = 0.01;
      this.camera.far = 1e7;
      this.camera.updateProjectionMatrix();
      return;
    }
    const { min, max } = this._bounds;
    const corners = [
      [min.x, min.y, min.z],
      [max.x, min.y, min.z],
      [min.x, max.y, min.z],
      [max.x, max.y, min.z],
      [min.x, min.y, max.z],
      [max.x, min.y, max.z],
      [min.x, max.y, max.z],
      [max.x, max.y, max.z],
    ];
    const cp = this.camera.position;
    const t = this.controls.target;
    let maxDist = cp.distanceTo(t);
    const v = this._cornerScratch;
    for (let i = 0; i < corners.length; i++) {
      const c = corners[i];
      v.set(c[0], c[1], c[2]);
      maxDist = Math.max(maxDist, cp.distanceTo(v));
    }
    maxDist = Math.max(maxDist, 1);
    // Fixed small near: scaling near with scene size caused clipping when zoomed in on detail then pulling back.
    this.camera.near = 0.02;
    this.camera.far = Math.max(maxDist * 2.75, 5e4, 1e7);
    this.camera.updateProjectionMatrix();
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
      this._doublePickZoom(e.clientX, e.clientY);
    });

    const tapMaxMovePx = 24;
    const tapMaxMs = 500;
    const dblTapGapMs = 360;
    const dblTapMaxSepPx = 50;

    el.addEventListener(
      "touchstart",
      (e) => {
        if (e.touches.length !== 1) {
          this._touchStart = null;
          return;
        }
        const t = e.touches[0];
        this._touchStart = {
          x: t.clientX,
          y: t.clientY,
          t: performance.now(),
        };
      },
      { passive: true },
    );

    el.addEventListener(
      "touchend",
      (e) => {
        if (e.changedTouches.length !== 1) return;
        const t = e.changedTouches[0];
        if (!this._touchStart) return;
        const start = this._touchStart;
        this._touchStart = null;

        const dx = t.clientX - start.x;
        const dy = t.clientY - start.y;
        const dist = Math.hypot(dx, dy);
        const duration = performance.now() - start.t;
        if (dist > tapMaxMovePx || duration > tapMaxMs) return;

        const cx = t.clientX;
        const cy = t.clientY;
        const now = performance.now();
        const prev = this._lastTap;
        if (
          prev &&
          now - prev.time < dblTapGapMs &&
          Math.hypot(cx - prev.x, cy - prev.y) < dblTapMaxSepPx
        ) {
          e.preventDefault();
          clearTimeout(this._lastTapClearT);
          this._lastTap = null;
          this._doublePickZoom(cx, cy);
        } else {
          this._lastTap = { time: now, x: cx, y: cy };
          clearTimeout(this._lastTapClearT);
          this._lastTapClearT = setTimeout(() => {
            this._lastTap = null;
          }, dblTapGapMs + 40);
        }
      },
      { passive: false },
    );

    el.addEventListener(
      "touchcancel",
      () => {
        this._touchStart = null;
      },
      { passive: true },
    );
  }

  /** Double-click / double-tap: select hit mesh and frame camera (same as desktop dblclick). */
  _doublePickZoom(clientX, clientY) {
    const mesh = this._pickMesh(clientX, clientY);
    if (!mesh) return;
    this._setSelectedMesh(mesh);
    this._focusOnMesh(mesh);
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
    if (this._selectedMesh?.userData?.giaSelectionEdges) {
      const line = this._selectedMesh.userData.giaSelectionEdges;
      line.parent?.remove(line);
      line.geometry?.dispose();
      line.material?.dispose();
      delete this._selectedMesh.userData.giaSelectionEdges;
    }
    if (!this._selectedMesh) return;
    const mats = this._collectMaterials(this._selectedMesh);
    mats.forEach((mat) => {
      if (!mat || !mat.userData.giaSelectionActive) return;
      if (mat.userData.giaPrevEmissive)
        mat.emissive.copy(mat.userData.giaPrevEmissive);
      else mat.emissive?.setHex(0x000000);
      if (mat.userData.giaPrevEmissiveIntensity != null)
        mat.emissiveIntensity = mat.userData.giaPrevEmissiveIntensity;
      if (mat.userData.giaPrevColor) mat.color.copy(mat.userData.giaPrevColor);
      delete mat.userData.giaPrevEmissive;
      delete mat.userData.giaPrevEmissiveIntensity;
      delete mat.userData.giaPrevColor;
      delete mat.userData.giaSelectionActive;
      delete mat.userData.giaSelectionColorMode;
    });
    this._selectedMesh = null;
  }

  _setSelectedMesh(mesh) {
    this._clearSelectionHighlight();
    if (!mesh) return;
    this._selectedMesh = mesh;
    mesh.updateWorldMatrix(true, true);

    try {
      const skipEdges =
        mesh.isInstancedMesh &&
        mesh.count > 48 &&
        mesh.geometry?.attributes?.position;
      if (!skipEdges) {
        const edges = new THREE.EdgesGeometry(mesh.geometry, 35);
        const line = new THREE.LineSegments(
          edges,
          new THREE.LineBasicMaterial({
            color: 0xff8800,
            depthTest: true,
            transparent: true,
            opacity: 0.95,
          }),
        );
        line.name = "gia-selection-outline";
        line.raycast = () => {};
        line.renderOrder = 1;
        mesh.add(line);
        mesh.userData.giaSelectionEdges = line;
      }
    } catch {
      /* non-geometry mesh etc. */
    }

    const mats = this._collectMaterials(mesh);
    mats.forEach((mat) => {
      if (!mat || mat.userData.giaSelectionActive) return;
      if (mat.emissive != null) {
        mat.userData.giaPrevEmissive = mat.emissive.clone();
        mat.userData.giaPrevEmissiveIntensity = mat.emissiveIntensity;
        mat.emissive.setHex(0xffaa44);
        mat.emissiveIntensity = Math.max(mat.emissiveIntensity ?? 1, 0.15) * 2.2;
        mat.userData.giaSelectionActive = true;
      } else if (mat.color != null) {
        mat.userData.giaPrevColor = mat.color.clone();
        mat.color.lerp(new THREE.Color(0xffaa44), 0.35);
        mat.userData.giaSelectionActive = true;
        mat.userData.giaSelectionColorMode = true;
      }
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

    this.sun.position.copy(center.clone().add(new THREE.Vector3(40, 80, 30)));
    this.sun.target.position.copy(center);

    this.controls.update();
    this._syncCameraFrustum();
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

    const dir = new THREE.Vector3(1, 0.65, 1).normalize();
    this.camera.position.copy(center.clone().add(dir.multiplyScalar(offset)));

    this.sun.position.copy(center.clone().add(new THREE.Vector3(40, 80, 30)));
    this.sun.target.position.copy(center);

    this.controls.update();
    this._syncCameraFrustum();
    this.initSectionSlidersFromBounds();
  }

  /**
   * After load: GPU instancing for repeated blocks (same geometry+material → one draw).
   * Disable with URL ?noinst=1
   */
  _maybeMergeInstancing(root) {
    const countMeshes = () => {
      let n = 0;
      root.traverse((o) => {
        if (o.isMesh && !o.isSkinnedMesh) n++;
      });
      return n;
    };

    if (typeof window === "undefined") {
      const meshCountBefore = countMeshes();
      return { mergedGroups: 0, meshCountBefore, meshCountAfter: meshCountBefore };
    }
    if (new URLSearchParams(window.location.search).has("noinst")) {
      const meshCountBefore = countMeshes();
      return { mergedGroups: 0, meshCountBefore, meshCountAfter: meshCountBefore };
    }

    const stats = mergeIdenticalMeshesToInstanced(root, { minGroupSize: 2 });
    if (stats.mergedGroups > 0) {
      console.info(
        `[GIA] GPU instancing: ${stats.meshCountBefore} mesh draws → ${stats.meshCountAfter} (${stats.mergedGroups} instanced groups; add ?noinst=1 to disable)`,
      );
    }
    return stats;
  }

  /**
   * Large instance counts: SSAO and shadow resolution hurt more than they help at city scale.
   * URL ?heavy=1 enables; or auto when instancing merged 800+ draws away (unless ?ssao=1).
   */
  _tuneForHeavyScene(stats) {
    if (typeof window === "undefined" || !stats) return;
    const sp = new URLSearchParams(window.location.search);
    const forceHeavy = sp.has("heavy");
    const forceSsao = sp.get("ssao") === "1";
    const saved = stats.meshCountBefore - stats.meshCountAfter;
    const manyMeshes = stats.meshCountBefore >= 1800;
    if (!forceHeavy && !manyMeshes && (saved < 800 || forceSsao)) return;

    if (this.useSsao && !forceSsao) {
      this.ssaoPass.kernelRadius = Math.min(this.ssaoPass.kernelRadius, 6);
      this.ssaoPass.maxDistance = Math.min(this.ssaoPass.maxDistance, 0.06);
      this.ssaoPass.minDistance = Math.max(this.ssaoPass.minDistance, 0.002);
    }
    const sm = this.sun.shadow.mapSize;
    if (sm.x > 1024) sm.set(1024, 1024);
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
          const instStats = this._maybeMergeInstancing(root);
          this._tuneForHeavyScene(instStats);
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
