using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Eto.Forms;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace GIAViewer.Components
{
    public class PublishModelComponent : GH_Component
    {
        public PublishModelComponent()
            : base("Publish Model", "Publish", "Build GLB, upload to R2 via API, return viewer link.", "GIA Viewer", "Web")
        {
        }

        public override Guid ComponentGuid => new Guid("e4f5a6b7-c8d9-4012-d345-6789abcdef01");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Items", "I", "Bim Mesh + Bim Instance objects", GH_ParamAccess.tree);
            pManager.AddTextParameter("ApiBase", "A", "Deployed site root (e.g. https://….vercel.app)", GH_ParamAccess.item, "");
            pManager.AddTextParameter("ViewerBase", "V", "Viewer URL (often same as ApiBase)", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Publish", "P", "Trigger upload", GH_ParamAccess.item, false);
            pManager.AddTextParameter(
                "LocalGlb",
                "L",
                "Full path to a .glb file (e.g. ~/Downloads/model.glb), not a folder. Leave empty to skip.",
                GH_ParamAccess.item,
                "");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("ViewerUrl", "U", "Shareable link", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "S", "OK or Error", GH_ParamAccess.item);
            pManager.AddTextParameter("Message", "M", "Details", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var param = Params.Input[0];
            if (param?.VolatileData == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No items.");
                return;
            }

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
            var instances = new List<GiaMeshInstance>();
            GiaObjectHelper.CollectFromTree(flat, meshById, instances);

            if (meshById.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No Bim Mesh definitions.");
                da.SetData(0, "");
                da.SetData(1, "Idle");
                da.SetData(2, "Add at least one Bim Mesh.");
                return;
            }

            var apiBase = "";
            var viewerBase = "";
            var publish = false;
            var localPath = "";
            da.GetData(1, ref apiBase);
            da.GetData(2, ref viewerBase);
            da.GetData(3, ref publish);
            da.GetData(4, ref localPath);

            var tempGlb = Path.Combine(Path.GetTempPath(), $"gia_{Guid.NewGuid():N}.glb");
            try
            {
                GlbExporter.Export(tempGlb, meshById, instances);
            }
            catch (Exception ex)
            {
                da.SetData(0, "");
                da.SetData(1, "Error");
                da.SetData(2, ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
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
                da.SetData(0, "");
                da.SetData(1, "GLB ready");
                da.SetData(2, tempGlb);
                return;
            }

            if (string.IsNullOrWhiteSpace(apiBase) || string.IsNullOrWhiteSpace(viewerBase))
            {
                da.SetData(0, "");
                da.SetData(1, "Error");
                da.SetData(2, "Set ApiBase and ViewerBase for upload.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ApiBase and ViewerBase required when Publish is true.");
                return;
            }

            var (url, status, detail) = UploadClient.Publish(tempGlb, apiBase, viewerBase);
            da.SetData(0, url ?? "");
            da.SetData(1, status);
            da.SetData(2, detail);

            if (status == "OK" && !string.IsNullOrEmpty(url))
            {
                TryCopyLink(url);
            }

            try
            {
                File.Delete(tempGlb);
            }
            catch
            {
                /* ignore */
            }
        }

        /// <summary>
        /// If the user passes a directory (or a path ending in /), append a file name.
        /// File.Copy requires a file path; passing a folder causes "access denied" on macOS.
        /// </summary>
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
