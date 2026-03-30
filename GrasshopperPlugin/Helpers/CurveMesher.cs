using System;
using Rhino.Geometry;

namespace GIAViewer.Helpers
{
    internal static class CurveMesher
    {
        /// <summary>
        /// Closed planar curves → planar brep mesh; otherwise curve pipe.
        /// </summary>
        public static Mesh MeshFromCurve(Curve curve, double pipeRadius, int segments)
        {
            if (curve == null || !curve.IsValid)
                return null;

            curve = curve.DuplicateCurve();
            var tol = Math.Max(RhinoDocTolerance(), 0.001);

            if (curve.IsClosed && curve.IsPlanar(tol))
            {
                var breps = Brep.CreatePlanarBreps(curve, tol);
                if (breps != null && breps.Length > 0)
                {
                    var combined = new Mesh();
                    var mp = MeshingParameters.Default;
                    foreach (var b in breps)
                    {
                        var parts = Mesh.CreateFromBrep(b, mp);
                        if (parts == null) continue;
                        foreach (var m in parts)
                            combined.Append(m);
                    }

                    if (combined.Vertices.Count > 0 && combined.IsValid)
                    {
                        combined.Normals.ComputeNormals();
                        combined.FaceNormals.ComputeFaceNormals();
                        return combined;
                    }
                }
            }

            var r = Math.Max(pipeRadius, 1e-6);
            var seg = Math.Max(8, segments);
            var capSeg = Math.Max(4, seg / 2);
            var pipe = Mesh.CreateFromCurvePipe(
                curve,
                r,
                seg,
                capSeg,
                MeshPipeCapStyle.Dome,
                false,
                null);
            if (pipe != null && pipe.IsValid)
            {
                pipe.Normals.ComputeNormals();
                pipe.FaceNormals.ComputeFaceNormals();
                return pipe;
            }

            return null;
        }

        private static double RhinoDocTolerance()
        {
            try
            {
                return Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;
            }
            catch
            {
                return 0.01;
            }
        }
    }
}
