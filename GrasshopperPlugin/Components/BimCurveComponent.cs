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

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (Params?.Input.Count > 1)
                Params.Input[1].Optional = true;
            if (Params?.Input.Count > 5)
                Params.Input[5].Optional = true;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Closed planar → cap; open/non-planar → pipe", GH_ParamAccess.item);
            pManager.AddGenericParameter(
                "Material",
                "Mat",
                "Optional Bim Material; default = white PBR if empty",
                GH_ParamAccess.item);
            pManager.AddTextParameter(
                "MeshId",
                "Id",
                "Optional; empty = auto id. Wire Id out to Bim Instance Ref.",
                GH_ParamAccess.item,
                "");
            pManager.AddNumberParameter("PipeRadius", "R", "Radius when meshed as pipe", GH_ParamAccess.item, 0.05);
            pManager.AddIntegerParameter("Segments", "S", "Pipe / mesh density", GH_ParamAccess.item, 16);
            pManager.AddMeshParameter(
                "ConvexHull",
                "H",
                "Optional LOD convex hull mesh (same material as main mesh in viewer).",
                GH_ParamAccess.item);
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
            da.GetData(1, ref matObj);
            var mat = GiaObjectHelper.AsMaterial(matObj) ?? GiaDefaults.CreateWhiteMaterial();

            var id = "";
            da.GetData(2, ref id);
            id = GiaMeshId.ResolveDefinitionId(this, da, id, "c");

            var radius = 0.05;
            da.GetData(3, ref radius);

            var segments = 16;
            da.GetData(4, ref segments);

            GH_Mesh ghHull = null;
            da.GetData(5, ref ghHull);

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
                LodConvexHullMesh = ghHull is { IsValid: true, Value: { IsValid: true } }
                    ? ghHull.Value.DuplicateMesh()
                    : null,
                Material = mat,
            };

            da.SetData(0, new GH_ObjectWrapper(def));
            da.SetData(1, id);
        }
    }
}
