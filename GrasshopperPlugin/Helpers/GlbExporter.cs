using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GIAViewer.Models;
using Rhino.Geometry;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace GIAViewer.Helpers
{
    internal static class GlbExporter
    {
        public static void Export(
            string path,
            Dictionary<string, GiaMeshDefinition> meshById,
            IReadOnlyList<GiaMeshInstance> instances)
        {
            if (meshById.Count == 0)
                throw new InvalidOperationException("No mesh definitions to export.");

            var scene = new SceneBuilder();

            var meshBuilders = new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>();
            foreach (var kv in meshById)
            {
                var def = kv.Value;
                var mb = VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>.CreateCompatibleMesh(
                    SanitizeName(def.MeshId));
                var rgba = ToRgba(def.Material?.Color ?? System.Drawing.Color.LightGray);
                var metallic = (float)Math.Clamp(def.Material?.Metallic ?? 0, 0, 1);
                var roughness = (float)Math.Clamp(def.Material?.Roughness ?? 0.5, 0, 1);
                var matName = SanitizeName(def.Material?.Name ?? "Material");
                var material = new MaterialBuilder(matName)
                    .WithDoubleSide(true)
                    .WithMetallicRoughnessShader()
                    .WithBaseColor(rgba)
                    .WithMetallicRoughness(metallic, roughness);

                var prim = mb.UsePrimitive(material);
                AddRhinoMesh(prim, def.RhinoMesh);
                meshBuilders[kv.Key] = mb;
            }

            var instById = instances
                .Where(i => meshBuilders.ContainsKey(i.MeshId))
                .GroupBy(i => i.MeshId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kv in meshBuilders)
            {
                var id = kv.Key;
                var mb = kv.Value;
                if (instById.TryGetValue(id, out var list) && list.Count > 0)
                {
                    foreach (var ins in list)
                    {
                        var m = PlaneToMatrix(ins.Plane);
                        scene.AddRigidMesh(mb, m);
                    }
                }
                else
                {
                    scene.AddRigidMesh(mb, Matrix4x4.Identity);
                }
            }

            var model = scene.ToGltf2();
            model.SaveGLB(path);
        }

        private static string SanitizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "item";
            var t = s.Trim();
            return t.Length > 64 ? t.Substring(0, 64) : t;
        }

        private static Vector4 ToRgba(System.Drawing.Color c)
        {
            return new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
        }

        private static Matrix4x4 PlaneToMatrix(Rhino.Geometry.Plane plane)
        {
            var xf = Transform.PlaneToPlane(Rhino.Geometry.Plane.WorldXY, plane);
            return new Matrix4x4(
                (float)xf.M00, (float)xf.M01, (float)xf.M02, (float)xf.M03,
                (float)xf.M10, (float)xf.M11, (float)xf.M12, (float)xf.M13,
                (float)xf.M20, (float)xf.M21, (float)xf.M22, (float)xf.M23,
                (float)xf.M30, (float)xf.M31, (float)xf.M32, (float)xf.M33);
        }

        private static void AddRhinoMesh(
            PrimitiveBuilder<MaterialBuilder, VertexPositionNormal, VertexEmpty, VertexEmpty> prim,
            Mesh mesh)
        {
            var normals = mesh.Normals;
            var verts = mesh.Vertices;
            var hasVn = normals.Count == verts.Count;
            if (!hasVn)
            {
                mesh.FaceNormals.ComputeFaceNormals();
            }

            for (var fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var f = mesh.Faces[fi];
                var fn = mesh.FaceNormals[fi];

                void Tri(int a, int b, int c)
                {
                    var pa = ToPos(verts[a]);
                    var pb = ToPos(verts[b]);
                    var pc = ToPos(verts[c]);
                    var na = hasVn ? ToN(normals[a]) : ToN(fn);
                    var nb = hasVn ? ToN(normals[b]) : ToN(fn);
                    var nc = hasVn ? ToN(normals[c]) : ToN(fn);
                    prim.AddTriangle(
                        new VertexPositionNormal(pa, na),
                        new VertexPositionNormal(pb, nb),
                        new VertexPositionNormal(pc, nc));
                }

                if (f.IsTriangle)
                {
                    Tri(f.A, f.B, f.C);
                }
                else if (f.IsQuad)
                {
                    Tri(f.A, f.B, f.C);
                    Tri(f.A, f.C, f.D);
                }
            }
        }

        private static Vector3 ToPos(Point3f p) => new Vector3(p.X, p.Y, p.Z);

        private static Vector3 ToN(Vector3f n) => new Vector3(n.X, n.Y, n.Z);
    }
}
