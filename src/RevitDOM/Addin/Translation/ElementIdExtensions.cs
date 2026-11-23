using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    public static class ElementIdExtensions
    {
        public static ElementIdModel ToModel(this ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null!;

            long idVal;
#if REVIT2023
            idVal = id.IntegerValue;
#else
            idVal = id.Value;
#endif

            if (id == LinePatternElement.GetSolidPatternId())
            {
                return new ElementIdModel
                {
                    Name = "Solid",
                    Class = "Autodesk.Revit.DB.LinePatternElement",
                    UniqueId = "",
                    Category = "",
                    Id = idVal,
                    IsTemplate = isTemplate
                };
            }

            var model = new ElementIdModel
            {
                Id = idVal,
                IsTemplate = isTemplate
            };

            if (doc != null)
            {
                Element elem = doc.GetElement(id);
                if (elem != null)
                {
                    model.Name = elem.Name;
                    model.Class = elem.GetType().FullName ?? string.Empty;
                    model.UniqueId = elem.UniqueId;

                    Category cat = elem.Category;
                    if (cat != null)
                    {
                        model.Category = cat.Name;
                    }
                }
            }

            return model;
        }

        public static ElementId ToElementId(this ElementIdModel model)
        {
#if REVIT2023
            return new ElementId((int)model.Id);
#else
            return new ElementId(model.Id);
#endif
        }
    }
}
