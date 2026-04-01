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
    public class PublishModelComponent : GH_Component
    {
        private string _outUrl = "";
        private string _outStatus = "Idle";
        private string _outMessage = "";

        /// <summary>Incremented when uploads are invalidated (P off) or a new upload starts; completed tasks compare their id.</summary>
        private int _uploadGeneration;

        /// <summary>
        /// After a background upload finishes, we expire the component so outputs propagate. Without this flag, the next
        /// solve would start another upload (infinite loop). When 1, iteration 0 only pushes _out* fields and skips work.
        /// </summary>
        private int _pendingOutputRefresh;

        private CancellationTokenSource _uploadCts;

        public PublishModelComponent()
            : base("Publish Model", "Publish", "Build GLB, upload to R2 via API, return viewer link.", "GIA Viewer", "Web")
        {
        }

        public override Guid ComponentGuid => new Guid("e4f5a6b7-c8d9-4012-d345-6789abcdef01");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Items",
                "I",
                "Mesh definitions (GiaMeshDefinition / Placed / Bim Mesh) **and** instances (GiaMeshInstance). Block To Bim: merge **D** + **I** here.",
                GH_ParamAccess.tree);
            pManager.AddTextParameter(
                "ApiBase",
                "A",
                "Optional; empty uses GiaDefaults.PublicViewerBase (https added if missing)",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "ViewerBase",
                "V",
                "Optional; empty uses same as ApiBase",
                GH_ParamAccess.item,
                "");
            pManager.AddBooleanParameter(
                "Publish",
                "P",
                "True = upload in background (Grasshopper stays responsive). False = skip GLB unless L is set.",
                GH_ParamAccess.item,
                false);
            pManager.AddTextParameter(
                "LocalGlb",
                "L",
                "Full path to a .glb file (e.g. ~/Downloads/model.glb), not a folder. Leave empty to skip.",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "StableKey",
                "K",
                "Same key = overwrite same file + stable ?m= link (a-z 0-9 _ -). Empty = random id each time.",
                GH_ParamAccess.item,
                "");
            pManager.AddTextParameter(
                "UploadSecret",
                "X",
                "Optional; must match Vercel GIA_UPLOAD_SECRET if you set it.",
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
            pManager.AddTextParameter("ViewerUrl", "U", "Shareable link", GH_ParamAccess.item);
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
                    _outMessage = "Connect Bim Mesh / Bim Instance to Items.";
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
                        AddRuntimeMessage(
                            GH_RuntimeMessageLevel.Remark,
                            "Items must include GiaMeshDefinition data (mesh geometry), not only GiaMeshInstance transforms. "
                            + "For Block To Bim: merge the **D** (Definitions) and **I** (Instances) outputs into Items — connecting only **I** causes this error.");
                        _outMessage = "Add definitions: Bim Mesh, Bim Placed Mesh, Bim Curve, or Block To Bim **D** + **I** merged into Items.";
                    }
                    else
                    {
                        var needExport = publish || !string.IsNullOrWhiteSpace(localPath);
                        if (!needExport)
                        {
                            _outStatus = "Idle";
                            _outMessage = "Publish off: set P=true to upload (background) or L for local GLB only.";
                            goto DoneIter0;
                        }

                        if (publish)
                        {
                            if (string.IsNullOrWhiteSpace(apiBase))
                                apiBase = GiaDefaults.PublicViewerBase;
                            if (string.IsNullOrWhiteSpace(viewerBase))
                                viewerBase = apiBase;
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

                        var tempGlb = Path.Combine(Path.GetTempPath(), $"gia_{Guid.NewGuid():N}.glb");
                        try
                        {
                            GlbExporter.Export(tempGlb, meshById, placements, (float)geometryScale);
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
                                        "Local save failed: " + ex.Message
                                            + " (use a full file path like ~/Downloads/my.glb; macOS may block writes to some folders.)");
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
                        _outMessage = "Upload running in background; canvas stays responsive. Outputs refresh when done.";

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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Link on output U (and clipboard if allowed).");
            }
            else if (!string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, detail ?? status ?? "Upload failed.");
            }

            Interlocked.Exchange(ref _pendingOutputRefresh, 1);
            var doc = OnPingDocument();
            doc?.ScheduleSolution(5, ScheduleExpireAfterUpload);
        }

        private void ScheduleExpireAfterUpload(GH_Document document)
        {
            ExpireSolution(false);
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
                var name = $"gia_export_{DateTime.Now:yyyyMMdd_HHmmss}.glb";
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
