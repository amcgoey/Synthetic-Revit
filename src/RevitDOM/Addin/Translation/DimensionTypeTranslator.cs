using System;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;

namespace Synthetic.RevitDOM.Translation
{
    internal class DimensionTypeTranslator : IModelTranslator<DimensionType, DimensionTypeModel>
    {
        public void ExtractSpecifics(DimensionType revitElement, DimensionTypeModel model, Document doc)
        {
            model.DimensionStyle = revitElement.StyleType.ToString();
            model.DimensionType = revitElement;
        }

        public DimensionType? InjectSpecifics(DimensionTypeModel model, DimensionType? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(elem => !string.Equals(elem.Name, DimensionTypeModel.InternalDimStyleName, StringComparison.Ordinal))
                .ToList();

            DimensionType? template = null;
            if (!string.IsNullOrEmpty(model.DimensionStyle))
            {
                template = candidates.FirstOrDefault(elem => string.Equals(elem.StyleType.ToString(), model.DimensionStyle, StringComparison.OrdinalIgnoreCase));
                if (template == null)
                {
                    throw new InvalidOperationException($"No template element of type 'DimensionType' with StyleType '{model.DimensionStyle}' found in the document to duplicate for '{model.Name}'.");
                }
            }
            else
            {
                template = candidates.FirstOrDefault();
                if (template == null)
                {
                    throw new InvalidOperationException($"No template element of type 'DimensionType' found in the document to duplicate for '{model.Name}'.");
                }
            }

            var duplicated = template.Duplicate(model.Name) as DimensionType;
            if (duplicated == null)
            {
                throw new InvalidOperationException($"Failed to duplicate 'DimensionType' template to '{model.Name}'.");
            }

            return duplicated;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((DimensionType)revitElement, (DimensionTypeModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((DimensionTypeModel)model, (DimensionType?)revitElement, doc);
        }

        #endregion
    }
}
