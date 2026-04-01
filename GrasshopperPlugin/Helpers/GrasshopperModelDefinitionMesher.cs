using System;
using System.Collections.Generic;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// Meshes Grasshopper-native <see cref="ModelInstanceDefinition"/> when
    /// <c>CastTo&lt;InstanceDefinition&gt;</c> is not available (virtual GH blocks).
    /// </summary>
    internal static class GrasshopperModelDefinitionMesher
    {
        public static Mesh BuildMesh(
            ModelInstanceDefinition mid,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            BrepMeshingOptions brepOpts = null)
        {
            if (mid == null || !mid.IsValid)
                return null;

            var opts = brepOpts ?? BrepMeshingOptions.ForDocument(toleranceDoc);

            InstanceDefinition rhIdef = null;
            if (mid.CastTo(out rhIdef) && rhIdef != null)
                return BlockDefinitionMesher.BuildMeshFromDefinition(rhIdef, toleranceDoc, warn, opts);

            var parts = new List<Mesh>();
            var defStack = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var rhStack = new HashSet<Guid>();
            CollectFromModelDefinition(mid, Transform.Identity, parts, defStack, toleranceDoc, warn, rhStack, opts);
            var mesh = BlockDefinitionMesher.CombineParts(parts, opts.DocumentAbsoluteTolerance);
            if (mesh != null)
                return mesh;

            return TryHeadlessBake(mid, toleranceDoc, warn, opts);
        }

        private static void CollectFromModelDefinition(
            ModelInstanceDefinition mid,
            Transform parentXf,
            List<Mesh> parts,
            HashSet<object> defStack,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            HashSet<Guid> rhIdefStack,
            BrepMeshingOptions brepOpts)
        {
            if (mid == null || !mid.IsValid)
                return;

            if (!defStack.Add(mid))
            {
                warn?.Invoke("Skipped nested Grasshopper block cycle.");
                return;
            }

            try
            {
                var objs = mid.Objects;
                if (objs == null)
                    return;

                foreach (var mo in objs)
                {
                    if (mo == null || !mo.IsValid)
                        continue;

                    ProcessModelObject(mo, parentXf, parts, defStack, toleranceDoc, warn, rhIdefStack, brepOpts);
                }
            }
            finally
            {
                defStack.Remove(mid);
            }
        }

        private static void ProcessModelObject(
            ModelObject mo,
            Transform xf,
            List<Mesh> parts,
            HashSet<object> defStack,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            HashSet<Guid> rhIdefStack,
            BrepMeshingOptions brepOpts)
        {
            InstanceReferenceGeometry ir = null;
            if (mo.CastTo(out ir) && ir != null)
            {
                ModelInstanceDefinition nestMid = null;
                if (mo.CastTo(out nestMid) && nestMid != null)
                {
                    CollectFromModelDefinition(nestMid, xf * ir.Xform, parts, defStack, toleranceDoc, warn, rhIdefStack, brepOpts);
                    return;
                }

                InstanceDefinition nestRh = null;
                if (mo.CastTo(out nestRh) && nestRh != null)
                {
                    BlockDefinitionMesher.CollectFromDefinition(
                        nestRh,
                        xf * ir.Xform,
                        parts,
                        toleranceDoc,
                        warn,
                        rhIdefStack,
                        brepOpts);
                    return;
                }

                BlockDefinitionMesher.AddGeometryAsMeshes(
                    ir,
                    xf,
                    parts,
                    toleranceDoc,
                    toleranceDoc,
                    warn,
                    rhIdefStack,
                    brepOpts);
                return;
            }

            if (TryCastGeometry(mo, out var geom) && geom != null)
            {
                BlockDefinitionMesher.AddGeometryAsMeshes(
                    geom,
                    xf,
                    parts,
                    toleranceDoc,
                    toleranceDoc,
                    warn,
                    rhIdefStack,
                    brepOpts);
            }
        }

        private static bool TryCastGeometry(ModelObject mo, out GeometryBase geom)
        {
            geom = null;
            if (mo.CastTo(out Mesh m) && m != null)
            {
                geom = m;
                return true;
            }

            if (mo.CastTo(out Brep brep) && brep != null)
            {
                geom = brep;
                return true;
            }

            if (mo.CastTo(out Surface srf) && srf != null)
            {
                geom = srf;
                return true;
            }

            if (mo.CastTo(out Extrusion ext) && ext != null)
            {
                geom = ext;
                return true;
            }

            if (mo.CastTo(out SubD subd) && subd != null)
            {
                geom = subd;
                return true;
            }

            if (mo.CastTo(out Curve crv) && crv != null)
            {
                geom = crv;
                return true;
            }

            return false;
        }

        private static Mesh TryHeadlessBake(
            ModelInstanceDefinition mid,
            RhinoDoc toleranceDoc,
            Action<string> warn,
            BrepMeshingOptions brepOpts)
        {
            RhinoDoc hd = null;
            try
            {
                hd = RhinoDoc.CreateHeadless(null);
                var attr = new ObjectAttributes();
                var id = Guid.Empty;
                if (!mid.BakeGeometry(hd, attr, ref id))
                {
                    warn?.Invoke("Grasshopper block BakeGeometry failed (headless).");
                    return null;
                }

                if (id != Guid.Empty)
                {
                    var robj = hd.Objects.FindId(id);
                    if (robj is InstanceObject io)
                        return BlockDefinitionMesher.BuildMeshFromDefinition(io.InstanceDefinition, hd, warn, brepOpts);
                }

                var parts = new List<Mesh>();
                var stack = new HashSet<Guid>();
                foreach (var o in hd.Objects.GetObjectList(ObjectType.AnyObject))
                {
                    if (o == null)
                        continue;
                    if (o is InstanceObject inst)
                    {
                        BlockDefinitionMesher.CollectFromDefinition(
                            inst.InstanceDefinition,
                            inst.InstanceXform,
                            parts,
                            hd,
                            warn,
                            stack,
                            brepOpts);
                    }
                    else if (o.Geometry != null)
                    {
                        BlockDefinitionMesher.AddGeometryAsMeshes(
                            o.Geometry,
                            Transform.Identity,
                            parts,
                            hd,
                            hd,
                            warn,
                            stack,
                            brepOpts);
                    }
                }

                return BlockDefinitionMesher.CombineParts(parts, brepOpts.DocumentAbsoluteTolerance);
            }
            catch (Exception ex)
            {
                warn?.Invoke("Headless block bake: " + ex.Message);
                return null;
            }
            finally
            {
                hd?.Dispose();
            }
        }
    }
}
