using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Pure state container for BoundingBoxXYZ data, completely free of Revit API dependencies.
    /// </summary>
    public class BoundingBoxXYZModel : ObjectModel
    {
        public XYZModel? Min { get; set; }
        public XYZModel? Max { get; set; }
        public TransformModel? Transform { get; set; }

        public BoundingBoxXYZModel() : base() { }

        public static BoundingBoxXYZModel? ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<BoundingBoxXYZModel>(JSON);
        }

        public static string ToJSON(BoundingBoxXYZModel model)
        {
            return JsonConvert.SerializeObject(model, Formatting.Indented);
        }
    }
}
