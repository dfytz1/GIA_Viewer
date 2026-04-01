# GIA Viewer

Grasshopper plugin + static web viewer for publishing facade meshes as **GLB** (PBR materials, mesh reuse via repeated nodes) and sharing a **public link** (`?m=<uuid>`). Uploads use **Vercel** `POST /api/upload` and **Cloudflare R2** presigned `PUT`.

## Web viewer (`viewer/`)

```bash
cd viewer
npm install
cp .env.example .env.local
# Set VITE_R2_PUBLIC_BASE_URL to your R2 public base (no trailing slash)
npm run build
```

Local dev (viewer only; API needs `vercel dev` or deployed backend):

```bash
npm run dev
```

Deploy on [Vercel](https://vercel.com): the **`gia-viewer`** project uses **Root Directory** = `viewer` (subfolder of this repo). **Production deploy** (canonical URL `https://gia-viewer.vercel.app`): from the **repository root** (`GIA_Viewer/`), run `npx vercel link` once (choose project `gia-viewer`), then `npx vercel --prod`, or from `viewer/` run `npm run deploy:production` (runs Vercel from the parent folder so paths match). Do **not** run `vercel --prod` only from inside `viewer/` if the Vercel project has Root Directory `viewer` — that doubles the path and fails.

Add environment variables from `viewer/.env.example` (`VITE_*` for build, `R2_*` for the API). See [docs/R2_SETUP.md](docs/R2_SETUP.md) for the bucket, CORS, and tokens.

## Grasshopper plugin (`GrasshopperPlugin/`)

The project uses the official [**Grasshopper** NuGet package](https://www.nuget.org/packages/Grasshopper) (`ExcludeAssets="runtime"`), per [McNeel — Using NuGet](https://developer.rhino3d.com/guides/rhinocommon/using-nuget/). **You can compile without Rhino installed**; you still need **Rhino 8 with Grasshopper** to load and test the `.gha`.

From the plugin folder:

```bash
dotnet build -c Release
```

Copy `bin/Release/GIAViewer.gha` (same path for `Debug`) into Grasshopper’s components folder, or use **Grasshopper → File → Special Folders → User Object Folders**.

Bump the `Grasshopper` package version in `GIAViewer.csproj` when you want to target a newer Rhino 8 SDK.

### Components

- **Bim Material** — color, metallic, roughness, name.
- **Bim Mesh** — Rhino mesh + material + `MeshId` (template geometry).
- **Bim Instance** — `MeshId` + plane (repeat placements; same id shares one mesh in GLB).
- **Publish Model** — tree of mesh + instance items; optional local GLB path; with **Publish** true, calls your **ApiBase** `/api/upload`, then `PUT`s the file and outputs a **ViewerUrl** (also copied to the clipboard when Eto allows).

**Stable client link:** set **StableKey** (e.g. `main-facade`) to always upload to the same object `main-facade.glb`. The **ViewerUrl** stays `…?m=main-facade` on every publish; R2 overwrites the file so clients refresh to see updates.

**Optional `GIA_UPLOAD_SECRET`:** set this in Vercel; then set **UploadSecret** on the component to the same value. Otherwise anyone who can call `/api/upload` could overwrite a known stable key.

**Default viewer URL:** if **ApiBase** / **ViewerBase** are empty, Publish uses `GiaDefaults.PublicViewerBase` in `GrasshopperPlugin/Models/GiaDefaults.cs`. That must match the Vercel deployment where you actually run `npx vercel --prod` for `viewer/` (same origin as `/api/upload`). If copied links open an old build, either update that constant and rebuild the `.gha`, or set **ApiBase** and **ViewerBase** explicitly on the component.

## End-to-end

1. Deploy the `viewer` app and configure R2 + env vars.
2. In Grasshopper, set **ApiBase** and **ViewerBase** to that deployment URL (same origin is typical), or rely on `GiaDefaults.PublicViewerBase` after editing it to match production.
3. Toggle **Publish** to upload and get `https://…/?m=<uuid>`.

The viewer applies a **−90° X** rotation so **Rhino Z-up** models read sensibly in **Three.js Y-up**. Large models: consider meshing coarser panels or splitting files; GLB is binary but not Draco-compressed in this plugin (can be added later).
