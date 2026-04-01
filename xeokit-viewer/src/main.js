import "./style.css";
import { Viewer, GLTFLoaderPlugin } from "@xeokit/xeokit-sdk";

const params = new URLSearchParams(window.location.search);
const modelId = params.get("m");
const modelBase =
  import.meta.env.VITE_R2_PUBLIC_BASE_URL?.replace(/\/$/, "") || "";

const canvas = document.getElementById("xeokit-canvas");
const labelEl = document.getElementById("model-label");

const viewer = new Viewer({
  canvasElement: canvas,
  transparent: false,
  backgroundColor: [0.055, 0.062, 0.078],
  pbrEnabled: true,
  antialias: true,
  // Viewer.js: enable for large scenes so Perspective.far can exceed default 10000 without clipping.
  logarithmicDepthBufferEnabled: true,
});

viewer.scene.camera.perspective.near = 0.1;
viewer.scene.camera.perspective.far = 1e7;

const gltfLoader = new GLTFLoaderPlugin(viewer);

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
    try {
      const aabb = viewer.scene.getAABB();
      viewer.cameraFlight.flyTo({ aabb });
    } catch (e) {
      console.warn("flyTo", e);
    }
  });
}

const urlParam = params.get("url");
if (urlParam) {
  loadFromUrl(urlParam);
} else if (modelId && modelBase) {
  loadFromUrl(`${modelBase}/${encodeURIComponent(modelId)}.glb`);
} else if (modelId && !modelBase) {
  setLabel("Set VITE_R2_PUBLIC_BASE_URL or use ?url=");
} else {
  setLabel("Open with ?m=<id> or ?url=<glb>");
}
