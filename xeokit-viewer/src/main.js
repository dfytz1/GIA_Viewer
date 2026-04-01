import "./style.css";
import {
  Viewer,
  GLTFLoaderPlugin,
  Mesh,
  ReadableGeometry,
  buildPlaneGeometry,
  PhongMaterial,
} from "@xeokit/xeokit-sdk";

const params = new URLSearchParams(window.location.search);
const modelId = params.get("m");
const modelBase =
  import.meta.env.VITE_R2_PUBLIC_BASE_URL?.replace(/\/$/, "") || "";

const LS_BG = "gia-xeokit-bg";

function hexToRgb01(hex) {
  let s = String(hex || "").trim();
  if (!s.startsWith("#")) s = `#${s}`;
  if (s.length === 4) {
    s = `#${s[1]}${s[1]}${s[2]}${s[2]}${s[3]}${s[3]}`;
  }
  const n = parseInt(s.slice(1), 16);
  if (!Number.isFinite(n) || s.length < 7) return [0.055, 0.062, 0.078];
  return [(n >> 16) / 255, ((n >> 8) & 0xff) / 255, (n & 0xff) / 255];
}

function rgb01ToHex(rgb) {
  const r = Math.round(Math.min(255, Math.max(0, rgb[0] * 255)));
  const g = Math.round(Math.min(255, Math.max(0, rgb[1] * 255)));
  const b = Math.round(Math.min(255, Math.max(0, rgb[2] * 255)));
  return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`;
}

const canvas = document.getElementById("xeokit-canvas");
const labelEl = document.getElementById("model-label");

const bgParam = params.get("bg");
let storedBg = null;
try {
  storedBg = localStorage.getItem(LS_BG);
} catch {
  /* ignore */
}
const initialHex =
  bgParam != null && bgParam !== ""
    ? `#${String(bgParam).replace(/^#/, "")}`
    : storedBg || "#0e1018";
const initialRgb = hexToRgb01(initialHex);

const viewer = new Viewer({
  canvasElement: canvas,
  transparent: false,
  backgroundColor: initialRgb,
  backgroundColorFromAmbientLight: false,
  pbrEnabled: true,
  antialias: true,
});

const gltfLoader = new GLTFLoaderPlugin(viewer);

/** @type {import("@xeokit/xeokit-sdk").Mesh | null} */
let groundMesh = null;

function ensureGroundMesh() {
  if (groundMesh) return groundMesh;
  groundMesh = new Mesh(viewer.scene, {
    id: "gia-xeokit-ground",
    visible: false,
    pickable: false,
    geometry: new ReadableGeometry(
      viewer.scene,
      buildPlaneGeometry({
        xSize: 800,
        zSize: 800,
        xSegments: 1,
        zSegments: 1,
      }),
    ),
    material: new PhongMaterial(viewer.scene, {
      diffuse: [0.72, 0.74, 0.78],
      ambient: [0.25, 0.26, 0.28],
      specular: [0.08, 0.08, 0.08],
      shininess: 40,
      emissive: [0, 0, 0],
      opacity: 1,
      backfaces: true,
    }),
    position: [0, -0.02, 0],
  });
  return groundMesh;
}

function applyCanvasBackgroundFromHex(hex) {
  const rgb = hexToRgb01(hex);
  viewer.scene.canvas.backgroundColorFromAmbientLight = false;
  viewer.scene.canvas.backgroundColor = rgb;
  try {
    document.body.style.background = hex.startsWith("#") ? hex : `#${hex}`;
  } catch {
    /* ignore */
  }
}

/**
 * Grasshopper GlbExporter may emit `gia_detail` + `gia_hull`; hide hulls (no screen-space LOD here).
 */
function hideGiaHullLodMeshes(sceneModel) {
  const objects = sceneModel.objects;
  if (!objects) return;
  for (const id of Object.keys(objects)) {
    if (typeof id !== "string") continue;
    if (id === "gia_hull" || id.startsWith("gia_hull.")) {
      const ent = objects[id];
      if (ent) ent.visible = false;
    }
  }
}

function setLabel(text) {
  if (labelEl) labelEl.textContent = text;
}

function loadFromUrl(url) {
  setLabel(url.split("/").pop() || url);

  const existing = viewer.scene.models["gia-model"];
  if (existing) existing.destroy();

  const model = gltfLoader.load({
    id: "gia-model",
    src: url,
    edges: false,
    rotation: [-90, 0, 0],
  });

  model.on("error", (err) => {
    console.error(err);
    setLabel("Load error — see console");
  });

  model.on("loaded", () => {
    hideGiaHullLodMeshes(model);
    try {
      const aabb = viewer.scene.getAABB();
      viewer.cameraFlight.flyTo({ aabb });
    } catch (e) {
      console.warn("flyTo", e);
    }
  });
}

/* ——— UI: background, ground, open URL ——— */
const bgBtn = document.getElementById("xeo-bg-btn");
const bgColorEl = document.getElementById("xeo-bg-color");
const bgSwatch = document.getElementById("xeo-bg-swatch");
const groundEl = document.getElementById("xeo-ground");
const urlInput = document.getElementById("xeo-url");
const loadBtn = document.getElementById("xeo-load");

function syncBgUi() {
  const rgb = viewer.scene.canvas.backgroundColor;
  const hex = rgb01ToHex(rgb);
  if (bgColorEl) bgColorEl.value = hex;
  if (bgSwatch) bgSwatch.style.backgroundColor = hex;
}

syncBgUi();
if (bgBtn && bgColorEl) {
  bgBtn.addEventListener("click", () => bgColorEl.click());
  bgColorEl.addEventListener("input", () => {
    const v = bgColorEl.value;
    applyCanvasBackgroundFromHex(v);
    if (bgSwatch) bgSwatch.style.backgroundColor = v;
    try {
      localStorage.setItem(LS_BG, v);
    } catch {
      /* ignore */
    }
  });
}

if (groundEl) {
  groundEl.checked = params.get("gp") === "1";
  ensureGroundMesh().visible = groundEl.checked;
  groundEl.addEventListener("change", () => {
    ensureGroundMesh().visible = groundEl.checked;
  });
}

if (loadBtn && urlInput) {
  loadBtn.addEventListener("click", () => {
    const url = urlInput.value.trim();
    if (!url) return;
    loadFromUrl(url);
  });
  urlInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter") loadBtn.click();
  });
}

const urlParam = params.get("url");
if (urlParam) {
  loadFromUrl(urlParam);
  if (urlInput) urlInput.value = urlParam;
} else if (modelId && modelBase) {
  const objectUrl = `${modelBase}/${encodeURIComponent(modelId)}.glb`;
  loadFromUrl(objectUrl);
} else if (modelId && !modelBase) {
  setLabel("Set VITE_R2_PUBLIC_BASE_URL or use Open GLB URL / ?url=");
} else {
  setLabel("Open with ?m=<id>, ?url=, or load a GLB below");
}
