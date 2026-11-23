using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Synthetic.Platform.Revit;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.RevitDOM.Translation
{
    public static class ElementModelExtensions
    {
        public static void Populate(this ElementModel model, Element elem, bool isTemplate)
        {
            model.Populate(elem, isTemplate, null);
        }

        internal static void Populate(this ElementModel model, Element elem, bool isTemplate, IIdentityService? identityService)
        {
            if (elem == null) return;
            var resolvedIdentity = identityService ?? new RevitIdentityService();

            model.Element = elem;
            model.Document = elem.Document;
            model.ElementId = resolvedIdentity.ToModel(elem.Id, elem.Document, isTemplate);
            model.Class = elem.GetType().FullName ?? string.Empty;
            model.Name = elem.Name;

            long idVal;
#if REVIT2023
            idVal = elem.Id.IntegerValue;
#else
            idVal = elem.Id.Value;
#endif
            model.Id = idVal;
            model.UniqueId = elem.UniqueId;
            model.IsTemplate = isTemplate;

            if (elem.Category != null)
            {
                model.Category = elem.Category.Name;
            }

            if (model is ElementTypeModel elemTypeModel && elem is ElementType elemType)
            {
                elemTypeModel.FamilyName = elemType.FamilyName ?? string.Empty;
                if (elemType is ViewFamilyType vft)
                {
                    elemTypeModel.ViewFamily = vft.ViewFamily.ToString();
                }
            }

            ParameterEngine.ExtractParameters(elem, model, isTemplate, resolvedIdentity);
        }

        public static void Populate(this ElementTypeModel model, ElementType elemType, bool isTemplate)
        {
            model.Populate((Element)elemType, isTemplate, null);
            model.ElementType = elemType;
        }

        public static Element GetRevitElem(this ElementModel model, Document doc, IIdentityService identityService)
        {
            return identityService.ResolveElement(model.ElementId, doc);
        }

        public static Element GetRevitElem(this ElementModel model, Document doc)
        {
            return model.GetRevitElem(doc, new RevitIdentityService());
        }

        public static List<Element> GetAliasElements_Revit(this ElementModel model, Document doc)
        {
            List<Element> elements = new List<Element>();
            if (model.Aliases != null)
            {
                foreach (string aliasName in model.Aliases)
                {
                    Assembly assembly = typeof(Element).Assembly;
                    Type? elemClass = assembly.GetType(model.Class);
                    if (elemClass != null)
                    {
                        Element? elem = Select.ElementByNameClass(aliasName, elemClass, doc);
                        if (elem != null)
                        {
                            elements.Add(elem);
                        }
                    }
                }
            }
            return elements;
        }

        public static ElementModel ToModel(this Element revitElement, bool isTemplate)
        {
            if (revitElement == null) return null!;

            if (revitElement is FilledRegionType fillRegionType)
            {
                return fillRegionType.ToModel(isTemplate);
            }
            if (revitElement is Material material)
            {
                return material.ToModel(revitElement.Document, isTemplate);
            }
            if (revitElement is DimensionType dimType)
            {
                if (dimType.Name != DimensionTypeModel.InternalDimStyleName)
                {
                    return dimType.ToModel(isTemplate);
                }
                return null!;
            }
            if (revitElement is CurtainSystemType curtainSystemType)
            {
                return curtainSystemType.ToModel(isTemplate);
            }
            if (revitElement is HostObjAttributes hostObjType)
            {
                return hostObjType.ToModel(isTemplate);
            }
            if (revitElement is View view)
            {
                if (view.IsTemplate)
                {
                    return view.ToTemplateModel(isTemplate);
                }
                else
                {
                    return view.ToModel(isTemplate);
                }
            }
            if (revitElement is GridType gridType)
            {
                return gridType.ToModel(isTemplate);
            }
            if (revitElement is LevelType levelType)
            {
                return levelType.ToModel(isTemplate);
            }
            if (revitElement is MullionType mullionType)
            {
                return mullionType.ToModel(isTemplate);
            }
            if (revitElement is FasciaType fasciaType)
            {
                return fasciaType.ToModel(isTemplate);
            }
            if (revitElement is GutterType gutterType)
            {
                return gutterType.ToModel(isTemplate);
            }
            if (revitElement is ViewFamilyType viewFamilyType)
            {
                return viewFamilyType.ToModel(isTemplate);
            }
            if (revitElement is ElementType elemType)
            {
                return RevitDomExtensions.ToModel(elemType, isTemplate);
            }
            if (revitElement is FillPatternElement fillPatternElement)
            {
                return fillPatternElement.ToModel(isTemplate);
            }
            if (revitElement is LinePatternElement linePatternElement)
            {
                return linePatternElement.ToModel(isTemplate);
            }
            if (revitElement is PropertySetElement propertySetElement)
            {
                return propertySetElement.ToModel(isTemplate);
            }
            if (revitElement is BrowserOrganization browserOrganization)
            {
                return browserOrganization.ToModel(isTemplate);
            }
            if (revitElement is ParameterElement parameterElement)
            {
                return parameterElement.ToModel(isTemplate);
            }
            if (revitElement is ParameterFilterElement parameterFilterElement)
            {
                return parameterFilterElement.ToModel(revitElement.Document);
            }

            var serializeElement = new ElementModel();
            serializeElement.Populate(revitElement, isTemplate);
            return serializeElement;
        }
    }
}
