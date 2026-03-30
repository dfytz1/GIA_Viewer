using System;
using System.Drawing;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    public class BimCurveComponent : GH_Component
    {
        public BimCurveComponent()
            : base("Bim Curve", "BimCrv", "Mesh a curve (planar cap or pipe) as GiaMeshDefinition for Publish.", "GIA Viewer", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("a9b8c7d6-e5f4-4321-8765-432109876543");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Closed planar → cap; open/non-planar → pipe", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "Mat", "From Bim Material", GH_ParamAccess.item);
            pManager.AddTextParameter(
                "MeshId",
                "Id",
                "Optional; empty = auto id. Wire Id out to Bim Instance Ref.",
                GH_ParamAccess.item,
                "");
            pManager.AddNumberParameter("PipeRadius", "R", "Radius when meshed as pipe", GH_ParamAccess.item, 0.05);
            pManager.AddIntegerParameter("Segments", "S", "Pipe / mesh density", GH_ParamAccess.item, 16);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "GiaMeshDefinition for Publish / Placed Mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("MeshId", "Id", "Resolved id (for Bim Instance Ref)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Curve crv = null;
            if (!da.GetData(0, ref crv) || crv == null || !crv.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Valid curve required.");
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

            var id = "";
            da.GetData(2, ref id);
            id = GiaMeshId.ResolveDefinitionId(this, da, id, "c");

            var radius = 0.05;
            da.GetData(3, ref radius);

            var segments = 16;
            da.GetData(4, ref segments);

            var mesh = CurveMesher.MeshFromCurve(crv, radius, segments);
            if (mesh == null || !mesh.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not mesh curve (try adjusting radius or segments).");
                return;
            }

            var def = new GiaMeshDefinition
            {
                MeshId = id,
                RhinoMesh = mesh,
                Material = mat,
            };

            da.SetData(0, new GH_ObjectWrapper(def));
            da.SetData(1, id);
        }
    }
}
