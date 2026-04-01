import { S3Client, PutObjectCommand } from "@aws-sdk/client-s3";
import { getSignedUrl } from "@aws-sdk/s3-request-presigner";
import { randomUUID } from "crypto";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Headers":
    "Content-Type, Authorization, X-GIA-Upload-Secret",
  "Access-Control-Max-Age": "86400",
};

function getClient() {
  const accountId = process.env.R2_ACCOUNT_ID;
  const accessKeyId = process.env.R2_ACCESS_KEY_ID;
  const secretAccessKey = process.env.R2_SECRET_ACCESS_KEY;
  const bucket = process.env.R2_BUCKET_NAME;

  if (!accountId || !accessKeyId || !secretAccessKey || !bucket) {
    throw new Error(
      "Missing R2 env: R2_ACCOUNT_ID, R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, R2_BUCKET_NAME"
    );
  }

  return {
    client: new S3Client({
      region: "auto",
      endpoint: `https://${accountId}.r2.cloudflarestorage.com`,
      credentials: { accessKeyId, secretAccessKey },
    }),
    bucket,
  };
}

function sanitizeStableKey(raw) {
  if (raw == null || typeof raw !== "string") return null;
  const t = raw.trim().replace(/[^a-zA-Z0-9_-]/g, "").slice(0, 96);
  return t.length > 0 ? t : null;
}

function readRequestBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (c) => chunks.push(c));
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

export default async function handler(req, res) {
  Object.entries(corsHeaders).forEach(([k, v]) => res.setHeader(k, v));

  if (req.method === "OPTIONS") {
    res.statusCode = 204;
    res.end();
    return;
  }

  if (req.method !== "POST") {
    res.statusCode = 405;
    res.setHeader("Content-Type", "application/json");
    res.end(JSON.stringify({ error: "Method not allowed" }));
    return;
  }

  const expectedSecret = process.env.GIA_UPLOAD_SECRET?.trim();
  if (expectedSecret) {
    const sent = req.headers["x-gia-upload-secret"];
    if (sent !== expectedSecret) {
      res.statusCode = 401;
      res.setHeader("Content-Type", "application/json");
      res.end(
        JSON.stringify({
          error:
            "Unauthorized: set Vercel GIA_UPLOAD_SECRET and send the same value in header X-GIA-Upload-Secret from Grasshopper.",
        })
      );
      return;
    }
  }

  try {
    let requestedKey = null;
    try {
      const raw = await readRequestBody(req);
      if (raw) {
        const j = JSON.parse(raw);
        if (j && typeof j.key === "string") {
          requestedKey = sanitizeStableKey(j.key);
        }
      }
    } catch {
      /* ignore */
    }

    const modelId = requestedKey || randomUUID();
    const key = `${modelId}.glb`;

    const { client, bucket } = getClient();

    const command = new PutObjectCommand({
      Bucket: bucket,
      Key: key,
      ContentType: "model/gltf-binary",
    });

    const presignedUrl = await getSignedUrl(client, command, {
      expiresIn: 3600,
    });

    const publicBase =
      process.env.R2_PUBLIC_BASE_URL?.replace(/\/$/, "") || "";
    const publicUrl = publicBase ? `${publicBase}/${key}` : "";

    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json");
    res.end(
      JSON.stringify({
        presignedUrl,
        modelId,
        modelUuid: modelId,
        publicUrl,
        key,
        overwrite: Boolean(requestedKey),
      })
    );
  } catch (e) {
    console.error(e);
    res.statusCode = 500;
    res.setHeader("Content-Type", "application/json");
    res.end(JSON.stringify({ error: String(e.message || e) }));
  }
}
