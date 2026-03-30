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
