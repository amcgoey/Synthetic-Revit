using Newtonsoft.Json;

namespace Synthetic.RevitDOM.Models
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
}
