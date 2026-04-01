using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;

namespace GIAViewer.Helpers
{
    internal static class GiaMeshingParamUtil
    {
        public static ModelMeshingParameters AsModelMeshingParameters(object src)
        {
            if (src == null)
                return null;
            if (src is ModelMeshingParameters m && m.IsValid)
                return m;
            if (src is GH_ObjectWrapper ow && ow.Value is ModelMeshingParameters mm && mm.IsValid)
                return mm;
            return null;
        }
    }
}
