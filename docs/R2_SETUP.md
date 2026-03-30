# Cloudflare R2 setup

1. In the Cloudflare dashboard, open **R2** → **Create bucket** (e.g. `gia-viewer-models`).
2. **Settings → Public access**: enable public development URL or attach a **Custom Domain** (recommended for production).
3. **CORS** for the bucket: allow `GET` from your Vercel viewer origin (e.g. `https://your-app.vercel.app`) and optionally `*` for testing.
4. **API tokens**: R2 → **Manage R2 API Tokens** → create token with **Object Read & Write** on this bucket. Note **Access Key ID**, **Secret Access Key**, and **Account ID** (from the R2 overview URL or dashboard sidebar).
5. In **Vercel** → your project → **Settings → Environment Variables**, set:
   - `R2_ACCOUNT_ID`
   - `R2_ACCESS_KEY_ID`
   - `R2_SECRET_ACCESS_KEY`
   - `R2_BUCKET_NAME` (bucket name)
   - `R2_PUBLIC_BASE_URL` (same value as `VITE_R2_PUBLIC_BASE_URL`, no trailing slash)
6. In Vercel, set **Build Environment Variable** `VITE_R2_PUBLIC_BASE_URL` to the public URL clients use to download `.glb` files (must match the bucket’s public base).

Presigned uploads use the S3-compatible endpoint `https://<R2_ACCOUNT_ID>.r2.cloudflarestorage.com`. The Grasshopper plugin uploads bytes to the presigned URL returned by `POST /api/upload`.

See also: [Cloudflare R2 documentation](https://developers.cloudflare.com/r2/).
