using System;
using System.Drawing;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace GIAViewer.Components
{
    public class BimMaterialComponent : GH_Component
    {
        public BimMaterialComponent()
            : base("Bim Material", "BimMat", "PBR base color, metallic, and roughness.", "GIA Viewer", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("b1c2d3e4-f5a6-4789-a012-3456789abcde");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddColourParameter(
                "Color",
                "C",
                "RGBA; non-opaque alpha exports as blended PBR in GLB",
                GH_ParamAccess.item,
                Color.LightGray);
            pManager.AddNumberParameter("Metallic", "M", "0–1", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Roughness", "R", "0–1", GH_ParamAccess.item, 0.5);
            pManager.AddTextParameter("Name", "N", "Material name", GH_ParamAccess.item, "Material");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Material", "M", "Connect to Bim Mesh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            GH_Colour ghCol = null;
            var m = 0.0;
            var r = 0.5;
            var name = "Material";
            if (!da.GetData(0, ref ghCol) || ghCol == null) return;
            var c = ghCol.Value;
            da.GetData(1, ref m);
            da.GetData(2, ref r);
            da.GetData(3, ref name);

            var mat = new GiaBimMaterial
            {
                Color = c,
                Metallic = Math.Max(0, Math.Min(1, m)),
                Roughness = Math.Max(0, Math.Min(1, r)),
                Name = string.IsNullOrWhiteSpace(name) ? "Material" : name.Trim(),
            };

            da.SetData(0, new GH_ObjectWrapper(mat));
        }
    }
}
