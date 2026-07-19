using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
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

        public static XYZModel operator +(XYZModel a, XYZModel b)
        {
            if (a == null && b == null) return new XYZModel(0, 0, 0);
            if (a == null) return b;
            if (b == null) return a;
            return new XYZModel(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static XYZModel operator -(XYZModel a, XYZModel b)
        {
            if (a == null && b == null) return new XYZModel(0, 0, 0);
            if (a == null) return new XYZModel(-b.X, -b.Y, -b.Z);
            if (b == null) return a;
            return new XYZModel(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static XYZModel operator *(XYZModel a, double scalar)
        {
            if (a == null) return new XYZModel(0, 0, 0);
            return new XYZModel(a.X * scalar, a.Y * scalar, a.Z * scalar);
        }

        public double GetLength()
        {
            return System.Math.Sqrt(X * X + Y * Y + Z * Z);
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
