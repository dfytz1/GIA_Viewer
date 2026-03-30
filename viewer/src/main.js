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
mountUi({ modelId, modelBase, viewer });
