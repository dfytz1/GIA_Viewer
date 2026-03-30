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
            IReadOnlyList<(string meshId, Matrix4x4 matrix)> placements)
        {
            if (meshById.Count == 0)
                throw new InvalidOperationException("No mesh definitions to export.");
            if (placements == null || placements.Count == 0)
                throw new InvalidOperationException("No placements.");

            var scene = new SceneBuilder();

            var meshBuilders =
                new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>(
                    StringComparer.OrdinalIgnoreCase);
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

                if (rgba.W < 0.999f)
                    material.AlphaMode = AlphaMode.BLEND;

                var prim = mb.UsePrimitive(material);
                AddRhinoMesh(prim, def.RhinoMesh);
                meshBuilders[kv.Key] = mb;
            }

            foreach (var (meshId, matrix) in placements)
            {
                if (!meshBuilders.TryGetValue(meshId, out var mb))
                    continue;
                scene.AddRigidMesh(mb, matrix);
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
