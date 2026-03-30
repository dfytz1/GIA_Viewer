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

Deploy on [Vercel](https://vercel.com): set the project root to `viewer`, add environment variables from `.env.example` (both `VITE_*` for build and `R2_*` for the API). See [docs/R2_SETUP.md](docs/R2_SETUP.md) for the bucket, CORS, and tokens.

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

## End-to-end

1. Deploy the `viewer` app and configure R2 + env vars.
2. In Grasshopper, set **ApiBase** and **ViewerBase** to that deployment URL (same origin is typical).
3. Toggle **Publish** to upload and get `https://…/?m=<uuid>`.

The viewer applies a **−90° X** rotation so **Rhino Z-up** models read sensibly in **Three.js Y-up**. Large models: consider meshing coarser panels or splitting files; GLB is binary but not Draco-compressed in this plugin (can be added later).
