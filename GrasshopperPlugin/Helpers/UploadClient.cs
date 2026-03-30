using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GIAViewer.Helpers
{
    internal static class UploadClient
    {
        public static (string viewerUrl, string status, string detail) Publish(
            string glbPath,
            string apiBase,
            string viewerBase)
        {
            if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
                return ("", "Error", "GLB file not found.");

            apiBase = apiBase.Trim().TrimEnd('/');
            viewerBase = viewerBase.Trim().TrimEnd('/');

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                var postUri = new Uri($"{apiBase}/api/upload");
                var post = client.PostAsync(postUri, null).GetAwaiter().GetResult();
                var body = post.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!post.IsSuccessStatusCode)
                    return ("", "Error", $"POST /api/upload: {(int)post.StatusCode} {body}");

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("presignedUrl", out var pu) ||
                    !root.TryGetProperty("modelUuid", out var idEl))
                    return ("", "Error", "Invalid API response (missing presignedUrl or modelUuid).");

                var presignedUrl = pu.GetString();
                var modelUuid = idEl.GetString();
                if (string.IsNullOrEmpty(presignedUrl) || string.IsNullOrEmpty(modelUuid))
                    return ("", "Error", "Empty presigned URL or model id.");

                var bytes = File.ReadAllBytes(glbPath);
                using var put = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
                put.Content = new ByteArrayContent(bytes);
                put.Content.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");
                var putResp = client.SendAsync(put).GetAwaiter().GetResult();
                var putBody = putResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!putResp.IsSuccessStatusCode)
                    return ("", "Error", $"PUT to R2: {(int)putResp.StatusCode} {putBody}");

                var link = viewerBase.Contains("?", StringComparison.Ordinal)
                    ? $"{viewerBase}&m={modelUuid}"
                    : $"{viewerBase}?m={modelUuid}";

                return (link, "OK", "Uploaded.");
            }
            catch (Exception ex)
            {
                return ("", "Error", ex.Message);
            }
        }
    }
}
