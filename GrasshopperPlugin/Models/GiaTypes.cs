using System;
using Grasshopper.Kernel.Types;

namespace GIAViewer.Models
{
    public sealed class GiaBimMaterial
    {
        public string Name { get; set; } = "Material";
        public System.Drawing.Color Color { get; set; } = System.Drawing.Color.LightGray;
        public double Metallic { get; set; }
        public double Roughness { get; set; } = 0.5;
    }

    public sealed class GiaMeshDefinition
    {
        public string MeshId { get; set; } = "mesh";
        public Rhino.Geometry.Mesh RhinoMesh { get; set; }
        /// <summary>Optional convex hull (same units as main mesh). Viewer swaps to this beyond LOD distance.</summary>
        public Rhino.Geometry.Mesh LodConvexHullMesh { get; set; }
        public GiaBimMaterial Material { get; set; }
    }

    /// <summary>
    /// Definition + transform in one item (no separate template on canvas).
    /// </summary>
    public sealed class GiaPlacedMesh
    {
        public GiaMeshDefinition Definition { get; set; }
        public Rhino.Geometry.Transform Xform { get; set; } = Rhino.Geometry.Transform.Identity;
    }

    public sealed class GiaMeshInstance
    {
        public string MeshId { get; set; } = "mesh";
        public Rhino.Geometry.Transform Xform { get; set; } = Rhino.Geometry.Transform.Identity;
    }

    public static class GiaObjectHelper
    {
        public static GiaBimMaterial AsMaterial(object obj)
        {
            if (obj is GiaBimMaterial m) return m;
            if (obj is GH_ObjectWrapper ow && ow.Value is GiaBimMaterial mm) return mm;
            return null;
        }

        public static GiaMeshDefinition AsMeshDef(object obj)
        {
            if (obj is GiaMeshDefinition d) return d;
            if (obj is GH_ObjectWrapper ow && ow.Value is GiaMeshDefinition dd) return dd;
            return null;
        }

        /// <summary><see cref="GiaMeshDefinition"/> or <see cref="GiaPlacedMesh"/>.Definition from a wrapped GH object.</summary>
        public static GiaMeshDefinition AsMeshDefOrPlaced(object obj)
        {
            var d = AsMeshDef(obj);
            if (d != null) return d;
            var p = AsPlacedMesh(obj);
            return p?.Definition;
        }

        public static GiaPlacedMesh AsPlacedMesh(object obj)
        {
            if (obj is GiaPlacedMesh p) return p;
            if (obj is GH_ObjectWrapper ow && ow.Value is GiaPlacedMesh pp) return pp;
            return null;
        }

        public static GiaMeshInstance AsInstance(object obj)
        {
            if (obj is GiaMeshInstance i) return i;
            if (obj is GH_ObjectWrapper ow && ow.Value is GiaMeshInstance ii) return ii;
            return null;
        }
    }
}
