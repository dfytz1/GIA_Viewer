using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GIAViewer.Helpers
{
    internal static class UploadClient
    {
        /// <summary>Shared client avoids per-upload socket churn; do not dispose.</summary>
        private static readonly HttpClient SharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15),
        };

        public static (string viewerUrl, string status, string detail) Publish(
            string glbPath,
            string apiBase,
            string viewerBase,
            string stableKey = null,
            string uploadSecret = null)
        {
            return PublishAsync(glbPath, apiBase, viewerBase, stableKey, uploadSecret, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public static async Task<(string viewerUrl, string status, string detail)> PublishAsync(
            string glbPath,
            string apiBase,
            string viewerBase,
            string stableKey = null,
            string uploadSecret = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
                return ("", "Error", "GLB file not found.");

            apiBase = apiBase.Trim().TrimEnd('/');
            viewerBase = viewerBase.Trim().TrimEnd('/');

            try
            {
                var postUri = new Uri($"{apiBase}/api/upload");

                object jsonObj = string.IsNullOrWhiteSpace(stableKey)
                    ? new { }
                    : new { key = stableKey.Trim() };
                var json = JsonSerializer.Serialize(jsonObj);
                using var postReq = new HttpRequestMessage(HttpMethod.Post, postUri);
                postReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(uploadSecret))
                    postReq.Headers.TryAddWithoutValidation("X-GIA-Upload-Secret", uploadSecret.Trim());

                using var post = await SharedClient.SendAsync(postReq, cancellationToken).ConfigureAwait(false);
                var body = await post.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

                await using var fileStream = new FileStream(
                    glbPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1 << 20,
                    options: FileOptions.Asynchronous);
                using var put = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
                put.Content = new StreamContent(fileStream);
                put.Content.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
                put.Content.Headers.ContentLength = fileStream.Length;

                using var putResp = await SharedClient.SendAsync(put, cancellationToken).ConfigureAwait(false);
                var putBody = await putResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
            catch (OperationCanceledException)
            {
                return ("", "Cancelled", "Upload cancelled.");
            }
            catch (Exception ex)
            {
                return ("", "Error", ex.Message);
            }
        }
    }
}
