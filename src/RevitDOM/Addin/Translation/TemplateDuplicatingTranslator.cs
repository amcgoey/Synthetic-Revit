using System;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;

namespace Synthetic.RevitDOM.Translation
{
    internal abstract class TemplateDuplicatingTranslator<TRev, TModel> : IModelTranslator<TRev, TModel>
        where TRev : ElementType
        where TModel : ElementModel
    {
        public virtual void ExtractSpecifics(TRev revitElement, TModel model, Document doc)
        {
            // Empty. ParameterEngine handles standard parameters.
        }

        public virtual TRev? InjectSpecifics(TModel model, TRev? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            Type targetType = typeof(TRev);
            if (targetType == typeof(ElementType) && !string.IsNullOrEmpty(model.Class))
            {
                var resolvedType = Synthetic.Platform.Revit.Select.RevitClassByString(model.Class);
                if (resolvedType != null)
                {
                    targetType = resolvedType;
                }
            }

            var candidates = new FilteredElementCollector(doc)
                .OfClass(targetType)
                .Cast<TRev>()
                .ToList();

            TRev? template = null;
            if (model is ElementTypeModel etModel && !string.IsNullOrEmpty(etModel.FamilyName))
            {
                template = candidates.FirstOrDefault(c => string.Equals(c.FamilyName, etModel.FamilyName, StringComparison.OrdinalIgnoreCase));
                if (template == null)
                {
                    throw new InvalidOperationException($"No template element of type '{targetType.Name}' with FamilyName '{etModel.FamilyName}' found in the document to duplicate for '{model.Name}'.");
                }
            }
            else
            {
                template = candidates.FirstOrDefault();
            }

            if (template == null)
            {
                throw new InvalidOperationException($"No template element of type '{targetType.Name}' found in the document to duplicate for '{model.Name}'.");
            }

            var duplicated = template.Duplicate(model.Name) as TRev;
            if (duplicated == null)
            {
                throw new InvalidOperationException($"Failed to duplicate '{targetType.Name}' template to '{model.Name}'.");
            }

            return duplicated;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            if (revitElement is TRev typedElement)
            {
                ExtractSpecifics(typedElement, (TModel)model, doc);
            }
            else
            {
                throw new NotSupportedException($"Cannot extract element of type '{revitElement.GetType().FullName}' using translator '{this.GetType().Name}' which expects '{typeof(TRev).FullName}'.");
            }
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            if (revitElement != null && !(revitElement is TRev))
            {
                throw new NotSupportedException($"Cannot inject element of type '{revitElement.GetType().FullName}' using translator '{this.GetType().Name}' which expects '{typeof(TRev).FullName}'.");
            }
            return InjectSpecifics((TModel)model, revitElement as TRev, doc);
        }

        #endregion
    }
}
