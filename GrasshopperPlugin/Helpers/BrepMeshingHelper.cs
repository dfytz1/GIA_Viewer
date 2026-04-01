using System;
using Grasshopper.Rhinoceros;
using Rhino;
using Rhino.Geometry;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// Brep → mesh with resolved <see cref="MeshingParameters"/> (no zero tolerances that force document locks on every call in parallel).
    /// </summary>
    internal static class BrepMeshingHelper
    {
        /// <summary>
        /// Build fully-resolved parameters on the calling thread (safe before parallel work).
        /// </summary>
        public static MeshingParameters ResolveMeshingParameters(MeshingParameters input, RhinoDoc doc)
        {
            var docTol = doc?.ModelAbsoluteTolerance ?? 0.01;
            if (input != null)
            {
                var p = new MeshingParameters(input);
                if (p.Tolerance <= 0)
                    p.Tolerance = docTol;
                if (p.MinimumTolerance <= 0)
                    p.MinimumTolerance = docTol * 0.1;
                return p;
            }

            var meshParams = new MeshingParameters(0.5);
            meshParams.Tolerance = docTol;
            meshParams.MinimumTolerance = docTol * 0.1;
            meshParams.SimplePlanes = true;
            meshParams.RefineGrid = true;
            return meshParams;
        }

        /// <summary>
        /// Tessellate brep; optionally join face meshes with <see cref="Math.PI"/> weld like GH mesh-brep components.
        /// </summary>
        /// <param name="computeNormals">False when meshes are merged again (e.g. blocks) to avoid duplicate heavy work.</param>
        public static bool TryMeshFromBrep(
            Brep brep,
            Transform xf,
            MeshingParameters meshParams,
            bool joinPerBrep,
            out Mesh mesh,
            Action<string> warn,
            bool computeNormals = true)
        {
            mesh = null;
            if (brep == null || !brep.IsValid)
                return false;

            Brep brepIn;
            if (xf.IsIdentity)
                brepIn = brep;
            else
            {
                brepIn = brep.DuplicateBrep();
                brepIn.Transform(xf);
            }

            var ms = Mesh.CreateFromBrep(brepIn, meshParams);
            if (ms == null || ms.Length == 0)
            {
                warn?.Invoke("Brep produced no mesh.");
                return false;
            }

            if (joinPerBrep && ms.Length > 1)
            {
                var joined = new Mesh();
                foreach (var m in ms)
                {
                    if (m != null && m.IsValid)
                        joined.Append(m);
                }

                joined.Weld(Math.PI);
                mesh = joined;
            }
            else
            {
                var combined = new Mesh();
                foreach (var m in ms)
                {
                    if (m != null && m.IsValid)
                        combined.Append(m);
                }

                var tol = Math.Max(meshParams.Tolerance, 1e-6);
                combined.Weld(tol);
                mesh = combined;
            }

            if (mesh == null || !mesh.IsValid)
                return false;

            if (computeNormals)
            {
                mesh.Normals.ComputeNormals();
                mesh.FaceNormals.ComputeFaceNormals();
            }

            return true;
        }
    }

    /// <summary>Passed through block / GH model meshing for all <see cref="Brep"/> tessellation.</summary>
    internal sealed class BrepMeshingOptions
    {
        public MeshingParameters MeshParams;
        public bool JoinPerBrep = true;

        /// <summary>Captured on the Grasshopper main thread before any parallel meshing (avoid doc reads from workers).</summary>
        public double DocumentAbsoluteTolerance = 0.01;

        public static BrepMeshingOptions ForDocument(RhinoDoc doc)
        {
            var tol = doc?.ModelAbsoluteTolerance ?? 0.01;
            return new BrepMeshingOptions
            {
                MeshParams = BrepMeshingHelper.ResolveMeshingParameters(null, doc),
                JoinPerBrep = true,
                DocumentAbsoluteTolerance = tol,
            };
        }

        /// <summary>Optional Grasshopper <see cref="ModelMeshingParameters"/> (native meshing param component).</summary>
        public static BrepMeshingOptions FromGrasshopper(
            ModelMeshingParameters ghMp,
            RhinoDoc doc,
            bool joinPerBrep)
        {
            var tol = doc?.ModelAbsoluteTolerance ?? 0.01;
            MeshingParameters rh;
            if (ghMp != null && ghMp.IsValid && ghMp.CastTo(out MeshingParameters mp) && mp != null)
                rh = BrepMeshingHelper.ResolveMeshingParameters(mp, doc);
            else
                rh = BrepMeshingHelper.ResolveMeshingParameters(null, doc);

            return new BrepMeshingOptions
            {
                MeshParams = rh,
                JoinPerBrep = joinPerBrep,
                DocumentAbsoluteTolerance = tol,
            };
        }
    }
}
