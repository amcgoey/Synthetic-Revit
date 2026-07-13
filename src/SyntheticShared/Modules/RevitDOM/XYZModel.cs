using Newtonsoft.Json;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Pure state container for XYZ coordinate data, completely free of Revit API dependencies.
    /// </summary>
    public class XYZModel : ObjectModel
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public XYZModel() : base() { }

        public XYZModel(double x, double y, double z) : base()
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static XYZModel? ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<XYZModel>(JSON);
        }

        public static string ToJSON(XYZModel model)
        {
            return JsonConvert.SerializeObject(model, Formatting.Indented);
        }
    }
}
