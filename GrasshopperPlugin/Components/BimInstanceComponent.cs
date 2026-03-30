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
            : base(
                "Bim Instance",
                "BimInst",
                "Place a mesh by id and transform. Leave Id empty if Ref is a GiaMeshDefinition from Bim Mesh / Curve.",
                "GIA Viewer",
                "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("d3e4f5a6-b7c8-4901-c234-56789abcdef0");

        protected override Bitmap Icon => null;

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (Params?.Input.Count > 2)
                Params.Input[2].Optional = true;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "MeshId",
                "Id",
                "Optional if Ref is set. Must match exported mesh id.",
                GH_ParamAccess.item,
                "");
            pManager.AddTransformParameter("Transform", "X", "Instance transform in world space", GH_ParamAccess.item);
            pManager.AddGenericParameter(
                "Ref",
                "R",
                "Optional GiaMeshDefinition; supplies MeshId when Id is empty",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "Feed into Publish Model", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var id = "";
            da.GetData(0, ref id);

            var ghXf = new GH_Transform();
            if (!da.GetData(1, ref ghXf))
                return;

            object refObj = null;
            da.GetData(2, ref refObj);

            if (string.IsNullOrWhiteSpace(id))
            {
                var def = GiaObjectHelper.AsMeshDefOrPlaced(refObj);
                if (def == null || string.IsNullOrWhiteSpace(def.MeshId))
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "Set MeshId or connect Ref (GiaMeshDefinition or Bim Placed Mesh Item).");
                    return;
                }

                id = def.MeshId.Trim();
            }
            else
            {
                id = id.Trim();
            }

            var inst = new GiaMeshInstance { MeshId = id, Xform = ghXf.Value };
            da.SetData(0, new GH_ObjectWrapper(inst));
        }
    }
}
