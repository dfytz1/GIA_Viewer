import { GiaViewer } from "./viewer.js";

function debounce(fn, ms) {
  let t;
  return (...args) => {
    clearTimeout(t);
    t = setTimeout(() => fn(...args), ms);
  };
}

function formatLookValue(key, n) {
  if (key === "ssaoKernelRadius") return String(Math.round(n));
  if (key.includes("ssao") && key.includes("Distance"))
    return n < 0.01 ? n.toExponential(2) : n.toFixed(4);
  return n.toFixed(3);
}

const LOOK_SLIDERS = [
  {
    key: "toneMappingExposure",
    label: "Exposure (tone map)",
    min: 0.05,
    max: 3,
    step: 0.05,
  },
  {
    key: "environmentIntensity",
    label: "IBL / environment",
    min: 0,
    max: 2,
    step: 0.05,
  },
  { key: "sunIntensity", label: "Sun (directional)", min: 0, max: 4, step: 0.05 },
  {
    key: "fillIntensity",
    label: "Sky / ground fill",
    min: 0,
    max: 2,
    step: 0.05,
  },
  {
    key: "ssaoKernelRadius",
    label: "AO kernel radius",
    min: 1,
    max: 48,
    step: 1,
  },
  {
    key: "ssaoMinDistance",
    label: "AO min depth",
    min: 0.0001,
    max: 0.025,
    step: 0.0001,
  },
  {
    key: "ssaoMaxDistance",
    label: "AO max depth",
    min: 0.02,
    max: 0.6,
    step: 0.005,
  },
];

function appendShareParams(u, viewer) {
  for (const [k, v] of GiaViewer.lookSettingsToUrlEntries(viewer.getLookSettings())) {
    u.searchParams.set(k, v);
  }
  u.searchParams.delete("lodm");
  u.searchParams.delete("lodpx");
  u.searchParams.delete("gp");
  for (const [k, v] of GiaViewer.sceneViewToUrlEntries(viewer)) {
    u.searchParams.set(k, v);
  }
  if (viewer.useSsao) u.searchParams.delete("ssao");
  else u.searchParams.set("ssao", "0");
}

export function mountUi({ modelId, modelBase, viewer }) {
  const app = document.getElementById("app");

  const top = document.createElement("div");
  top.className =
    "pointer-events-none fixed left-0 right-0 top-0 z-10 flex items-start justify-between gap-3 p-3 sm:p-4";
  top.innerHTML = `
    <div class="pointer-events-auto flex items-center gap-3 rounded-xl border border-gia-border bg-gia-panel px-4 py-2.5 shadow-lg backdrop-blur-md">
      <div class="text-sm font-semibold tracking-tight text-white">GIA Viewer</div>
      <div class="hidden h-4 w-px bg-gia-border sm:block"></div>
      <div id="gia-model-label" class="max-w-[50vw] truncate text-xs text-gia-muted font-mono">${modelId ? modelId : "No model"}</div>
    </div>
    <div class="pointer-events-auto flex flex-wrap items-center justify-end gap-2">
      <label class="flex cursor-pointer items-center gap-2 rounded-lg border border-gia-border bg-gia-panel px-3 py-2 text-xs text-gia-muted backdrop-blur-md hover:border-white/20">
        <input id="gia-ssao" type="checkbox" class="accent-gia-accent" />
        <span>Ambient occlusion</span>
      </label>
      <label class="flex cursor-pointer items-center gap-2 rounded-lg border border-gia-border bg-gia-panel px-3 py-2 text-xs text-gia-muted backdrop-blur-md hover:border-white/20">
        <input id="gia-grid" type="checkbox" class="accent-gia-accent" checked />
        <span>Grid</span>
      </label>
      <label class="flex cursor-pointer items-center gap-2 rounded-lg border border-gia-border bg-gia-panel px-3 py-2 text-xs text-gia-muted backdrop-blur-md hover:border-white/20">
        <input id="gia-ground" type="checkbox" class="accent-gia-accent" />
        <span>Ground plane</span>
      </label>
      <div class="flex items-center gap-1.5 rounded-lg border border-gia-border bg-gia-panel px-2 py-1.5 backdrop-blur-md">
        <button type="button" id="gia-bg-btn" class="flex items-center gap-2 rounded-md border border-gia-border bg-black/30 px-2 py-1 text-xs text-gia-muted hover:border-white/25 hover:text-white" title="Choose scene background color" aria-label="Choose background color">
          <span id="gia-bg-swatch" class="h-6 w-6 shrink-0 rounded border border-white/25 shadow-inner" aria-hidden="true"></span>
          <span class="whitespace-nowrap">Background</span>
        </button>
        <input id="gia-bg-color" type="color" class="sr-only" title="Scene background" />
      </div>
      <label class="flex cursor-pointer items-center gap-2 rounded-lg border border-gia-border bg-gia-panel px-3 py-2 text-xs text-gia-muted backdrop-blur-md hover:border-white/20" title="When the mesh’s on-screen diameter (bounding sphere) is below this many pixels, show convex hull. Empty = LOD off. 0 = hull only. Try ~80–200 for facades.">
        <span class="whitespace-nowrap">LOD (px)</span>
        <input id="gia-lodpx" type="number" min="0" step="5" class="w-[4.75rem] rounded border border-gia-border bg-black/40 px-1.5 py-1 font-mono text-[11px] text-white focus:border-gia-accent focus:outline-none" />
      </label>
    </div>
  `;
  app.appendChild(top);

  const panel = document.createElement("div");
  panel.className =
    "pointer-events-auto fixed bottom-0 left-0 z-10 m-3 max-w-[min(100%-24px,380px)] rounded-xl border border-gia-border bg-gia-panel p-4 shadow-xl backdrop-blur-md sm:m-4";
  panel.innerHTML = `
    <details class="group rounded-lg border border-white/10 bg-black/20">
      <summary class="cursor-pointer select-none list-none px-2 py-2.5 text-xs font-semibold uppercase tracking-wider text-gia-muted marker:content-none hover:text-white [&::-webkit-details-marker]:hidden">
        <span class="inline-block w-4 origin-center text-gia-muted transition-transform group-open:rotate-90">▸</span>
        Section planes
      </summary>
      <div class="space-y-3 border-t border-gia-border/60 px-2 pb-3 pt-3">
        <label class="flex items-center gap-2 text-xs text-gia-muted">
          <input id="gia-sec-x-on" type="checkbox" class="accent-gia-accent" />
          <span class="w-6 font-mono text-white">X</span>
          <input id="gia-sec-x" type="range" class="flex-1 accent-gia-accent" />
        </label>
        <label class="flex items-center gap-2 text-xs text-gia-muted">
          <input id="gia-sec-y-on" type="checkbox" class="accent-gia-accent" />
          <span class="w-6 font-mono text-white">Y</span>
          <input id="gia-sec-y" type="range" class="flex-1 accent-gia-accent" />
        </label>
        <label class="flex items-center gap-2 text-xs text-gia-muted">
          <input id="gia-sec-z-on" type="checkbox" class="accent-gia-accent" />
          <span class="w-6 font-mono text-white">Z</span>
          <input id="gia-sec-z" type="range" class="flex-1 accent-gia-accent" />
        </label>
      </div>
    </details>
    <details class="group mt-3 rounded-lg border border-white/10 bg-black/20">
      <summary class="cursor-pointer select-none list-none px-2 py-2.5 text-xs font-semibold uppercase tracking-wider text-gia-muted marker:content-none hover:text-white [&::-webkit-details-marker]:hidden">
        <span class="inline-block w-4 origin-center text-gia-muted transition-transform group-open:rotate-90">▸</span>
        Lighting &amp; AO
      </summary>
      <div class="border-t border-gia-border/60 px-2 pb-3 pt-3">
        <div id="gia-look-sliders" class="space-y-2.5"></div>
        <label class="mt-3 flex cursor-pointer items-center gap-2 text-xs text-gia-muted">
          <input id="gia-look-sync-url" type="checkbox" class="accent-gia-accent" checked />
          <span>Sync look to address bar</span>
        </label>
        <div class="mt-2 flex flex-wrap gap-2">
          <button type="button" id="gia-look-reset" class="rounded-lg border border-gia-border bg-black/30 px-2.5 py-1.5 text-xs text-gia-muted hover:border-white/25 hover:text-white">Reset look</button>
          <button type="button" id="gia-look-copy" class="rounded-lg border border-gia-border bg-black/30 px-2.5 py-1.5 text-xs text-gia-muted hover:border-white/25 hover:text-white">Copy view link</button>
        </div>
        <p id="gia-look-hint" class="mt-2 text-[10px] leading-snug text-gia-muted/90">
          Link includes lighting (<span class="font-mono">exp</span>, …), <span class="font-mono">bg</span>, <span class="font-mono">gp</span> (ground), camera <span class="font-mono">cx</span>–<span class="font-mono">cz</span>, target <span class="font-mono">tx</span>–<span class="font-mono">tz</span>, <span class="font-mono">lodpx</span>, <span class="font-mono">ssao</span>
        </p>
      </div>
    </details>
    <div class="mt-4 border-t border-gia-border pt-3">
      <div class="mb-2 text-xs font-semibold uppercase tracking-wider text-gia-muted">Open GLB URL</div>
      <div class="flex gap-2">
        <input id="gia-url" type="url" placeholder="https://…/model.glb" class="min-w-0 flex-1 rounded-lg border border-gia-border bg-black/30 px-2 py-1.5 text-xs text-white placeholder:text-gia-muted focus:border-gia-accent focus:outline-none" />
        <button id="gia-load" type="button" class="rounded-lg bg-gia-accent px-3 py-1.5 text-xs font-semibold text-white hover:brightness-110">Load</button>
      </div>
    </div>
  `;
  app.appendChild(panel);

  const loading = document.createElement("div");
  loading.id = "gia-loading";
  loading.className =
    "pointer-events-none fixed inset-0 z-20 flex items-center justify-center bg-black/50 opacity-0 transition-opacity duration-300";
  loading.innerHTML = `
    <div class="rounded-xl border border-gia-border bg-gia-panel px-6 py-4 text-sm text-white shadow-2xl backdrop-blur-md">
      <div class="mb-2 font-semibold">Loading model…</div>
      <div class="text-xs text-gia-muted">Large facades may take a moment.</div>
    </div>
  `;
  app.appendChild(loading);

  const toast = document.createElement("div");
  toast.id = "gia-toast";
  toast.className =
    "pointer-events-none fixed bottom-24 left-1/2 z-30 max-w-[90vw] -translate-x-1/2 rounded-lg border border-red-500/40 bg-red-950/90 px-4 py-2 text-center text-xs text-red-100 opacity-0 transition-opacity";
  app.appendChild(toast);

  function showLoading(show) {
    loading.style.opacity = show ? "1" : "0";
    loading.style.pointerEvents = show ? "auto" : "none";
  }

  function showToast(msg) {
    toast.textContent = msg;
    toast.style.opacity = "1";
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => {
      toast.style.opacity = "0";
    }, 6000);
  }

  function formatModelLoadError(err) {
    const msg = String(err?.message || err || "Unknown error");
    const lower = msg.toLowerCase();
    const network =
      lower.includes("failed to fetch") ||
      lower.includes("networkerror") ||
      lower.includes("load failed") ||
      lower.includes("network request failed");
    if (network && typeof window !== "undefined") {
      return `${msg} — Likely R2 CORS: add "${window.location.origin}" to the bucket AllowedOrigins (see docs/R2_SETUP.md).`;
    }
    return msg;
  }

  const ssaoEl = top.querySelector("#gia-ssao");
  ssaoEl.checked = viewer.useSsao;
  const syncLookUrl = debounce(() => {
    const syncEl = panel.querySelector("#gia-look-sync-url");
    if (!syncEl?.checked) return;
    const u = new URL(window.location.href);
    appendShareParams(u, viewer);
    window.history.replaceState({}, "", u);
  }, 250);

  ssaoEl.addEventListener("change", () => {
    viewer.setSsao(ssaoEl.checked);
    syncLookUrl();
  });

  viewer.controls.addEventListener("change", syncLookUrl);

  const lookRoot = panel.querySelector("#gia-look-sliders");
  for (const spec of LOOK_SLIDERS) {
    const row = document.createElement("div");
    row.className = "flex flex-col gap-0.5";
    const head = document.createElement("div");
    head.className = "flex items-baseline justify-between gap-2 text-xs text-gia-muted";
    const lab = document.createElement("span");
    lab.textContent = spec.label;
    const val = document.createElement("span");
    val.className =
      "gia-look-val shrink-0 font-mono text-[10px] text-white";
    head.append(lab, val);
    const range = document.createElement("input");
    range.type = "range";
    range.className = "h-1.5 w-full accent-gia-accent";
    range.min = String(spec.min);
    range.max = String(spec.max);
    range.step = String(spec.step);
    const applyFromViewer = () => {
      const s = viewer.getLookSettings();
      const n = s[spec.key];
      range.value = String(n);
      val.textContent = formatLookValue(spec.key, n);
    };
    range.addEventListener("input", () => {
      const n = parseFloat(range.value);
      viewer.applyLookSettings({ [spec.key]: n });
      val.textContent = formatLookValue(spec.key, n);
      syncLookUrl();
    });
    row.append(head, range);
    lookRoot.append(row);
    applyFromViewer();
  }

  panel.querySelector("#gia-look-reset").addEventListener("click", () => {
    viewer.resetLookSettings();
    lookRoot.querySelectorAll('input[type="range"]').forEach((range, i) => {
      const spec = LOOK_SLIDERS[i];
      const s = viewer.getLookSettings();
      range.value = String(s[spec.key]);
      const val = range.parentElement?.querySelector(".gia-look-val");
      if (val) val.textContent = formatLookValue(spec.key, s[spec.key]);
    });
    syncLookUrl();
  });

  panel.querySelector("#gia-look-copy").addEventListener("click", async () => {
    const u = new URL(window.location.href);
    appendShareParams(u, viewer);
    const link = u.toString();
    try {
      await navigator.clipboard.writeText(link);
      const hint = panel.querySelector("#gia-look-hint");
      if (hint) {
        const prev = hint.textContent;
        hint.textContent = "Copied view link (lighting, background, camera) to clipboard.";
        setTimeout(() => {
          hint.textContent = prev;
        }, 2200);
      }
    } catch {
      window.prompt("Copy this URL:", link);
    }
  });

  panel.querySelector("#gia-look-sync-url").addEventListener("change", () => {
    if (panel.querySelector("#gia-look-sync-url").checked) syncLookUrl();
  });

  const gridEl = top.querySelector("#gia-grid");
  gridEl.addEventListener("change", () => viewer.setGridVisible(gridEl.checked));

  const groundEl = top.querySelector("#gia-ground");
  groundEl.checked = viewer.getGroundPlaneVisible();
  groundEl.addEventListener("change", () => {
    viewer.setGroundPlaneVisible(groundEl.checked);
    syncLookUrl();
  });

  const lodpxEl = top.querySelector("#gia-lodpx");
  function syncLodFieldFromViewer() {
    const v = viewer.getLodDetailMinPx();
    lodpxEl.value = v === null ? "" : String(v);
  }
  syncLodFieldFromViewer();
  lodpxEl.placeholder = "off";
  const applyLodFromInput = () => {
    const raw = lodpxEl.value.trim();
    if (raw === "") {
      viewer.setLodDetailMinPx(null);
      syncLodFieldFromViewer();
      return;
    }
    const n = parseFloat(raw);
    if (!Number.isFinite(n) || n < 0) {
      viewer.setLodDetailMinPx(null);
      syncLodFieldFromViewer();
      return;
    }
    viewer.setLodDetailMinPx(n);
    syncLodFieldFromViewer();
  };
  lodpxEl.addEventListener("change", () => {
    applyLodFromInput();
    syncLookUrl();
  });
  lodpxEl.addEventListener("keydown", (e) => {
    if (e.key === "Enter") {
      applyLodFromInput();
      syncLookUrl();
    }
  });

  const bgColorEl = top.querySelector("#gia-bg-color");
  const bgBtn = top.querySelector("#gia-bg-btn");
  const bgSwatch = top.querySelector("#gia-bg-swatch");
  function syncBgSwatch() {
    const hex = viewer.getBackgroundColorHex();
    bgColorEl.value = hex;
    if (bgSwatch) bgSwatch.style.backgroundColor = hex;
  }
  syncBgSwatch();
  bgBtn.addEventListener("click", () => bgColorEl.click());
  bgColorEl.addEventListener("input", () => {
    const v = bgColorEl.value;
    viewer.setBackgroundColor(v);
    if (bgSwatch) bgSwatch.style.backgroundColor = v;
    try {
      localStorage.setItem("gia-bg", v);
    } catch {
      /* private mode */
    }
    syncLookUrl();
  });

  function wireSection(axis, onId, rangeId) {
    const on = panel.querySelector(onId);
    const range = panel.querySelector(rangeId);
    const update = () => {
      const b = viewer.getSectionBounds();
      const min = b.min[axis];
      const max = b.max[axis];
      if (!Number.isFinite(min) || !Number.isFinite(max) || min >= max) return;
      range.min = min;
      range.max = max;
      range.step = (max - min) / 500 || 0.001;
      if (!range.dataset.inited) {
        range.value = String((min + max) / 2);
        range.dataset.inited = "1";
      }
      const pos = parseFloat(range.value);
      viewer.setSectionAxis(axis, on.checked, pos);
    };
    on.addEventListener("change", update);
    range.addEventListener("input", update);
    return update;
  }

  const ux = wireSection("x", "#gia-sec-x-on", "#gia-sec-x");
  const uy = wireSection("y", "#gia-sec-y-on", "#gia-sec-y");
  const uz = wireSection("z", "#gia-sec-z-on", "#gia-sec-z");

  window.addEventListener("gia-model-loaded", () => {
    ["#gia-sec-x", "#gia-sec-y", "#gia-sec-z"].forEach((sel) => {
      delete panel.querySelector(sel).dataset.inited;
    });
    ux();
    uy();
    uz();
  });

  panel.querySelector("#gia-load").addEventListener("click", async () => {
    const url = panel.querySelector("#gia-url").value.trim();
    if (!url) return;
    showLoading(true);
    try {
      await viewer.loadFromUrl(url);
      const label = url.split("/").pop() || url;
      top.querySelector("#gia-model-label").textContent = label;
      window.dispatchEvent(new Event("gia-model-loaded"));
    } catch (e) {
      showToast(formatModelLoadError(e));
    } finally {
      showLoading(false);
    }
  });

  if (modelId && !modelBase) {
    showToast(
      "VITE_R2_PUBLIC_BASE_URL was not set at build time. Use “Open GLB URL” or rebuild with env."
    );
  }

  if (modelId && modelBase) {
    showLoading(true);
    const objectUrl = `${modelBase}/${encodeURIComponent(modelId)}.glb`;
    viewer
      .loadFromUrl(objectUrl)
      .then(() => window.dispatchEvent(new Event("gia-model-loaded")))
      .catch((e) => showToast(formatModelLoadError(e)))
      .finally(() => showLoading(false));
  }
}
