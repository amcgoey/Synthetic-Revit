using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Generic parameter engine responsible for extracting and injecting standard Revit parameters on elements.
    /// Implements the "Filter on Extract" strategy to ensure lightweight, transportable templates.
    /// </summary>
    internal static class ParameterEngine
    {
        /// <summary>
        /// Extracts all standard Revit parameters from the element and populates the model's parameters collection using an injected IIdentityService.
        /// If isTemplate is true, aggressively drops read-only and empty/null parameters.
        /// </summary>
        public static void ExtractParameters(Element element, ElementModel model, bool isTemplate, IIdentityService identityService)
        {
            model.Parameters = new List<ParameterModel>();
            foreach (Parameter param in element.Parameters)
            {
                if (isTemplate)
                {
                    // "Filter on Extract": drop read-only parameters and parameters without values
                    if (param.IsReadOnly) continue;
                    if (!param.HasValue) continue;

                    // Additionally skip empty strings or invalid element IDs
                    if (param.StorageType == StorageType.String && string.IsNullOrEmpty(param.AsString())) continue;
                    if (param.StorageType == StorageType.ElementId && param.AsElementId() == ElementId.InvalidElementId) continue;
                }
                else
                {
                    // For live elements, we only extract non-read-only parameters
                    if (param.IsReadOnly) continue;
                }

                model.Parameters.Add(ToModel(param, element.Document, isTemplate, identityService));
            }
        }

        /// <summary>
        /// Legacy overload using default RevitIdentityService.
        /// </summary>
        public static void ExtractParameters(Element element, ElementModel model, bool isTemplate)
        {
            model.Parameters = new List<ParameterModel>();
            foreach (Parameter param in element.Parameters)
            {
                if (isTemplate)
                {
                    // "Filter on Extract": drop read-only parameters and parameters without values
                    if (param.IsReadOnly) continue;
                    if (!param.HasValue) continue;

                    // Additionally skip empty strings or invalid element IDs
                    if (param.StorageType == StorageType.String && string.IsNullOrEmpty(param.AsString())) continue;
                    if (param.StorageType == StorageType.ElementId && param.AsElementId() == ElementId.InvalidElementId) continue;
                }
                else
                {
                    // For live elements, we only extract non-read-only parameters
                    if (param.IsReadOnly) continue;
                }

                model.Parameters.Add(param.ToModel(element.Document, isTemplate));
            }
        }

        /// <summary>
        /// Injects the parameters defined in the model back into the Revit element using the injected IIdentityService.
        /// </summary>
        public static void InjectParameters(ElementModel model, Element element, IIdentityService identityService)
        {
            if (model.Parameters == null) return;
            foreach (var paramModel in model.Parameters)
            {
                InjectParameter(paramModel, element, identityService);
            }
        }

        /// <summary>
        /// Legacy parameter injection using default RevitIdentityService.
        /// </summary>
        public static void InjectParameters(ElementModel model, Element element)
        {
            if (model.Parameters == null) return;
            foreach (var paramModel in model.Parameters)
            {
                InjectParameter(paramModel, element);
            }
        }

        /// <summary>
        /// Injects a single parameter model's value into the target Revit element using the injected IIdentityService.
        /// </summary>
        public static void InjectParameter(ParameterModel paramModel, Element element, IIdentityService identityService)
        {
            if (paramModel.IsReadOnly) return;

            Parameter param = GetParameter(paramModel, element);
            if (param != null && !param.IsReadOnly)
            {
                try
                {
                    switch (paramModel.StorageType)
                    {
                        case "Double":
                            if (paramModel.Value != null)
                            {
                                double val = Convert.ToDouble(paramModel.Value, System.Globalization.CultureInfo.InvariantCulture);
                                param.Set(val);
                            }
                            break;
                        case "ElementId":
                            ModifyElementIdParameter(param, paramModel.ValueElemId, element.Document, identityService);
                            break;
                        case "Integer":
                            if (paramModel.Value != null)
                            {
                                int val = Convert.ToInt32(paramModel.Value);
                                param.Set(val);
                            }
                            break;
                        case "String":
                        default:
                            param.Set(paramModel.Value ?? string.Empty);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error modifying parameter '{paramModel.Name}' (ID: {paramModel.Id}) on element: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Legacy parameter injection.
        /// </summary>
        public static void InjectParameter(ParameterModel paramModel, Element element)
        {
            if (paramModel.IsReadOnly) return;

            Parameter param = GetParameter(paramModel, element);
            if (param != null && !param.IsReadOnly)
            {
                try
                {
                    switch (paramModel.StorageType)
                    {
                        case "Double":
                            if (paramModel.Value != null)
                            {
                                double val = Convert.ToDouble(paramModel.Value, System.Globalization.CultureInfo.InvariantCulture);
                                param.Set(val);
                            }
                            break;
                        case "ElementId":
                            ModifyElementIdParameter(param, paramModel.ValueElemId, element.Document);
                            break;
                        case "Integer":
                            if (paramModel.Value != null)
                            {
                                int val = Convert.ToInt32(paramModel.Value);
                                param.Set(val);
                            }
                            break;
                        case "String":
                        default:
                            param.Set(paramModel.Value ?? string.Empty);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error modifying parameter '{paramModel.Name}' (ID: {paramModel.Id}) on element: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Finds the parameter on the Revit element matching the model's description.
        /// </summary>
        private static Parameter GetParameter(ParameterModel paramModel, Element element)
        {
            Document doc = element.Document;

            // 1. Shared parameter lookup
            if (paramModel.IsShared && !string.IsNullOrEmpty(paramModel.GUID))
            {
                return element.get_Parameter(new Guid(paramModel.GUID));
            }

            // 2. BuiltInParameter lookup
            if (paramModel.Id < 0)
            {
                return element.get_Parameter((BuiltInParameter)paramModel.Id);
            }

            // 3. User parameter lookup by definition ID
            if (paramModel.Id > 0)
            {
#if REVIT2022 || REVIT2023
                ParameterElement paramElem = (ParameterElement)doc.GetElement(new ElementId((int)paramModel.Id));
#else
                ParameterElement paramElem = (ParameterElement)doc.GetElement(new ElementId(paramModel.Id));
#endif
                if (paramElem != null)
                {
                    Definition def = paramElem.GetDefinition();
                    return element.get_Parameter(def);
                }
            }

            return null;
        }

        private static ParameterModel ToModel(Parameter parameter, Document doc, bool isTemplate, IIdentityService identityService)
        {
            if (parameter == null) return null;
            
            long idVal;
#if REVIT2022 || REVIT2023
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
                    model.ValueElemId = identityService.ToModel(parameter.AsElementId(), doc, isTemplate);
                }
                else if (parameter.StorageType == StorageType.Integer)
                {
                    model.Value = parameter.AsInteger().ToString();
                }
                else if (parameter.StorageType == StorageType.Double)
                {
                    model.Value = parameter.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (parameter.StorageType == StorageType.String)
                {
                    model.Value = parameter.AsString();
                }
            }

            return model;
        }

        /// <summary>
        /// Resolves the ElementIdModel reference and applies the resolved element's Id to the parameter.
        /// </summary>
        private static bool ModifyElementIdParameter(Parameter param, ElementIdModel valModel, Document doc, IIdentityService identityService)
        {
            if (valModel == null || 
                string.IsNullOrEmpty(valModel.Name) || 
                valModel.Name == "<None>" || 
                valModel.Name == "<By Category>" || 
                valModel.Name == "Solid")
            {
                return param.Set(ElementId.InvalidElementId);
            }

            Element elem = identityService.ResolveElement(valModel, doc);
            if (elem != null)
            {
                return param.Set(elem.Id);
            }

            return false;
        }

        private static bool ModifyElementIdParameter(Parameter param, ElementIdModel valModel, Document doc)
        {
            return ModifyElementIdParameter(param, valModel, doc, new RevitIdentityService());
        }
    }
}
