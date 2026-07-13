using Newtonsoft.Json;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Pure state container for Transform coordinate transformation data, completely free of Revit API dependencies.
    /// </summary>
    public class TransformModel : ObjectModel
    {
        public XYZModel? Origin { get; set; }
        public XYZModel? BasisX { get; set; }
        public XYZModel? BasisY { get; set; }
        public XYZModel? BasisZ { get; set; }

        public TransformModel() : base() { }

        public static TransformModel? ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<TransformModel>(JSON);
        }

        public static string ToJSON(TransformModel model)
        {
            return JsonConvert.SerializeObject(model, Formatting.Indented);
        }
    }
}
