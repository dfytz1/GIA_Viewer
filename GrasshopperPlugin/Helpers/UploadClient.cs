using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GIAViewer.Helpers
{
    internal static class UploadClient
    {
        public static (string viewerUrl, string status, string detail) Publish(
            string glbPath,
            string apiBase,
            string viewerBase,
            string stableKey = null,
            string uploadSecret = null)
        {
            if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
                return ("", "Error", "GLB file not found.");

            apiBase = apiBase.Trim().TrimEnd('/');
            viewerBase = viewerBase.Trim().TrimEnd('/');

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                var postUri = new Uri($"{apiBase}/api/upload");

                object jsonObj = string.IsNullOrWhiteSpace(stableKey)
                    ? new { }
                    : new { key = stableKey.Trim() };
                var json = JsonSerializer.Serialize(jsonObj);
                using var postReq = new HttpRequestMessage(HttpMethod.Post, postUri);
                postReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(uploadSecret))
                    postReq.Headers.TryAddWithoutValidation("X-GIA-Upload-Secret", uploadSecret.Trim());

                var post = client.SendAsync(postReq).GetAwaiter().GetResult();
                var body = post.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!post.IsSuccessStatusCode)
                    return ("", "Error", $"POST /api/upload: {(int)post.StatusCode} {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("presignedUrl", out var pu))
                    return ("", "Error", "Invalid API response (missing presignedUrl).");

                string modelId = null;
                if (root.TryGetProperty("modelId", out var idEl))
                    modelId = idEl.GetString();
                if (string.IsNullOrEmpty(modelId) && root.TryGetProperty("modelUuid", out var legacy))
                    modelId = legacy.GetString();

                var presignedUrl = pu.GetString();
                if (string.IsNullOrEmpty(presignedUrl) || string.IsNullOrEmpty(modelId))
                    return ("", "Error", "Empty presigned URL or model id.");

                var bytes = File.ReadAllBytes(glbPath);
                using var put = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
                put.Content = new ByteArrayContent(bytes);
                put.Content.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
                var putResp = client.SendAsync(put).GetAwaiter().GetResult();
                var putBody = putResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!putResp.IsSuccessStatusCode)
                    return ("", "Error", $"PUT to R2: {(int)putResp.StatusCode} {putBody}");

                var q = Uri.EscapeDataString(modelId);
                var link = viewerBase.Contains("?", StringComparison.Ordinal)
                    ? $"{viewerBase}&m={q}"
                    : $"{viewerBase}?m={q}";

                var note = string.IsNullOrWhiteSpace(stableKey)
                    ? "Uploaded (new id)."
                    : "Uploaded (same link; object overwritten in R2).";

                return (link, "OK", note);
            }
            catch (Exception ex)
            {
                return ("", "Error", ex.Message);
            }
        }
    }
}
