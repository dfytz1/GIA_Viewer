using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    /// <summary>
    /// Converts Grasshopper / Rhino block instances into <see cref="GiaMeshDefinition"/> +
    /// <see cref="GiaMeshInstance"/>. Reads the <b>entire</b> Blocks data tree in one pass,
    /// meshes each <b>unique block name</b> once, outputs D on paths {0}…{n-1} and I matching input paths.
    /// </summary>
    public class BlockToBimComponent : GH_Component
    {
        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        public BlockToBimComponent()
            : base(
                "Block To Bim",
                "BlkBim",
                "Block instance tree → definitions (one branch per unique block name) + instances (same tree as Blocks).",
                "GIA Viewer",
                "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("e7c2a91b-4f63-4e89-9c1d-2b8f4a6e0d10");

        protected override Bitmap Icon => null;

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (Params?.Input.Count > 1)
                Params.Input[1].Optional = true;
            if (Params?.Input.Count > 3)
                Params.Input[3].Optional = true;
        }

        private static string ModelDefLabel(ModelInstanceDefinition mid)
        {
            if (mid is null)
                return "block";
            var s = mid.Name.ToString();
            return string.IsNullOrWhiteSpace(s) ? "block" : s;
        }

        /// <summary>Logical grouping key: block name (case-insensitive).</summary>
        private static string NameKey(InstanceDefinition rhIdef, ModelInstanceDefinition mid)
        {
            if (rhIdef != null && !string.IsNullOrWhiteSpace(rhIdef.Name))
                return rhIdef.Name.Trim();
            return ModelDefLabel(mid);
        }

        private static bool SameLogicalBlock(
            InstanceDefinition rhA,
            ModelInstanceDefinition midA,
            InstanceDefinition rhB,
            ModelInstanceDefinition midB)
        {
            if (rhA != null && rhB != null)
                return rhA.Id == rhB.Id;
            if (rhA != null || rhB != null)
                return false;
            return ReferenceEquals(midA, midB);
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddParameter(
                new Param_InstanceReference(),
                "Blocks",
                "B",
                "Block instances as data tree (all branches read in one pass; flatten upstream if you want a single branch).",
                GH_ParamAccess.tree);
            pManager.AddGenericParameter(
                "Material",
                "Mat",
                "Optional Bim Material; default white if empty",
                GH_ParamAccess.item);
            pManager.AddTextParameter(
                "IdPrefix",
                "P",
                "Optional prefix for MeshId (per block definition name + id suffix)",
                GH_ParamAccess.item,
                "");
            pManager.AddGenericParameter(
                "Meshing",
                "Mp",
                "Optional; wire native Meshing Parameters (Model). Empty = document-tolerance defaults.",
                GH_ParamAccess.item);
            pManager.AddBooleanParameter(
                "JoinPerBrep",
                "Join",
                "Join brep face meshes with π weld",
                GH_ParamAccess.item,
                true);
            pManager.AddBooleanParameter(
                "UseParallel",
                "Par",
                "Mesh distinct block names in parallel (one task per unique name)",
                GH_ParamAccess.item,
                false);
            pManager.AddBooleanParameter(
                "UsePartitioner",
                "Part",
                "Use range partitioner with parallel",
                GH_ParamAccess.item,
                false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Definitions",
                "D",
                "GiaMeshDefinition tree — path {k} = k-th unique block name (merge into Publish Items)",
                GH_ParamAccess.tree);
            pManager.AddGenericParameter(
                "Instances",
                "I",
                "GiaMeshInstance tree — same paths as Blocks",
                GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var rhinoDoc = RhinoDoc.ActiveDoc;

            object matObj = null;
            da.GetData(1, ref matObj);
            var mat = GiaObjectHelper.AsMaterial(matObj) ?? GiaDefaults.CreateWhiteMaterial();

            var idPrefix = "";
            da.GetData(2, ref idPrefix);
            if (!string.IsNullOrWhiteSpace(idPrefix))
                idPrefix = idPrefix.Trim();

            object meshingObj = null;
            da.GetData(3, ref meshingObj);
            var ghMp = GiaMeshingParamUtil.AsModelMeshingParameters(meshingObj);

            var joinPerBrep = true;
            da.GetData(4, ref joinPerBrep);

            var useParallel = false;
            da.GetData(5, ref useParallel);

            var usePartitioner = false;
            da.GetData(6, ref usePartitioner);

            var brepOpts = BrepMeshingOptions.FromGrasshopper(ghMp, rhinoDoc, joinPerBrep);

            if (Params.Input[0].VolatileData is not IGH_Structure structure || structure.PathCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No block instances in Blocks.");
                da.SetDataTree(0, new DataTree<IGH_Goo>());
                da.SetDataTree(1, new DataTree<IGH_Goo>());
                return;
            }

            var rows = new List<BlockRow>();
            var uniqueOrder = new List<string>();
            var uniqueMid = new Dictionary<string, ModelInstanceDefinition>(NameComparer);
            var uniqueRh = new Dictionary<string, InstanceDefinition>(NameComparer);

            foreach (GH_Path path in structure.Paths)
            {
                var branch = structure.get_Branch(path);
                if (branch == null || branch.Count == 0)
                    continue;

                for (var j = 0; j < branch.Count; j++)
                {
                    if (branch[j] is not GH_InstanceReference gir || gir == null || !gir.IsValid)
                        continue;

                    if (gir.IsReferencedGeometry && rhinoDoc != null)
                        gir.LoadGeometry(rhinoDoc);

                    var mid = gir.InstanceDefinition;
                    if (mid == null || !mid.IsValid)
                        continue;

                    InstanceDefinition rhIdef = null;
                    mid.CastTo(out rhIdef);
                    var nk = NameKey(rhIdef, mid);

                    if (!uniqueMid.TryGetValue(nk, out var existingMid))
                    {
                        uniqueMid[nk] = mid;
                        uniqueRh[nk] = rhIdef;
                        uniqueOrder.Add(nk);
                    }
                    else
                    {
                        var existingRh = uniqueRh[nk];
                        if (!SameLogicalBlock(existingRh, existingMid, rhIdef, mid))
                            AddRuntimeMessage(
                                GH_RuntimeMessageLevel.Warning,
                                $"Block name \"{nk}\" is used by more than one definition; using geometry from the first occurrence.");
                    }

                    rows.Add(new BlockRow(path, gir, mid, rhIdef, nk));
                }
            }

            if (rows.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No block instances in Blocks.");
                da.SetDataTree(0, new DataTree<IGH_Goo>());
                da.SetDataTree(1, new DataTree<IGH_Goo>());
                return;
            }

            var meshByName = new ConcurrentDictionary<string, Mesh>(NameComparer);
            var warns = new ConcurrentBag<string>();

            void Warn(string s)
            {
                warns.Add(s);
            }

            var uniqueWork = new List<(string nameKey, ModelInstanceDefinition mid)>();
            foreach (var nk in uniqueOrder)
                uniqueWork.Add((nk, uniqueMid[nk]));

            void MeshOne((string nameKey, ModelInstanceDefinition mid) item)
            {
                var mesh = GrasshopperModelDefinitionMesher.BuildMesh(item.mid, rhinoDoc, Warn, brepOpts);
                if (mesh != null && mesh.IsValid)
                    meshByName[item.nameKey] = mesh;
            }

            var nUnique = uniqueWork.Count;
            if (useParallel && nUnique > 1)
            {
                if (usePartitioner)
                {
                    var partitioner = Partitioner.Create(0, nUnique);
                    Parallel.ForEach(
                        partitioner,
                        range =>
                        {
                            for (var i = range.Item1; i < range.Item2; i++)
                                MeshOne(uniqueWork[i]);
                        });
                }
                else
                {
                    Parallel.For(0, nUnique, i => MeshOne(uniqueWork[i]));
                }
            }
            else
            {
                foreach (var u in uniqueWork)
                    MeshOne(u);
            }

            foreach (var msg in warns)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, msg);

            var defByName = new Dictionary<string, GiaMeshDefinition>(NameComparer);
            var outDefs = new DataTree<IGH_Goo>();

            for (var k = 0; k < uniqueOrder.Count; k++)
            {
                var nk = uniqueOrder[k];
                var mid = uniqueMid[nk];
                var rhIdef = uniqueRh[nk];

                if (!meshByName.TryGetValue(nk, out var mesh) || mesh == null || !mesh.IsValid)
                {
                    var label = rhIdef != null ? rhIdef.Name : ModelDefLabel(mid);
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"Could not mesh block \"{label}\" (empty or unsupported geometry).");
                    continue;
                }

                var slug = GiaMeshId.SanitizeForMeshId(
                    rhIdef != null ? rhIdef.Name : ModelDefLabel(mid),
                    "block");
                var idToken = rhIdef != null
                    ? rhIdef.Id.ToString("N").Substring(0, 8)
                    : RuntimeHelpers.GetHashCode(mid).ToString("X8");
                var meshId = string.IsNullOrEmpty(idPrefix)
                    ? $"{slug}_{idToken}"
                    : $"{GiaMeshId.SanitizeForMeshId(idPrefix, "p")}_{slug}_{idToken}";

                var def = new GiaMeshDefinition
                {
                    MeshId = meshId,
                    RhinoMesh = mesh.DuplicateMesh(),
                    Material = mat,
                };
                defByName[nk] = def;
                outDefs.Add(new GH_ObjectWrapper(def), new GH_Path(k));
            }

            var outInst = new DataTree<IGH_Goo>();
            foreach (var row in rows)
            {
                if (!defByName.TryGetValue(row.NameKey, out var def))
                    continue;

                var iref = row.Gir.Value;
                if (iref == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Block instance has no InstanceReferenceGeometry.");
                    continue;
                }

                outInst.Add(
                    new GH_ObjectWrapper(
                        new GiaMeshInstance
                        {
                            MeshId = def.MeshId,
                            Xform = iref.Xform,
                        }),
                    row.Path);
            }

            da.SetDataTree(0, outDefs);
            da.SetDataTree(1, outInst);

            if (outDefs.DataCount == 0 && rows.Count > 0)
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No blocks converted. Use Block Instance outputs from Rhino 8 Grasshopper block components.");
        }

        private readonly struct BlockRow
        {
            public BlockRow(GH_Path path, GH_InstanceReference gir, ModelInstanceDefinition mid, InstanceDefinition rhIdef, string nameKey)
            {
                Path = path;
                Gir = gir;
                Mid = mid;
                RhIdef = rhIdef;
                NameKey = nameKey;
            }

            public GH_Path Path { get; }
            public GH_InstanceReference Gir { get; }
            public ModelInstanceDefinition Mid { get; }
            public InstanceDefinition RhIdef { get; }
            public string NameKey { get; }
        }
    }
}
