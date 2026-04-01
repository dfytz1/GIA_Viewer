using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;

namespace GIAViewer.Helpers
{
    internal static class GiaMeshId
    {
        public static string SanitizeForMeshId(string s, string fallback = "mesh")
        {
            if (string.IsNullOrWhiteSpace(s))
                return fallback;
            var sb = new StringBuilder();
            foreach (var c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            var r = sb.ToString();
            return r.Length > 48 ? r.Substring(0, 48) : r;
        }

        /// <summary>
        /// Returns trimmed <paramref name="requested"/> if non-empty; otherwise a stable id per component instance and solution iteration.
        /// </summary>
        public static string ResolveDefinitionId(
            GH_Component owner,
            IGH_DataAccess da,
            string requested,
            string prefix = "m",
            int listIndex = 0,
            int listCount = 1)
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim();

            var g = owner.InstanceGuid.ToString("N");
            if (listCount <= 1)
                return $"{prefix}_{g}_{da.Iteration}";
            return $"{prefix}_{g}_{da.Iteration}_{listIndex}";
        }

        /// <summary>Stable id per path + index when Geometry is a data tree (matches one row per brep/mesh).</summary>
        public static string ResolveDefinitionIdForTreeBranch(
            GH_Component owner,
            IGH_DataAccess da,
            string requested,
            GH_Path path,
            int indexInBranch,
            string prefix = "m")
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim();

            var g = owner.InstanceGuid.ToString("N");
            var pathToken = SanitizeForMeshId(path.ToString(), "p");
            return $"{prefix}_{g}_{da.Iteration}_{pathToken}_{indexInBranch}";
        }
    }
}
