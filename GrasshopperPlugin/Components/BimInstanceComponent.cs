using System;
using System.Drawing;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    public class BimInstanceComponent : GH_Component
    {
        public BimInstanceComponent()
            : base("Bim Instance", "BimInst", "Place a registered mesh id with a plane transform.", "GIA Viewer", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("d3e4f5a6-b7c8-4901-c234-56789abcdef0");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("MeshId", "Id", "Must match Bim Mesh id", GH_ParamAccess.item, "panel");
            pManager.AddPlaneParameter("Plane", "P", "Instance transform", GH_ParamAccess.item, Plane.WorldXY);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "Feed into Publish Model", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var id = "panel";
            da.GetData(0, ref id);
            if (string.IsNullOrWhiteSpace(id))
                id = "panel";

            var plane = Plane.WorldXY;
            if (!da.GetData(1, ref plane))
                return;

            var inst = new GiaMeshInstance { MeshId = id.Trim(), Plane = plane };
            da.SetData(0, new GH_ObjectWrapper(inst));
        }
    }
}
