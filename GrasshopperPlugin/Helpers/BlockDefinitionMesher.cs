using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// Flattens a Rhino <see cref="InstanceDefinition"/> into a single mesh in definition space,
    /// including nested blocks.
    /// </summary>
    internal static class BlockDefinitionMesher
    {
        public static Mesh BuildMeshFromDefinition(
            InstanceDefinition idef,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            BrepMeshingOptions brepOpts = null)
        {
            if (idef == null || idef.IsDeleted)
                return null;

            var opts = brepOpts ?? BrepMeshingOptions.ForDocument(toleranceDoc);
            var parts = new List<Mesh>();
            var stack = new HashSet<Guid>();
            CollectFromDefinition(idef, Transform.Identity, parts, toleranceDoc, warn, stack, opts);
            return CombineParts(parts, opts.DocumentAbsoluteTolerance);
        }

        internal static Mesh CombineParts(List<Mesh> parts, double documentAbsoluteTolerance)
        {
            if (parts == null || parts.Count == 0)
                return null;

            var combined = new Mesh();
            foreach (var p in parts)
                combined.Append(p);

            var tol = Math.Max(documentAbsoluteTolerance, 1e-6);
            combined.Weld(tol);
            combined.Normals.ComputeNormals();
            combined.FaceNormals.ComputeFaceNormals();
            return combined.IsValid ? combined : null;
        }

        internal static void CollectFromDefinition(
            InstanceDefinition idef,
            Transform parentXf,
            List<Mesh> parts,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            HashSet<Guid> idefStack,
            BrepMeshingOptions brepOpts)
        {
            if (idef == null || idef.IsDeleted)
                return;

            var opts = brepOpts ?? BrepMeshingOptions.ForDocument(toleranceDoc);

            var id = idef.Id;
            if (idefStack.Contains(id))
            {
                warn?.Invoke($"Skipped nested block cycle: {idef.Name}");
                return;
            }

            idefStack.Add(id);
            try
            {
                foreach (var robj in idef.GetObjects())
                {
                    if (robj == null)
                        continue;

                    var ctxDoc = robj.Document ?? toleranceDoc;

                    if (robj is InstanceObject instObj)
                    {
                        var nest = instObj.InstanceDefinition;
                        var xf = parentXf * instObj.InstanceXform;
                        CollectFromDefinition(nest, xf, parts, toleranceDoc, warn, idefStack, opts);
                        continue;
                    }

                    var geom = robj.Geometry;
                    if (geom != null)
                        AddGeometryAsMeshes(geom, parentXf, parts, ctxDoc, toleranceDoc, warn, idefStack, opts);
                }
            }
            finally
            {
                idefStack.Remove(id);
            }
        }

        internal static void AddGeometryAsMeshes(
            GeometryBase geom,
            Transform xf,
            List<Mesh> parts,
            RhinoDoc ownerDocForNested,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            HashSet<Guid> idefStack,
            BrepMeshingOptions brepOpts)
        {
            var opts = brepOpts ?? BrepMeshingOptions.ForDocument(toleranceDoc);

            switch (geom)
            {
                case InstanceReferenceGeometry iref:
                {
                    InstanceDefinition nest = null;
                    if (ownerDocForNested != null)
                        nest = ownerDocForNested.InstanceDefinitions.Find(iref.ParentIdefId, true);
                    if (nest == null && toleranceDoc != null && !ReferenceEquals(ownerDocForNested, toleranceDoc))
                        nest = toleranceDoc.InstanceDefinitions.Find(iref.ParentIdefId, true);

                    if (nest != null)
                        CollectFromDefinition(nest, xf * iref.Xform, parts, toleranceDoc, warn, idefStack, opts);
                    else
                        warn?.Invoke($"Nested block definition not found: {iref.ParentIdefId}");
                    return;
                }
                case Mesh mesh:
                    AppendTransformedMesh(mesh, xf, parts);
                    return;
                case Extrusion ext:
                {
                    var b = ext.ToBrep();
                    if (b != null)
                        AppendMeshesFromBrep(b, xf, parts, warn, opts);
                    return;
                }
                case Surface srf:
                {
                    var b = Brep.CreateFromSurface(srf);
                    if (b != null)
                        AppendMeshesFromBrep(b, xf, parts, warn, opts);
                    return;
                }
                case Brep brep:
                    AppendMeshesFromBrep(brep, xf, parts, warn, opts);
                    return;
                case SubD subd:
                {
                    try
                    {
                        var m = Mesh.CreateFromSubD(subd, 2);
                        if (m != null && m.IsValid)
                            AppendTransformedMesh(m, xf, parts);
                    }
                    catch
                    {
                        warn?.Invoke("SubD meshing failed for part of a block.");
                    }

                    return;
                }
                case Curve crv:
                {
                    var absTol = opts.DocumentAbsoluteTolerance > 0
                        ? opts.DocumentAbsoluteTolerance
                        : (toleranceDoc?.ModelAbsoluteTolerance ?? 0.01);
                    var r = Math.Max(0.01, absTol * 100);
                    var m = CurveMesher.MeshFromCurve(crv, r, 16);
                    if (m != null && m.IsValid)
                        AppendTransformedMesh(m, xf, parts);
                    return;
                }
                default:
                    return;
            }
        }

        private static void AppendMeshesFromBrep(
            Brep brep,
            Transform xf,
            List<Mesh> parts,
            Action<string> warn,
            BrepMeshingOptions opts)
        {
            if (brep == null || !brep.IsValid)
                return;

            if (BrepMeshingHelper.TryMeshFromBrep(
                    brep,
                    xf,
                    opts.MeshParams,
                    opts.JoinPerBrep,
                    out var mesh,
                    warn,
                    computeNormals: false)
                && mesh != null
                && mesh.IsValid)
                parts.Add(mesh);
        }

        private static void AppendTransformedMesh(Mesh mesh, Transform xf, List<Mesh> parts)
        {
            var dup = mesh.DuplicateMesh();
            dup.Transform(xf);
            if (dup.IsValid)
                parts.Add(dup);
        }
    }
}
