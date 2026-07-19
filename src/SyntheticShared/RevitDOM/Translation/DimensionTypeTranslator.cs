using System;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    internal class DimensionTypeTranslator : IModelTranslator<DimensionType, DimensionTypeModel>
    {
        public void ExtractSpecifics(DimensionType revitElement, DimensionTypeModel model, Document doc)
        {
            // Empty. ParameterEngine handles standard parameters.
        }

        public DimensionType? InjectSpecifics(DimensionTypeModel model, DimensionType? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            var template = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Where(elem => elem.StyleType.ToString().Equals(model.DimensionStyle, StringComparison.OrdinalIgnoreCase))
                .Where(elem => elem.Name != DimensionTypeModel.InternalDimStyleName)
                .FirstOrDefault();

            if (template == null)
            {
                throw new InvalidOperationException($"No template element of type 'DimensionType' with StyleType '{model.DimensionStyle}' found in the document to duplicate for '{model.Name}'.");
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
