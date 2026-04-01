using System.Collections.Generic;
using System.Numerics;
using GIAViewer.Models;

namespace GIAViewer.Helpers
{
    /// <summary>
    /// Exports scene data for the <b>xeokit</b> web viewer (<c>xeokit-viewer/</c>).
    /// Currently writes the same binary GLB as <see cref="GlbExporter"/>; xeokit loads it via <c>GLTFLoaderPlugin</c>.
    /// A future path could emit XKT via an external converter (e.g. convert2xkt) without changing Grasshopper graph wiring.
    /// </summary>
    internal static class XeokitSceneExporter
    {
        public static void ExportGlb(
            string path,
            Dictionary<string, GiaMeshDefinition> meshById,
            IReadOnlyList<(string meshId, Matrix4x4 matrix)> placements)
        {
            GlbExporter.Export(path, meshById, placements);
        }
    }
}
