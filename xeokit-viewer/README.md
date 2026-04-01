# GIA xeokit viewer

Alternative web client using [**@xeokit/xeokit-sdk**](https://github.com/xeokit/xeokit-sdk) (`GLTFLoaderPlugin`) for the same **GLB** files produced by the Grasshopper plugin.

## License note

xeokit SDK is **AGPL-3.0**. If you ship this app in a way that triggers AGPL obligations, comply accordingly or obtain a commercial license from the xeokit authors. See [xeokit.io](https://xeokit.io).

## Grasshopper

Use **Publish Xeokit** (`PublishXeokitModelComponent`). It calls **`XeokitSceneExporter`** (currently the same geometry as `GlbExporter`) and builds links with **`GiaDefaults.PublicXeokitViewerBase`** when **ViewerBase** is empty.

- **ApiBase** — where `POST /api/upload` lives (often your main `gia-viewer` deployment).
- **ViewerBase** — this app’s origin (e.g. `https://gia-xeokit.vercel.app`).

## Deploy (Vercel)

Create a project with **Root Directory** = `xeokit-viewer`. Set the same **R2 / upload** env vars as `viewer/` (`R2_*`, `R2_PUBLIC_BASE_URL`, optional `GIA_UPLOAD_SECRET`). Set **VITE_R2_PUBLIC_BASE_URL** for the build.

Add this viewer’s origin to **R2 CORS** (see `docs/R2_SETUP.md`).

## Local dev

```bash
cd xeokit-viewer
npm install
cp .env.example .env.local
npm run dev
```

Open `http://localhost:5174/?url=https://…/model.glb` or `?m=<uuid>` when `VITE_R2_PUBLIC_BASE_URL` is set.
