using Grasshopper.Kernel;

namespace GIAViewer.Helpers
{
    internal static class GiaMeshId
    {
        /// <summary>
        /// Returns trimmed <paramref name="requested"/> if non-empty; otherwise a stable id per component instance and solution iteration.
        /// </summary>
        public static string ResolveDefinitionId(
            GH_Component owner,
            IGH_DataAccess da,
            string requested,
            string prefix = "m")
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim();

            var g = owner.InstanceGuid.ToString("N");
            return $"{prefix}_{g}_{da.Iteration}";
        }
    }
}
