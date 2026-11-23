using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    public static class UVExtensions
    {
        public static UVModel ToModel(this Autodesk.Revit.DB.UV uv)
        {
            if (uv == null) return null!;
            return new UVModel { U = uv.U, V = uv.V };
        }

        public static Autodesk.Revit.DB.UV ToUV(this UVModel model)
        {
            if (model == null) return null!;
            return new Autodesk.Revit.DB.UV(model.U, model.V);
        }
    }
}
