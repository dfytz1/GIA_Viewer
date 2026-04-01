using System.Drawing;

namespace GIAViewer.Models
{
    public static class GiaDefaults
    {
        /// <summary>
        /// Used when Publish is true and ApiBase / ViewerBase are left empty.
        /// Canonical production app: deploy <c>viewer/</c> to the Vercel project <c>gia-viewer</c> (see <c>viewer/package.json</c> deploy script).
        /// </summary>
        public const string PublicViewerBase = "https://gia-viewer.vercel.app";

        public static GiaBimMaterial CreateWhiteMaterial()
        {
            return new GiaBimMaterial
            {
                Name = "Default white",
                Color = Color.White,
                Metallic = 0,
                Roughness = 0.5,
            };
        }
    }
}
