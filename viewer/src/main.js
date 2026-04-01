import "./style.css";
import { GiaViewer } from "./viewer.js";
import { mountUi } from "./ui.js";

const params = new URLSearchParams(window.location.search);
const modelId = params.get("m");
const modelBase =
  import.meta.env.VITE_R2_PUBLIC_BASE_URL?.replace(/\/$/, "") || "";

const canvasWrap = document.createElement("div");
canvasWrap.id = "canvas-wrap";
document.getElementById("app").appendChild(canvasWrap);

const viewer = new GiaViewer(canvasWrap);
if (params.get("ssao") === "0") viewer.setSsao(false);
if (params.get("ssao") === "1") viewer.setSsao(true);
const lookFromUrl = GiaViewer.parseLookFromUrl(params);
if (Object.keys(lookFromUrl).length > 0) viewer.applyLookSettings(lookFromUrl);

const bgParam = params.get("bg");
const storedBg = localStorage.getItem("gia-bg");
if (bgParam) viewer.setBackgroundColor(bgParam);
else if (storedBg) viewer.setBackgroundColor(storedBg);

const lodpxParam = params.get("lodpx");
if (lodpxParam != null && lodpxParam !== "") {
  const l = parseFloat(lodpxParam);
  if (Number.isFinite(l) && l >= 0) viewer.setLodDetailMinPx(l);
}

const cameraFromUrl = GiaViewer.parseCameraViewFromUrl(params);
if (cameraFromUrl) {
  window.addEventListener("gia-model-loaded", () => {
    viewer.applyCameraView(cameraFromUrl);
  });
}

mountUi({ modelId, modelBase, viewer });
