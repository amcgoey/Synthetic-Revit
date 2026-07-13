using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Static translator providing bidirectional mapping between native Revit Transform and pure TransformModel POCOs.
    /// </summary>
    public static class TransformTranslator
    {
        public static TransformModel? ToModel(this Transform? transform)
        {
            if (transform == null) return null;
            return new TransformModel
            {
                Origin = transform.Origin.ToModel(),
                BasisX = transform.BasisX.ToModel(),
                BasisY = transform.BasisY.ToModel(),
                BasisZ = transform.BasisZ.ToModel()
            };
        }

        public static Transform? ToNative(this TransformModel? model)
        {
            if (model == null) return null;
            var transform = Transform.Identity;

            if (model.Origin != null)
            {
                var origin = model.Origin.ToNative();
                if (origin != null)
                {
                    transform.Origin = origin;
                }
            }

            if (model.BasisX != null)
            {
                var basisX = model.BasisX.ToNative();
                if (basisX != null)
                {
                    transform.set_Basis(0, basisX);
                }
            }

            if (model.BasisY != null)
            {
                var basisY = model.BasisY.ToNative();
                if (basisY != null)
                {
                    transform.set_Basis(1, basisY);
                }
            }

            if (model.BasisZ != null)
            {
                var basisZ = model.BasisZ.ToNative();
                if (basisZ != null)
                {
                    transform.set_Basis(2, basisZ);
                }
            }

            return transform;
        }
    }
}
