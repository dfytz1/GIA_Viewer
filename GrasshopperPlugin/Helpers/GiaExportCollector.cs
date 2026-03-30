using System;
using System.Collections.Generic;
using System.Numerics;
using GIAViewer.Models;

namespace GIAViewer.Helpers
{
    internal static class GiaExportCollector
    {
        public static void Collect(
            IList<object> flat,
            Dictionary<string, GiaMeshDefinition> meshById,
            List<(string meshId, Matrix4x4 matrix)> placements,
            Action<string> warn)
        {
            meshById.Clear();
            placements.Clear();

            foreach (var o in flat)
            {
                var placed = GiaObjectHelper.AsPlacedMesh(o);
                if (placed?.Definition != null && placed.Definition.RhinoMesh != null && placed.Definition.RhinoMesh.IsValid)
                {
                    var id = placed.Definition.MeshId?.Trim() ?? "mesh";
                    if (string.IsNullOrEmpty(id)) id = "mesh";
                    meshById[id] = CloneDef(placed.Definition, id);
                    placements.Add((id, GlbTransforms.RhinoToGltf(placed.Xform)));
                    continue;
                }

                var md = GiaObjectHelper.AsMeshDef(o);
                if (md != null && md.RhinoMesh != null && md.RhinoMesh.IsValid)
                {
                    var id = md.MeshId?.Trim() ?? "mesh";
                    if (string.IsNullOrEmpty(id)) id = "mesh";
                    meshById[id] = CloneDef(md, id);
                    continue;
                }

                var inst = GiaObjectHelper.AsInstance(o);
                if (inst != null)
                {
                    var id = inst.MeshId?.Trim() ?? "mesh";
                    placements.Add((id, GlbTransforms.RhinoToGltf(inst.Xform)));
                }
            }

            foreach (var kv in meshById)
            {
                var id = kv.Key;
                var hasPlacement = false;
                foreach (var p in placements)
                {
                    if (string.Equals(p.meshId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        hasPlacement = true;
                        break;
                    }
                }

                if (!hasPlacement)
                    placements.Add((id, Matrix4x4.Identity));
            }

            for (var i = placements.Count - 1; i >= 0; i--)
            {
                var id = placements[i].meshId;
                if (!meshById.ContainsKey(id))
                {
                    placements.RemoveAt(i);
                    warn?.Invoke($"Skipped placement for unknown MeshId \"{id}\".");
                }
            }
        }

        private static GiaMeshDefinition CloneDef(GiaMeshDefinition src, string meshId)
        {
            return new GiaMeshDefinition
            {
                MeshId = meshId,
                RhinoMesh = src.RhinoMesh.DuplicateMesh(),
                Material = src.Material,
            };
        }
    }
}
