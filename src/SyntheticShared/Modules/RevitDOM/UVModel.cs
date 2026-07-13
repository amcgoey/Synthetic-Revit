using Newtonsoft.Json;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Pure state container for UV coordinate data, completely free of Revit API dependencies.
    /// </summary>
    public class UVModel : ObjectModel
    {
        public double U { get; set; }
        public double V { get; set; }

        public UVModel() : base() { }

        public UVModel(double u, double v) : base()
        {
            U = u;
            V = v;
        }

        public static UVModel? ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<UVModel>(JSON);
        }

        public static string ToJSON(UVModel model)
        {
            return JsonConvert.SerializeObject(model, Formatting.Indented);
        }
    }

    public static class UVExtensions
    {
        public static UVModel ToModel(this Autodesk.Revit.DB.UV uv)
        {
            if (uv == null) return null;
            return new UVModel { U = uv.U, V = uv.V };
        }

        public static Autodesk.Revit.DB.UV ToUV(this UVModel model)
        {
            if (model == null) return null;
            return new Autodesk.Revit.DB.UV(model.U, model.V);
        }
    }
}
