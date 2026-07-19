using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Synthetic.Shared.RevitAPI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    /// <summary>
    /// Production implementation of IIdentityService that interacts with a live Revit Document.
    /// </summary>
    internal class RevitIdentityService : IIdentityService
    {
        public ElementIdModel ToModel(ElementId id, Document doc, bool isTemplate = false)
        {
            if (id == null) return null;

            long idVal;
#if REVIT2022 || REVIT2023
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

        public ElementId ResolveElementId(ElementIdModel model, Document doc)
        {
            if (model == null) return ElementId.InvalidElementId;
            if (model.Id == -3000010 || string.Equals(model.Name, "Solid", StringComparison.OrdinalIgnoreCase))
            {
                return LinePatternElement.GetSolidPatternId();
            }

            // Enum-First Identity Strategy: Intercept Built-In parameter and category enums (ADR 015 / ADR 016)
            var builtInId = InterceptBuiltIn(model);
            if (builtInId != null)
            {
                return builtInId;
            }

            var elem = ResolveElement(model, doc);
            return elem?.Id ?? ElementId.InvalidElementId;
        }

        public Element ResolveElement(ElementIdModel model, Document doc)
        {
            if (model == null || doc == null) return null;

            Element elem = null;

            // Step 1: UniqueId search
            if (!string.IsNullOrEmpty(model.UniqueId))
            {
                if (Guid.TryParse(model.UniqueId, out Guid guidValue))
                {
                    elem = SharedParameterElement.Lookup(doc, guidValue);
                }

                if (elem == null)
                {
                    elem = doc.GetElement(model.UniqueId);
                }
            }

            // Step 2: Id (integer) search
            if (elem == null && model.Id != 0)
            {
#if REVIT2022 || REVIT2023
                var id = new ElementId((int)model.Id);
#else
                var id = new ElementId(model.Id);
#endif
                elem = doc.GetElement(id);
            }

            // Step 3: Type Guard verification on any element resolved by UniqueId or Id
            if (elem != null && !string.IsNullOrEmpty(model.Class))
            {
                if (!VerifyType(elem, model.Class))
                {
                    // Type mismatch - discard to prevent false positives and fallback to name/aliases search
                    elem = null;
                }
            }

            // Step 4: Name match (within expected Revit Type)
            if (elem == null && !string.IsNullOrEmpty(model.Name) && !string.IsNullOrEmpty(model.Class))
            {
                Type expectedType = GetRevitType(model.Class);
                if (expectedType != null)
                {
                    elem = Select.ElementByNameClass(model.Name, expectedType, doc);
                }
            }

            // Step 5: Alias match (within expected Revit Type)
            if (elem == null && model.Aliases != null && model.Aliases.Count > 0 && !string.IsNullOrEmpty(model.Class))
            {
                Type expectedType = GetRevitType(model.Class);
                if (expectedType != null)
                {
                    foreach (string alias in model.Aliases)
                    {
                        if (!string.IsNullOrEmpty(alias))
                        {
                            elem = Select.ElementByNameClass(alias, expectedType, doc);
                            if (elem != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return elem;
        }

        public IEnumerable<Element> GetElementsByElementIdModels(Document doc, IEnumerable<ElementIdModel> identifiers)
        {
            var resolvedElements = new List<Element>();
            if (identifiers == null || doc == null) return resolvedElements;

            foreach (var model in identifiers)
            {
                if (model == null) continue;
                var elem = ResolveElement(model, doc);
                if (elem != null)
                {
                    resolvedElements.Add(elem);
                }
                else
                {
                    SerializationResultModel.LogWarning($"Dependency not found: {model.Name} ({model.Class})");
                }
            }

            return resolvedElements;
        }

        private ElementId? InterceptBuiltIn(ElementIdModel model)
        {
            if (TryParseBuiltIn(model.Name, out int nameVal))
            {
                return CreateElementId(nameVal);
            }

            if (TryParseBuiltIn(model.Class, out int classVal))
            {
                return CreateElementId(classVal);
            }

            return null;
        }

        private bool TryParseBuiltIn(string? str, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(str)) return false;

            // Trim and normalize prefix if present
            string cleanStr = str.Trim();
            if (cleanStr.StartsWith("BuiltInCategory.", StringComparison.OrdinalIgnoreCase))
            {
                cleanStr = cleanStr.Substring("BuiltInCategory.".Length);
            }
            else if (cleanStr.StartsWith("BuiltInParameter.", StringComparison.OrdinalIgnoreCase))
            {
                cleanStr = cleanStr.Substring("BuiltInParameter.".Length);
            }

            // Try parsing as BuiltInCategory
            if (Enum.TryParse(cleanStr, true, out BuiltInCategory bic))
            {
                value = (int)bic;
                return true;
            }

            // Try parsing as BuiltInParameter
            if (Enum.TryParse(cleanStr, true, out BuiltInParameter bip))
            {
                value = (int)bip;
                return true;
            }

            return false;
        }

        private ElementId CreateElementId(long value)
        {
#if REVIT2022 || REVIT2023
            return new ElementId((int)value);
#else
            return new ElementId(value);
#endif
        }

        private bool VerifyType(Element elem, string className)
        {
            Type expectedType = GetRevitType(className);
            if (expectedType != null)
            {
                return expectedType.IsAssignableFrom(elem.GetType());
            }

            return elem.GetType().FullName == className;
        }

        private Type GetRevitType(string className)
        {
            try
            {
                return typeof(Element).Assembly.GetType(className);
            }
            catch
            {
                return null;
            }
        }
    }
}
