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
        <input id="gia-ssao" type="checkbox" class="accent-gia-accent" checked />
        <span>Ambient occlusion</span>
      </label>
      <label class="flex cursor-pointer items-center gap-2 rounded-lg border border-gia-border bg-gia-panel px-3 py-2 text-xs text-gia-muted backdrop-blur-md hover:border-white/20">
        <input id="gia-grid" type="checkbox" class="accent-gia-accent" checked />
        <span>Grid</span>
      </label>
    </div>
  `;
  app.appendChild(top);

  const panel = document.createElement("div");
  panel.className =
    "pointer-events-auto fixed bottom-0 left-0 z-10 m-3 max-w-[min(100%-24px,380px)] rounded-xl border border-gia-border bg-gia-panel p-4 shadow-xl backdrop-blur-md sm:m-4";
  panel.innerHTML = `
    <div class="mb-3 text-xs font-semibold uppercase tracking-wider text-gia-muted">Section planes</div>
    <div class="space-y-3">
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

  const ssaoEl = top.querySelector("#gia-ssao");
  ssaoEl.addEventListener("change", () => viewer.setSsao(ssaoEl.checked));

  const gridEl = top.querySelector("#gia-grid");
  gridEl.addEventListener("change", () => viewer.setGridVisible(gridEl.checked));

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
      showToast(String(e.message || e));
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
      .catch((e) => showToast(String(e.message || e)))
      .finally(() => showLoading(false));
  }
}
