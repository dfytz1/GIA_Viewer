# Cloudflare R2 setup

1. In the Cloudflare dashboard, open **R2** → **Create bucket** (e.g. `gia-viewer-models`).
2. **Settings → Public access**: enable public development URL or attach a **Custom Domain** (recommended for production).
3. **CORS** (required for the viewer + browser uploads): without this, **Open GLB URL** and `?m=` loads fail with **“failed to fetch”** even though the `.glb` opens in a new tab.

   In the Cloudflare dashboard: **R2** → your bucket → **Settings** → **CORS Policy** → **Add CORS policy** → **JSON** tab, paste and save (replace the origin if your Vercel URL differs):

   ```json
   [
     {
       "AllowedOrigins": [
         "https://gia-viewer.vercel.app",
         "https://viewer-dusky.vercel.app"
       ],
       "AllowedMethods": ["GET", "HEAD", "PUT"],
       "AllowedHeaders": ["*"],
       "ExposeHeaders": ["ETag", "Content-Length"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```

   - **GET** / **HEAD**: loading models in the browser (Three.js `fetch`).
   - **PUT**: optional but recommended so **presigned uploads** from a browser (if you add one later) work; Grasshopper’s `HttpClient` upload does not use CORS.

   Add **every** viewer origin you use (no trailing slash). If you open a share link on `https://viewer-dusky.vercel.app` but CORS only allows `https://gia-viewer.vercel.app`, the console shows **Failed to fetch** and the model will not load.

   For **Vercel preview** URLs (`*.vercel.app` with random names), either add another origin string, or temporarily use `"AllowedOrigins": ["*"]` while testing (looser).

   Origins must **not** include a trailing slash or path — see [Configure CORS](https://developers.cloudflare.com/r2/buckets/cors/).
4. **API tokens**: R2 → **Manage R2 API Tokens** → create token with **Object Read & Write** on this bucket. Note **Access Key ID**, **Secret Access Key**, and **Account ID** (from the R2 overview URL or dashboard sidebar).
5. In **Vercel** → your project → **Settings → Environment Variables**, set:
   - `R2_ACCOUNT_ID`
   - `R2_ACCESS_KEY_ID`
   - `R2_SECRET_ACCESS_KEY`
   - `R2_BUCKET_NAME` (bucket name)
   - `R2_PUBLIC_BASE_URL` (same value as `VITE_R2_PUBLIC_BASE_URL`, no trailing slash)
6. In Vercel, set **Build Environment Variable** `VITE_R2_PUBLIC_BASE_URL` to the public URL clients use to download `.glb` files (must match the bucket’s public base).

Presigned uploads use the S3-compatible endpoint `https://<R2_ACCOUNT_ID>.r2.cloudflarestorage.com`. The Grasshopper plugin uploads bytes to the presigned URL returned by `POST /api/upload`.

### Stable keys (overwrite same file)

`POST /api/upload` with JSON body `{"key":"my-project"}` issues a presigned PUT for `my-project.glb`, overwriting any previous object with that key. The response includes `modelId` / `modelUuid` equal to `my-project` for building `?m=my-project` links.

### Optional upload lock

Set **`GIA_UPLOAD_SECRET`** in Vercel. Then every `POST /api/upload` must include header **`X-GIA-Upload-Secret: <same value>`** (Grasshopper **UploadSecret** input). Recommended if you use predictable **StableKey** values.

See also: [Cloudflare R2 documentation](https://developers.cloudflare.com/r2/).
