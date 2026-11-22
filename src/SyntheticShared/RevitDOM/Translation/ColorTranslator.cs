using System;
using Autodesk.Revit.DB;
using Color = Autodesk.Revit.DB.Color;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    public static class ColorTranslator
    {
        public static ColorModel ToModel(this Color color)
        {
            if (color == null) return null;
            if (!color.IsValid) return new ColorModel() { IsValid = false };
            return new ColorModel(color.Red, color.Green, color.Blue) { IsValid = true };
        }

        public static Color ToColor(this ColorModel model)
        {
            if (model == null) return null;
            if (model.IsValid)
            {
                return new Color(model.Red, model.Green, model.Blue);
            }
            return Color.InvalidColorValue;
        }
    }
}
