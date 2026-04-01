using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Eto.Forms;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;

namespace GIAViewer.Components
{
    /// <summary>
    /// Same pipeline as <see cref="PublishModelComponent"/>, but default viewer URL targets the xeokit app
    /// (<see cref="GiaDefaults.PublicXeokitViewerBase"/>). Uses <see cref="XeokitSceneExporter"/> for the GLB file.
    /// Upload still posts to <c>/api/upload</c> on <b>ApiBase</b> (often the main GIA Viewer deployment).
    /// </summary>
    public class PublishXeokitModelComponent : GH_Component
    {
        private string _outUrl = "";
        private string _outStatus = "Idle";
        private string _outMessage = "";
        private int _uploadGeneration;
        private int _pendingOutputRefresh;
        private CancellationTokenSource _uploadCts;

        public PublishXeokitModelComponent()
            : base(
                "Publish Xeokit",
                "XeokitPub",
                "Build GLB (xeokit path), upload to R2, return xeokit viewer link. Api defaults to GIA viewer; Viewer defaults to xeokit app.",
                "GIA Viewer",
                "Web")
        {
        }

        public override Guid ComponentGuid => new Guid("f7e8d9c0-b1a2-4f3e-9d8c-7b6a5e4d3c2b");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Items",
                "I",
                "Mesh definitions and instances (same as Publish Model).",
                GH_ParamAccess.tree);
            pManager.AddTextParameter(
                "ApiBase",
                "A",
                "Upload API origin (e.g. https://gia-viewer.vercel.app). Empty = GiaDefaults.PublicViewerBase.",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "ViewerBase",
                "V",
                "Xeokit viewer origin. Empty = GiaDefaults.PublicXeokitViewerBase.",
                GH_ParamAccess.item,
                "");
            pManager.AddBooleanParameter(
                "Publish",
                "P",
                "True = upload in background. False = local GLB only unless L is set.",
                GH_ParamAccess.item,
                false);
            pManager.AddTextParameter(
                "LocalGlb",
                "L",
                "Full path to save .glb locally (optional).",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "StableKey",
                "K",
                "Stable ?m= key (a-z 0-9 _ -). Empty = random id.",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "UploadSecret",
                "X",
                "Optional; must match Vercel GIA_UPLOAD_SECRET if set.",
                GH_ParamAccess.item,
                "");
            pManager.AddNumberParameter(
                "Scale",
                "Sc",
                "Uniform scale on export (e.g. 0.001 for mm → meters in the viewer). 1 = document units unchanged.",
                GH_ParamAccess.item,
                1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("ViewerUrl", "U", "Xeokit viewer link", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "S", "OK or Error", GH_ParamAccess.item);
            pManager.AddTextParameter("Message", "M", "Details", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            if (da.Iteration == 0)
            {
                if (Interlocked.CompareExchange(ref _pendingOutputRefresh, 0, 1) == 1)
                    goto DoneIter0;

                var apiBase = "";
                var viewerBase = "";
                var publish = false;
                var localPath = "";
                var stableKey = "";
                var uploadSecret = "";
                var geometryScale = 1.0;
                da.GetData(1, ref apiBase);
                da.GetData(2, ref viewerBase);
                da.GetData(3, ref publish);
                da.GetData(4, ref localPath);
                da.GetData(5, ref stableKey);
                da.GetData(6, ref uploadSecret);
                da.GetData(7, ref geometryScale);

                if (!publish)
                {
                    Interlocked.Increment(ref _uploadGeneration);
                    _uploadCts?.Cancel();
                    _uploadCts?.Dispose();
                    _uploadCts = null;
                }

                _outStatus = "Idle";
                _outMessage = "";

                var param = Params.Input[0];
                if (param?.VolatileData == null)
                {
                    _outUrl = "";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No items.");
                    _outMessage = "Connect mesh definitions / instances.";
                }
                else
                {
                    var flat = new List<object>();
                    var structure = param.VolatileData;
                    foreach (var path in structure.Paths)
                    {
                        var branch = structure.get_Branch(path);
                        foreach (var obj in branch)
                        {
                            if (obj is not IGH_Goo gh) continue;
                            object v = null;
                            if (gh is GH_ObjectWrapper ow)
                                v = ow.Value;
                            else
                            {
                                try
                                {
                                    v = gh.ScriptVariable();
                                }
                                catch
                                {
                                    /* ignore */
                                }
                            }

                            if (v != null)
                                flat.Add(v);
                        }
                    }

                    var meshById = new Dictionary<string, GiaMeshDefinition>(StringComparer.OrdinalIgnoreCase);
                    var placements = new List<(string meshId, Matrix4x4 matrix)>();
                    GiaExportCollector.Collect(flat, meshById, placements, w => AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w));

                    if (meshById.Count == 0)
                    {
                        _outUrl = "";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh definitions.");
                        _outMessage = "Add GiaMeshDefinition data (same as Publish Model).";
                    }
                    else
                    {
                        var needExport = publish || !string.IsNullOrWhiteSpace(localPath);
                        if (!needExport)
                        {
                            _outStatus = "Idle";
                            _outMessage = "Publish off: set P=true or L path.";
                            goto DoneIter0;
                        }

                        if (publish)
                        {
                            if (string.IsNullOrWhiteSpace(apiBase))
                                apiBase = GiaDefaults.PublicViewerBase;
                            if (string.IsNullOrWhiteSpace(viewerBase))
                                viewerBase = GiaDefaults.PublicXeokitViewerBase;
                        }

                        apiBase = NormalizeBaseUrl(apiBase);
                        viewerBase = NormalizeBaseUrl(viewerBase);

                        if (geometryScale <= 0.0 || double.IsNaN(geometryScale) || double.IsInfinity(geometryScale))
                        {
                            _outUrl = "";
                            _outStatus = "Error";
                            _outMessage = "Scale (Sc) must be a positive finite number.";
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _outMessage);
                            goto DoneIter0;
                        }

                        var tempGlb = Path.Combine(Path.GetTempPath(), $"gia_xeokit_{Guid.NewGuid():N}.glb");
                        try
                        {
                            XeokitSceneExporter.ExportGlb(tempGlb, meshById, placements, (float)geometryScale);
                        }
                        catch (Exception ex)
                        {
                            _outUrl = "";
                            _outStatus = "Error";
                            _outMessage = ex.Message;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                            goto DoneIter0;
                        }

                        if (!string.IsNullOrWhiteSpace(localPath))
                        {
                            var destFile = ResolveLocalGlbPath(localPath.Trim());
                            if (destFile != null)
                            {
                                try
                                {
                                    var dir = Path.GetDirectoryName(destFile);
                                    if (!string.IsNullOrEmpty(dir))
                                        Directory.CreateDirectory(dir);
                                    File.Copy(tempGlb, destFile, true);
                                    AddRuntimeMessage(
                                        GH_RuntimeMessageLevel.Remark,
                                        $"Saved GLB to: {destFile}");
                                }
                                catch (Exception ex)
                                {
                                    AddRuntimeMessage(
                                        GH_RuntimeMessageLevel.Warning,
                                        "Local save failed: " + ex.Message);
                                }
                            }
                        }

                        if (!publish)
                        {
                            _outStatus = "GLB ready";
                            _outMessage = tempGlb;
                            goto DoneIter0;
                        }

                        _outUrl = "";
                        _outStatus = "Uploading…";
                        _outMessage = "Upload running in background.";

                        _uploadCts?.Cancel();
                        _uploadCts?.Dispose();
                        _uploadCts = new CancellationTokenSource();
                        var token = _uploadCts.Token;
                        var taskGen = Interlocked.Increment(ref _uploadGeneration);

                        var pathCopy = tempGlb;
                        var apiCopy = apiBase;
                        var viewerCopy = viewerBase;
                        var keyCopy = stableKey;
                        var secretCopy = uploadSecret;

                        _ = Task.Run(
                            async () =>
                            {
                                try
                                {
                                    var (url, status, detail) = await UploadClient.PublishAsync(
                                        pathCopy,
                                        apiCopy,
                                        viewerCopy,
                                        keyCopy,
                                        secretCopy,
                                        token).ConfigureAwait(false);

                                    RhinoApp.InvokeOnUiThread(
                                        () => ApplyUploadResult(taskGen, url, status, detail));
                                }
                                catch (OperationCanceledException)
                                {
                                    RhinoApp.InvokeOnUiThread(() => ApplyUploadResult(taskGen, "", "Cancelled", "Upload cancelled."));
                                }
                                finally
                                {
                                    try
                                    {
                                        if (File.Exists(pathCopy))
                                            File.Delete(pathCopy);
                                    }
                                    catch
                                    {
                                        /* ignore */
                                    }
                                }
                            },
                            token);
                    }
                }

            DoneIter0: ;
            }

            da.SetData(0, _outUrl);
            da.SetData(1, _outStatus);
            da.SetData(2, _outMessage);
        }

        private void ApplyUploadResult(int taskGen, string url, string status, string detail)
        {
            if (taskGen != Volatile.Read(ref _uploadGeneration))
                return;

            _outUrl = url ?? "";
            _outStatus = status ?? "";
            _outMessage = detail ?? "";

            if (string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(url))
            {
                TryCopyLink(url);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Xeokit link on U (clipboard if allowed).");
            }
            else if (!string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, detail ?? status ?? "Upload failed.");
            }

            Interlocked.Exchange(ref _pendingOutputRefresh, 1);
            var doc = OnPingDocument();
            doc?.ScheduleSolution(5, _ => ExpireSolution(false));
        }

        private static string NormalizeBaseUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";
            s = s.Trim().TrimEnd('/');
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = "https://" + s;
            return s;
        }

        private static string ResolveLocalGlbPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var expanded = input.Trim();
            if (expanded.StartsWith("~/", StringComparison.Ordinal) || expanded == "~")
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                expanded = expanded.Length <= 1
                    ? home
                    : Path.Combine(home, expanded.Substring(2));
            }

            var full = Path.GetFullPath(expanded);
            var endsWithSep = full.EndsWith(Path.DirectorySeparatorChar)
                || full.EndsWith(Path.AltDirectorySeparatorChar);

            if (endsWithSep || Directory.Exists(full))
            {
                var dir = endsWithSep ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
                var name = $"gia_xeokit_{DateTime.Now:yyyyMMdd_HHmmss}.glb";
                return Path.Combine(dir, name);
            }

            if (string.IsNullOrEmpty(Path.GetExtension(full)))
                full = Path.ChangeExtension(full, ".glb");

            return full;
        }

        private static void TryCopyLink(string url)
        {
            try
            {
                if (Clipboard.Instance != null)
                    Clipboard.Instance.Text = url;
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
