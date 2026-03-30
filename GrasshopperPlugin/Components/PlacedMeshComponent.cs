using System;
using System.Drawing;
using GIAViewer.Helpers;
using GIAViewer.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace GIAViewer.Components
{
    /// <summary>
    /// Combines a <see cref="GiaMeshDefinition"/> with a transform so Publish does not need a separate template mesh on the canvas.
    /// </summary>
    public class PlacedMeshComponent : GH_Component
    {
        public PlacedMeshComponent()
            : base("Bim Placed Mesh", "PlcMesh", "GiaMeshDefinition + transform + optional MeshId override for export.", "GIA Viewer", "Data")
        {
        }

        public override Guid ComponentGuid => new Guid("f1a2b3c4-d5e6-4789-a012-3456789abcde");

        protected override Bitmap Icon => null;

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Name",
                "N",
                "Optional; empty = definition id, or auto id if that is also empty",
                GH_ParamAccess.item,
                "");
            pManager.AddGenericParameter("Item", "I", "GiaMeshDefinition from Bim Mesh or Bim Curve", GH_ParamAccess.item);
            pManager.AddTransformParameter("Transform", "X", "World transform", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Item", "I", "Feed into Publish Model", GH_ParamAccess.item);
            pManager.AddTextParameter("MeshId", "Id", "Resolved id (for Bim Instance Ref)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var nameOverride = "";
            da.GetData(0, ref nameOverride);

            object itemObj = null;
            if (!da.GetData(1, ref itemObj))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a GiaMeshDefinition (Bim Mesh / Bim Curve).");
                return;
            }

            var def = GiaObjectHelper.AsMeshDef(itemObj);
            if (def == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Item must be GiaMeshDefinition from Bim Mesh or Bim Curve.");
                return;
            }

            if (def.RhinoMesh == null || !def.RhinoMesh.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid mesh on definition.");
                return;
            }

            var ghXf = new GH_Transform();
            if (!da.GetData(2, ref ghXf))
                return;

            string id;
            if (!string.IsNullOrWhiteSpace(nameOverride))
                id = nameOverride.Trim();
            else if (!string.IsNullOrWhiteSpace(def.MeshId))
                id = def.MeshId.Trim();
            else
                id = GiaMeshId.ResolveDefinitionId(this, da, "", "p");

            var dupDef = new GiaMeshDefinition
            {
                MeshId = id,
                RhinoMesh = def.RhinoMesh.DuplicateMesh(),
                Material = def.Material,
            };
            dupDef.RhinoMesh.Normals.ComputeNormals();
            dupDef.RhinoMesh.FaceNormals.ComputeFaceNormals();

            var placed = new GiaPlacedMesh { Definition = dupDef, Xform = ghXf.Value };
            da.SetData(0, new GH_ObjectWrapper(placed));
            da.SetData(1, id);
        }
    }
}
