using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Translator for ViewFamilyType elements, enforcing ViewFamily-scoped seed matching and CanBeCopied guards.
    /// </summary>
    internal class ViewFamilyTypeTranslator : IModelTranslator<ViewFamilyType, ElementTypeModel>
    {
        public void ExtractSpecifics(ViewFamilyType revitElement, ElementTypeModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            model.ViewFamily = revitElement.ViewFamily.ToString();
        }

        public ViewFamilyType? InjectSpecifics(ElementTypeModel model, ViewFamilyType? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .ToList();

            List<ViewFamilyType> matchingSeeds;
            if (!string.IsNullOrWhiteSpace(model.ViewFamily))
            {
                matchingSeeds = candidates
                    .Where(v => string.Equals(v.ViewFamily.ToString(), model.ViewFamily, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (!string.IsNullOrWhiteSpace(model.FamilyName))
            {
                matchingSeeds = candidates
                    .Where(v => string.Equals(v.FamilyName, model.FamilyName, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(v.ViewFamily.ToString(), model.FamilyName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                matchingSeeds = candidates;
            }

            ViewFamilyType? seed = matchingSeeds.FirstOrDefault(v => v.CanBeCopied) ?? matchingSeeds.FirstOrDefault();
            if (seed == null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot create ViewFamilyType '{model.Name}': no seed matching ViewFamily '{model.ViewFamily ?? model.FamilyName}' found in the target document.");
            }

            if (!seed.CanBeCopied)
            {
                throw new System.InvalidOperationException(
                    $"ViewFamilyType '{model.Name}' (ViewFamily '{seed.ViewFamily}') cannot be copied or duplicated in Revit.");
            }

            try
            {
                var duplicated = seed.Duplicate(model.Name) as ViewFamilyType;
                if (duplicated == null)
                {
                    throw new System.InvalidOperationException(
                        $"Failed to duplicate ViewFamilyType '{model.Name}' (ViewFamily '{seed.ViewFamily}').");
                }
                return duplicated;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.InvalidOperationException || ex is System.InvalidOperationException)
            {
                throw new System.InvalidOperationException(
                    $"Failed to duplicate ViewFamilyType '{model.Name}' (ViewFamily '{seed.ViewFamily}'): {ex.Message}", ex);
            }
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            if (revitElement is ViewFamilyType vft)
            {
                ExtractSpecifics(vft, (ElementTypeModel)model, doc);
            }
            else
            {
                throw new NotSupportedException($"Cannot extract element of type '{revitElement.GetType().FullName}' using translator '{nameof(ViewFamilyTypeTranslator)}'.");
            }
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            if (revitElement != null && !(revitElement is ViewFamilyType))
            {
                throw new NotSupportedException($"Cannot inject element of type '{revitElement.GetType().FullName}' using translator '{nameof(ViewFamilyTypeTranslator)}'.");
            }
            return InjectSpecifics((ElementTypeModel)model, revitElement as ViewFamilyType, doc);
        }

        #endregion
    }
}
