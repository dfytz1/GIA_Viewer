using System;
using System.Collections.Generic;
using System.Linq;
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
            var skippedInvalidDefs = 0;

            foreach (var o in flat)
            {
                var placed = GiaObjectHelper.AsPlacedMesh(o);
                if (placed?.Definition != null)
                {
                    if (placed.Definition.RhinoMesh == null || !placed.Definition.RhinoMesh.IsValid)
                    {
                        skippedInvalidDefs++;
                        continue;
                    }

                    var id = placed.Definition.MeshId?.Trim() ?? "mesh";
                    if (string.IsNullOrEmpty(id)) id = "mesh";
                    meshById[id] = CloneDef(placed.Definition, id);
                    placements.Add((id, GlbTransforms.RhinoToGltf(placed.Xform)));
                    continue;
                }

                var md = GiaObjectHelper.AsMeshDef(o);
                if (md != null)
                {
                    if (md.RhinoMesh == null || !md.RhinoMesh.IsValid)
                    {
                        skippedInvalidDefs++;
                        continue;
                    }

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

            var orphanIds = new List<string>();
            for (var i = placements.Count - 1; i >= 0; i--)
            {
                var id = placements[i].meshId;
                if (!meshById.ContainsKey(id))
                    orphanIds.Add(id);
            }

            for (var i = placements.Count - 1; i >= 0; i--)
            {
                if (!meshById.ContainsKey(placements[i].meshId))
                    placements.RemoveAt(i);
            }

            if (orphanIds.Count > 0)
            {
                var distinctIds = orphanIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var shown = distinctIds.Take(4).ToList();
                var sample = string.Join(", ", shown.Select(x => "\"" + x + "\""));
                var tail = distinctIds.Count > 4 ? $", … (+{distinctIds.Count - 4} more MeshIds)" : "";
                warn?.Invoke(
                    $"{orphanIds.Count} instance placement(s) dropped: no GiaMeshDefinition for MeshId(s) {sample}{tail}. "
                    + "Include **definitions** in Items — merge Block To Bim **Definitions (D)** with **Instances (I)** into Publish.");
            }

            if (skippedInvalidDefs > 0)
                warn?.Invoke($"{skippedInvalidDefs} GiaMeshDefinition(s) skipped (Rhino mesh missing or invalid).");
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
