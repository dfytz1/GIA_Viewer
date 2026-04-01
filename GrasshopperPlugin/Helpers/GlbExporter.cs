using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GIAViewer.Models;
using Rhino.Geometry;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// Writes vertex positions in Rhino document units (no automatic conversion to glTF meters).
    /// The viewer interprets scene distances in the same numeric space as the exported coordinates.
    /// </summary>
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

            Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>> meshBuilders;

            if (meshById.Count <= 1)
            {
                meshBuilders = new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var kv in meshById)
                    meshBuilders[kv.Key] = BuildMeshBuilderForDefinition(kv.Value);
            }
            else
            {
                var concurrent = new ConcurrentDictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>(
                    StringComparer.OrdinalIgnoreCase);
                Parallel.ForEach(meshById, kv => { concurrent[kv.Key] = BuildMeshBuilderForDefinition(kv.Value); });
                meshBuilders = concurrent.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>> hullBuilders = null;
            var hullIds = meshById.Where(kv => IsValidHullMesh(kv.Value.LodConvexHullMesh)).Select(kv => kv.Key).ToList();
            if (hullIds.Count > 0)
            {
                hullBuilders = new Dictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>(
                    StringComparer.OrdinalIgnoreCase);
                if (hullIds.Count <= 1)
                {
                    foreach (var id in hullIds)
                        hullBuilders[id] = BuildMeshBuilderForHull(meshById[id]);
                }
                else
                {
                    var ch = new ConcurrentDictionary<string, MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>>(
                        StringComparer.OrdinalIgnoreCase);
                    Parallel.ForEach(hullIds, id => { ch[id] = BuildMeshBuilderForHull(meshById[id]); });
                    hullBuilders = ch.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                }
            }

            foreach (var (meshId, matrix) in placements)
            {
                if (!meshBuilders.TryGetValue(meshId, out var mb))
                    continue;
                if (hullBuilders != null && hullBuilders.TryGetValue(meshId, out var hullMb))
                {
                    var root = new NodeBuilder("gia_lod");
                    root.LocalMatrix = matrix;
                    var nodeDetail = root.CreateNode("gia_detail");
                    var nodeHull = root.CreateNode("gia_hull");
                    scene.AddRigidMesh(mb, nodeDetail);
                    scene.AddRigidMesh(hullMb, nodeHull);
                    scene.AddNode(root);
                }
                else
                {
                    scene.AddRigidMesh(mb, matrix);
                }
            }

            var model = scene.ToGltf2();
            model.SaveGLB(path);
        }

        private static MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty> BuildMeshBuilderForDefinition(
            GiaMeshDefinition def)
        {
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
            return mb;
        }

        private static bool IsValidHullMesh(Mesh mesh)
        {
            return mesh != null && mesh.IsValid && mesh.Faces.Count > 0 && mesh.Vertices.Count >= 3;
        }

        private static MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty> BuildMeshBuilderForHull(
            GiaMeshDefinition def)
        {
            var mb = VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>.CreateCompatibleMesh(
                SanitizeName(def.MeshId + "_giaHull"));
            var rgba = ToRgba(def.Material?.Color ?? System.Drawing.Color.LightGray);
            var metallic = (float)Math.Clamp(def.Material?.Metallic ?? 0, 0, 1);
            var roughness = (float)Math.Clamp(def.Material?.Roughness ?? 0.5, 0, 1);
            var matName = SanitizeName((def.Material?.Name ?? "Material") + "_hull");
            var material = new MaterialBuilder(matName)
                .WithDoubleSide(true)
                .WithMetallicRoughnessShader()
                .WithBaseColor(rgba)
                .WithMetallicRoughness(metallic, roughness);

            if (rgba.W < 0.999f)
                material.AlphaMode = AlphaMode.BLEND;

            var prim = mb.UsePrimitive(material);
            AddRhinoMesh(prim, def.LodConvexHullMesh);
            return mb;
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
            var vCount = verts.Count;
            var hasVn = vCount > 0 && normals.Count == vCount;
            if (!hasVn)
            {
                mesh.FaceNormals.ComputeFaceNormals();
            }

            var faceCount = mesh.Faces.Count;
            if (mesh.FaceNormals.Count < faceCount)
                mesh.FaceNormals.ComputeFaceNormals();

            bool VertOk(int i) => i >= 0 && i < vCount;

            for (var fi = 0; fi < faceCount; fi++)
            {
                var f = mesh.Faces[fi];
                if (fi >= mesh.FaceNormals.Count)
                    break;
                var fn = mesh.FaceNormals[fi];

                void Tri(int a, int b, int c)
                {
                    if (!VertOk(a) || !VertOk(b) || !VertOk(c))
                        return;
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
