using System;
using System.Numerics;
using Rhino.Geometry;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// glTF / SharpGLTF use column vectors; Rhino <see cref="Transform"/> matches System.Numerics layout
    /// but .NET transforms points as row-vector × matrix. Transpose aligns AddRigidMesh with Rhino previews.
    /// </summary>
    internal static class GlbTransforms
    {
        /// <summary>
        /// SharpGLTF <c>GuardMatrix(..., IdentityColumn4)</c> requires <see cref="Matrix4x4"/> column 4 to be
        /// exactly (0,0,0,1) per glTF — strict <c>==</c> on floats. Chained Rhino block <see cref="Transform"/>s
        /// often leave ~1e-7..1e-15 in the homogeneous row; <see cref="Matrix4x4.Transpose"/> moves that into
        /// M14–M34 and publish fails. Snap affine noise after transpose.
        /// </summary>
        private static void SanitizeGltfFourthColumn(ref Matrix4x4 m)
        {
            // Loose enough for float drift after long transform chains; still << any real geometric value in col 4.
            const float eps = 1e-3f;
            if (Math.Abs(m.M14) < eps) m.M14 = 0f;
            if (Math.Abs(m.M24) < eps) m.M24 = 0f;
            if (Math.Abs(m.M34) < eps) m.M34 = 0f;
            if (Math.Abs(m.M44 - 1f) < eps) m.M44 = 1f;
        }

        public static Matrix4x4 RhinoToGltf(Transform xf)
        {
            if (!xf.IsValid)
                throw new ArgumentException("Invalid Transform (singular or corrupt).");

            var m = new Matrix4x4(
                (float)xf.M00, (float)xf.M01, (float)xf.M02, (float)xf.M03,
                (float)xf.M10, (float)xf.M11, (float)xf.M12, (float)xf.M13,
                (float)xf.M20, (float)xf.M21, (float)xf.M22, (float)xf.M23,
                (float)xf.M30, (float)xf.M31, (float)xf.M32, (float)xf.M33);

            m = Matrix4x4.Transpose(m);
            SanitizeGltfFourthColumn(ref m);

            if (!Matrix4x4.Invert(m, out _))
                throw new ArgumentException("Transform is not invertible (degenerate scale or shear). Try simplifying the transform.");

            return m;
        }

        public static Matrix4x4 PlaneToGltf(Rhino.Geometry.Plane plane)
        {
            var xf = Transform.PlaneToPlane(Rhino.Geometry.Plane.WorldXY, plane);
            return RhinoToGltf(xf);
        }
    }
}
