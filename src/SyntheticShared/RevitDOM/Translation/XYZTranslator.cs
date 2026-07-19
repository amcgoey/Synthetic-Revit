using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Static translator providing bidirectional mapping between native Revit XYZ and pure XYZModel POCOs.
    /// </summary>
    public static class XYZTranslator
    {
        public static XYZModel? ToModel(this XYZ? xyz)
        {
            if (xyz == null) return null;
            return new XYZModel(xyz.X, xyz.Y, xyz.Z);
        }

        public static XYZ? ToNative(this XYZModel? model)
        {
            if (model == null) return null;
            return new XYZ(model.X, model.Y, model.Z);
        }
    }
}
