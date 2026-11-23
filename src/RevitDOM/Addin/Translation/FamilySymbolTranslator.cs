using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Translator for loadable FamilySymbol elements, enforcing family- and category-scoped sibling duplication.
    /// </summary>
    internal class FamilySymbolTranslator : IModelTranslator<FamilySymbol, ElementTypeModel>
    {
        public void ExtractSpecifics(FamilySymbol revitElement, ElementTypeModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));

            if (string.IsNullOrEmpty(model.FamilyName))
            {
                model.FamilyName = GetSymbolFamilyName(revitElement);
            }

            if (string.IsNullOrEmpty(model.Category))
            {
                string catName = GetSymbolCategoryName(revitElement);
                if (!string.IsNullOrEmpty(catName))
                {
                    model.Category = catName;
                }
            }
        }

        public FamilySymbol? InjectSpecifics(ElementTypeModel model, FamilySymbol? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            if (!string.IsNullOrWhiteSpace(model.Category))
            {
                candidates = candidates
                    .Where(sym =>
                    {
                        string symCat = GetSymbolCategoryName(sym);
                        return string.IsNullOrEmpty(symCat) || string.Equals(symCat, model.Category, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
            }

            FamilySymbol? seed;
            if (!string.IsNullOrWhiteSpace(model.FamilyName))
            {
                seed = candidates.FirstOrDefault(sym =>
                    string.Equals(GetSymbolFamilyName(sym), model.FamilyName, StringComparison.OrdinalIgnoreCase));

                if (seed == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot create FamilySymbol '{model.Name}': parent family '{model.FamilyName}' is not loaded in the target document.");
                }
            }
            else
            {
                var distinctFamilies = candidates
                    .Select(GetSymbolFamilyName)
                    .Where(fn => !string.IsNullOrEmpty(fn))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count == 0 || distinctFamilies.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Cannot create FamilySymbol '{model.Name}': FamilyName is missing and no unambiguous parent family exists in the target document.");
                }

                seed = candidates.FirstOrDefault();
                if (seed == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot create FamilySymbol '{model.Name}': FamilyName is missing and no unambiguous parent family exists in the target document.");
                }
            }

            var duplicated = seed.Duplicate(model.Name) as FamilySymbol;
            if (duplicated == null)
            {
                throw new InvalidOperationException(
                    $"Failed to duplicate FamilySymbol '{model.Name}' from family '{GetSymbolFamilyName(seed)}'.");
            }

            return duplicated;
        }

        private static string GetSymbolCategoryName(FamilySymbol sym)
        {
            return !string.IsNullOrEmpty(sym.Category?.Name)
                ? sym.Category!.Name
                : sym.Family?.FamilyCategory?.Name ?? string.Empty;
        }

        private static string GetSymbolFamilyName(FamilySymbol sym)
        {
            return !string.IsNullOrEmpty(sym.Family?.Name)
                ? sym.Family!.Name
                : sym.FamilyName ?? string.Empty;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            if (revitElement is FamilySymbol symbol)
            {
                ExtractSpecifics(symbol, (ElementTypeModel)model, doc);
            }
            else
            {
                throw new NotSupportedException($"Cannot extract element of type '{revitElement.GetType().FullName}' using translator '{nameof(FamilySymbolTranslator)}'.");
            }
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            if (revitElement != null && !(revitElement is FamilySymbol))
            {
                throw new NotSupportedException($"Cannot inject element of type '{revitElement.GetType().FullName}' using translator '{nameof(FamilySymbolTranslator)}'.");
            }
            return InjectSpecifics((ElementTypeModel)model, revitElement as FamilySymbol, doc);
        }

        #endregion
    }
}
