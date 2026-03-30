using System;
using System.Collections.Generic;
using Rhino.Geometry;

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
        public Mesh RhinoMesh { get; set; }
        public GiaBimMaterial Material { get; set; }
    }

    public sealed class GiaMeshInstance
    {
        public string MeshId { get; set; } = "mesh";
        public Plane Plane { get; set; } = Plane.WorldXY;
    }

    public static class GiaObjectHelper
    {
        public static GiaBimMaterial AsMaterial(object obj)
        {
            if (obj is GiaBimMaterial m) return m;
            if (obj is Grasshopper.Kernel.Types.GH_ObjectWrapper ow && ow.Value is GiaBimMaterial mm) return mm;
            return null;
        }

        public static GiaMeshDefinition AsMeshDef(object obj)
        {
            if (obj is GiaMeshDefinition d) return d;
            if (obj is Grasshopper.Kernel.Types.GH_ObjectWrapper ow && ow.Value is GiaMeshDefinition dd) return dd;
            return null;
        }

        public static GiaMeshInstance AsInstance(object obj)
        {
            if (obj is GiaMeshInstance i) return i;
            if (obj is Grasshopper.Kernel.Types.GH_ObjectWrapper ow && ow.Value is GiaMeshInstance ii) return ii;
            return null;
        }

        public static void CollectFromTree(
            IList<object> flat,
            Dictionary<string, GiaMeshDefinition> meshById,
            List<GiaMeshInstance> instances)
        {
            foreach (var o in flat)
            {
                var md = AsMeshDef(o);
                if (md != null && md.RhinoMesh != null && md.RhinoMesh.IsValid)
                {
                    meshById[md.MeshId] = md;
                }

                var inst = AsInstance(o);
                if (inst != null)
                {
                    instances.Add(inst);
                }
            }
        }
    }
}
