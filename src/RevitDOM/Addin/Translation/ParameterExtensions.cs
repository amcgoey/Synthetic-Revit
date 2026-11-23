using System.Globalization;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    public static class ParameterExtensions
    {
        public static ParameterModel ToModel(this Parameter parameter, Document doc, bool isTemplate = false)
        {
            if (parameter == null) return null!;

            long idVal;
#if REVIT2023
            idVal = parameter.Id.IntegerValue;
#else
            idVal = parameter.Id.Value;
#endif

            var model = new ParameterModel
            {
                Name = parameter.Definition.Name,
                StorageType = parameter.StorageType.ToString(),
                IsReadOnly = parameter.IsReadOnly,
                Id = idVal,
                IsShared = parameter.IsShared,
                IsTemplate = isTemplate
            };

            if (parameter.IsShared && parameter.GUID != null)
            {
                model.GUID = parameter.GUID.ToString();
            }

            if (parameter.HasValue)
            {
                if (parameter.StorageType == StorageType.ElementId)
                {
                    model.ValueElemId = parameter.AsElementId().ToModel(doc, isTemplate);
                }
                else if (parameter.StorageType == StorageType.Integer)
                {
                    model.Value = parameter.AsInteger().ToString();
                }
                else if (parameter.StorageType == StorageType.Double)
                {
                    model.Value = parameter.AsDouble().ToString(CultureInfo.InvariantCulture);
                }
                else if (parameter.StorageType == StorageType.String)
                {
                    model.Value = parameter.AsString();
                }
            }

            return model;
        }
    }
}
