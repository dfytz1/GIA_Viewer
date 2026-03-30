using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace GIAViewer
{
    public class GIAViewerInfo : GH_AssemblyInfo
    {
        public override string Name => "GIA Viewer";

        public override Bitmap Icon => null;

        public override string Description =>
            "Publish Rhino/Grasshopper meshes as GLB and open them in the GIA web viewer.";

        public override Guid Id => new Guid("a3f8c2e1-9b4d-4f6a-8c1e-2d7e9f0a1b2c");

        public override string AuthorName => "GIA Viewer";

        public override string AuthorContact => "";

        public override string Version => "1.0.0";
    }
}
