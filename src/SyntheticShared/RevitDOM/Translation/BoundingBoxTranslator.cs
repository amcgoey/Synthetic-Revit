using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Static translator providing bidirectional mapping between native Revit BoundingBoxXYZ and pure BoundingBoxXYZModel POCOs.
    /// </summary>
    public static class BoundingBoxTranslator
    {
        public static BoundingBoxXYZModel? ToModel(this BoundingBoxXYZ? bbox)
        {
            if (bbox == null) return null;
            return new BoundingBoxXYZModel
            {
                Min = bbox.Min.ToModel(),
                Max = bbox.Max.ToModel(),
                Transform = bbox.Transform.ToModel()
            };
        }

        public static BoundingBoxXYZ? ToNative(this BoundingBoxXYZModel? model)
        {
            if (model == null) return null;
            var bbox = new BoundingBoxXYZ();

            if (model.Min != null)
            {
                var min = model.Min.ToNative();
                if (min != null)
                {
                    bbox.Min = min;
                }
            }

            if (model.Max != null)
            {
                var max = model.Max.ToNative();
                if (max != null)
                {
                    bbox.Max = max;
                }
            }

            if (model.Transform != null)
            {
                var transform = model.Transform.ToNative();
                if (transform != null)
                {
                    bbox.Transform = transform;
                }
            }

            return bbox;
        }
    }
}
