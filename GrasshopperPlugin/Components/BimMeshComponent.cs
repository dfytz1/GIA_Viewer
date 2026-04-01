using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    /// <summary>
    /// Mirrors the usual GH C# script pattern: parallelize over <b>tree branches</b>, sequential breps inside each branch.
    /// </summary>
    public class BimMeshComponent : GH_Component
    {
        public BimMeshComponent()
            : base(
                "Bim Mesh",
                "BimMesh",
                "Mesh or Brep data tree + material + id. Par = parallel over branches (like Mesh Brep script), not per-brep.",
                "GIA Viewer",
                "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("c2d3e4f5-a6b7-4890-b123-456789abcdef");

        protected override Bitmap Icon => null;

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            foreach (var i in new[] { 0, 1, 3, 5, 6, 7 })
            {
                if (Params?.Input.Count > i)
                    Params.Input[i].Optional = true;
            }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter(
                "Geometry",
                "G",
                "Mesh and/or Brep as data tree (same layout as Mesh Brep script). Flat list = single branch {0}.",
                GH_ParamAccess.tree);
            pManager.AddGenericParameter(
                "Material",
                "Mat",
                "Optional Bim Material; default = white PBR if empty",
                GH_ParamAccess.item);
            pManager.AddTextParameter(
                "MeshId",
                "Id",
                "Optional; empty = auto id per path + index. Same id for all items if set (usually one branch only).",
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
                "Join brep face meshes with π weld (like GH)",
                GH_ParamAccess.item,
                true);
            pManager.AddBooleanParameter(
                "UseParallel",
                "Par",
                "Parallel over tree branches (not per brep). 4×12 tree → 4 workers, 12 breps each.",
                GH_ParamAccess.item,
                false);
            pManager.AddBooleanParameter(
                "UsePartitioner",
                "Part",
                "Use range partitioner over branch indices with Par",
                GH_ParamAccess.item,
                false);
            pManager.AddMeshParameter(
                "ConvexHull",
                "H",
                "Optional LOD convex hull mesh per Geometry item (same tree paths & branch order as G).",
                GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "GiaMeshDefinition tree — same paths as Geometry", GH_ParamAccess.tree);
            pManager.AddTextParameter("MeshId", "Id", "Resolved ids (tree)", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var param = Params.Input[0];
            if (param?.VolatileData is not IGH_Structure structure || structure.PathCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geometry (connect Mesh or Brep).");
                return;
            }

            object matObj = null;
            da.GetData(1, ref matObj);
            var mat = GiaObjectHelper.AsMaterial(matObj) ?? GiaDefaults.CreateWhiteMaterial();

            var idBase = "";
            da.GetData(2, ref idBase);

            object meshingObj = null;
            da.GetData(3, ref meshingObj);
            var ghMp = GiaMeshingParamUtil.AsModelMeshingParameters(meshingObj);

            var joinPerBrep = true;
            da.GetData(4, ref joinPerBrep);

            var useParallel = false;
            da.GetData(5, ref useParallel);

            var usePartitioner = false;
            da.GetData(6, ref usePartitioner);

            IGH_Structure hullStructure = null;
            if (Params.Input.Count > 7 && Params.Input[7].VolatileData is IGH_Structure hsHull)
                hullStructure = hsHull;

            var doc = RhinoDoc.ActiveDoc;
            var brepOpts = BrepMeshingOptions.FromGrasshopper(ghMp, doc, joinPerBrep);
            var meshParams = brepOpts.MeshParams;

            var pathList = new List<GH_Path>();
            foreach (GH_Path p in structure.Paths)
                pathList.Add(p);

            var branchCount = pathList.Count;
            if (branchCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No geometry (connect Mesh or Brep).");
                return;
            }

            var outDefs = new DataTree<IGH_Goo>();
            var outIds = new DataTree<GH_String>();
            var warns = new ConcurrentBag<string>();
            var treeLock = new object();
            var lockTree = useParallel && branchCount > 1;

            void ProcessBranch(int bi)
            {
                var path = pathList[bi];
                var branch = structure.get_Branch(path);
                if (branch == null || branch.Count == 0)
                    return;

                var localDefs = new List<IGH_Goo>();
                var localIds = new List<GH_String>();

                for (var j = 0; j < branch.Count; j++)
                {
                    if (branch[j] is not IGH_Goo goo)
                        continue;

                    if (!TryResolveMeshOrBrep(goo, out var meshIn, out var brepIn))
                    {
                        warns.Add($"Branch {path}, index {j}: not a Mesh or Brep; skipped.");
                        continue;
                    }

                    var id = GiaMeshId.ResolveDefinitionIdForTreeBranch(this, da, idBase, path, j);

                    Mesh rhMesh = null;
                    if (meshIn != null && meshIn.IsValid)
                    {
                        rhMesh = meshIn.DuplicateMesh();
                    }
                    else if (brepIn != null && brepIn.IsValid)
                    {
                        if (!BrepMeshingHelper.TryMeshFromBrep(
                                brepIn,
                                Transform.Identity,
                                meshParams,
                                joinPerBrep,
                                out rhMesh,
                                s => warns.Add($"Branch {path}, index {j}: {s}"),
                                computeNormals: false)
                            || rhMesh == null
                            || !rhMesh.IsValid)
                        {
                            warns.Add($"Branch {path}, index {j}: could not mesh Brep.");
                            continue;
                        }
                    }

                    if (rhMesh == null || !rhMesh.IsValid)
                        continue;

                    Mesh hullDup = null;
                    if (hullStructure != null && hullStructure.PathExists(path))
                    {
                        var hb = hullStructure.get_Branch(path);
                        if (hb != null && j < hb.Count && hb[j] is GH_Mesh ghh && ghh.IsValid && ghh.Value != null
                            && ghh.Value.IsValid)
                            hullDup = ghh.Value.DuplicateMesh();
                    }

                    var def = new GiaMeshDefinition
                    {
                        MeshId = id,
                        RhinoMesh = rhMesh,
                        LodConvexHullMesh = hullDup,
                        Material = mat,
                    };

                    localDefs.Add(new GH_ObjectWrapper(def));
                    localIds.Add(new GH_String(id));
                }

                void Flush()
                {
                    for (var k = 0; k < localDefs.Count; k++)
                    {
                        outDefs.Add(localDefs[k], path);
                        outIds.Add(localIds[k], path);
                    }
                }

                if (lockTree)
                {
                    lock (treeLock)
                        Flush();
                }
                else
                    Flush();
            }

            if (useParallel && branchCount > 1)
            {
                if (usePartitioner)
                {
                    var partitioner = Partitioner.Create(0, branchCount);
                    Parallel.ForEach(
                        partitioner,
                        range =>
                        {
                            for (var i = range.Item1; i < range.Item2; i++)
                                ProcessBranch(i);
                        });
                }
                else
                {
                    Parallel.For(0, branchCount, ProcessBranch);
                }
            }
            else
            {
                for (var i = 0; i < branchCount; i++)
                    ProcessBranch(i);
            }

            foreach (var w in warns)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

            if (outDefs.DataCount == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid Mesh or Brep in Geometry.");

            da.SetDataTree(0, outDefs);
            da.SetDataTree(1, outIds);
        }

        private static bool TryResolveMeshOrBrep(IGH_Goo goo, out Mesh mesh, out Brep brep)
        {
            mesh = null;
            brep = null;
            if (goo == null)
                return false;

            if (goo is GH_Mesh ghm && ghm.IsValid && ghm.Value != null)
            {
                mesh = ghm.Value;
                return true;
            }

            if (goo is GH_Brep ghb && ghb.IsValid && ghb.Value != null)
            {
                brep = ghb.Value;
                return true;
            }

            if (goo is GH_Surface ghs && ghs.IsValid && ghs.Value is Brep bFromSrf && bFromSrf.IsValid)
            {
                brep = bFromSrf;
                return true;
            }

            if (goo is GH_ObjectWrapper ow && ow.Value != null)
            {
                if (ow.Value is Mesh m && m.IsValid)
                {
                    mesh = m;
                    return true;
                }

                if (ow.Value is Brep b && b.IsValid)
                {
                    brep = b;
                    return true;
                }
            }

            return false;
        }
    }
}
