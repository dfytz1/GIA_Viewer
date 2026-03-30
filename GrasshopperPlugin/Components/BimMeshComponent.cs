using System;
using System.Drawing;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    public class BimMeshComponent : GH_Component
    {
        public BimMeshComponent()
            : base("Bim Mesh", "BimMesh", "Register a mesh template with material and id for instancing.", "GIA Viewer", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("c2d3e4f5-a6b7-4890-b123-456789abcdef");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Triangulated mesh", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "Mat", "From Bim Material", GH_ParamAccess.item);
            pManager.AddTextParameter("MeshId", "Id", "Unique id for Bim Instance", GH_ParamAccess.item, "panel");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "Feed into Publish Model", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Mesh mesh = null;
            if (!da.GetData(0, ref mesh) || mesh == null || !mesh.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid mesh.");
                return;
            }

            object matObj = null;
            if (!da.GetData(1, ref matObj))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Material required.");
                return;
            }

            var mat = GiaObjectHelper.AsMaterial(matObj);
            if (mat == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Material must come from Bim Material.");
                return;
            }

            var id = "panel";
            da.GetData(2, ref id);
            if (string.IsNullOrWhiteSpace(id))
                id = "panel";

            var dup = mesh.DuplicateMesh();
            dup.Normals.ComputeNormals();
            dup.FaceNormals.ComputeFaceNormals();

            var def = new GiaMeshDefinition
            {
                MeshId = id.Trim(),
                RhinoMesh = dup,
                Material = mat,
            };

            da.SetData(0, new GH_ObjectWrapper(def));
        }
    }
}
