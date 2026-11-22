using Newtonsoft.Json;


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
            if (a == null) return new XYZModel(b.X, b.Y, b.Z);
            if (b == null) return new XYZModel(a.X, a.Y, a.Z);
            return new XYZModel(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static XYZModel operator -(XYZModel a, XYZModel b)
        {
            if (a == null && b == null) return new XYZModel(0, 0, 0);
            if (a == null) return new XYZModel(-b.X, -b.Y, -b.Z);
            if (b == null) return new XYZModel(a.X, a.Y, a.Z);
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

        public bool IsValid => !double.IsNaN(X) && !double.IsInfinity(X) &&
                               !double.IsNaN(Y) && !double.IsInfinity(Y) &&
                               !double.IsNaN(Z) && !double.IsInfinity(Z);

        public static bool IsOffsetEqual(XYZModel? a, XYZModel? b, double tolerance = 1e-3)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (!a.IsValid || !b.IsValid) return false;
            return System.Math.Abs(a.X - b.X) <= tolerance &&
                   System.Math.Abs(a.Y - b.Y) <= tolerance &&
                   System.Math.Abs(a.Z - b.Z) <= tolerance;
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
