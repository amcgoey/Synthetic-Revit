using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Translation
{
    internal class ParameterElementTranslator : IModelTranslator<ParameterElement, ParameterElementModel>
    {
        public void ExtractSpecifics(ParameterElement revitElement, ParameterElementModel model, Document doc)
        {
            if (revitElement == null) throw new ArgumentNullException(nameof(revitElement));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            model.Categories = new List<CategoryIdModel>();

            // Find the ElementBinding associated with the ParameterElement
            ElementBinding? elementBinding = null;
            DefinitionBindingMapIterator iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                Definition def = iter.Key;
                if (def.Name == revitElement.Name)
                {
                    elementBinding = iter.Current as ElementBinding;
                    break;
                }
            }

            if (elementBinding != null)
            {
                foreach (Category cat in elementBinding.Categories)
                {
                    if (cat != null)
                    {
                        model.Categories.Add(cat.ToCategoryIdModel(doc, model.IsTemplate));
                    }
                }

                model.IsInstanceBinding = elementBinding is InstanceBinding;
                if (model.IsInstanceBinding)
                {
                    var internalDef = revitElement.GetDefinition() as InternalDefinition;
                    if (internalDef != null)
                    {
                        model.IsVaryByGroup = internalDef.VariesAcrossGroups;
                    }
                }
            }
        }

        public ParameterElement? InjectSpecifics(ParameterElementModel model, ParameterElement? revitElement, Document doc)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (revitElement != null)
            {
                return revitElement;
            }

            // Family Document Guard
            if (doc.IsFamilyDocument)
            {
                return null;
            }

            // Shared Parameter File Check
            string sharedParamsFilename = doc.Application.SharedParametersFilename;
            if (string.IsNullOrEmpty(sharedParamsFilename) || !System.IO.File.Exists(sharedParamsFilename))
            {
                SerializationResultModel.LogWarning($"Shared Parameter file is not configured or does not exist at: '{sharedParamsFilename}'.");
                return null;
            }

            // Open the Shared Parameter file
            DefinitionFile defFile = doc.Application.OpenSharedParameterFile();
            if (defFile == null)
            {
                SerializationResultModel.LogWarning("Failed to open the Shared Parameter file.");
                return null;
            }

            // Search for the parameter definition in any group
            Definition? definition = null;
            foreach (DefinitionGroup group in defFile.Groups)
            {
                definition = group.Definitions.get_Item(model.Name);
                if (definition != null)
                {
                    break;
                }
            }

            // If it does not exist, temporarily create the ExternalDefinition in the file
            if (definition == null)
            {
                DefinitionGroup group = defFile.Groups.get_Item("TemporaryGroup") ?? defFile.Groups.Create("TemporaryGroup");
                if (group == null)
                {
                    SerializationResultModel.LogWarning("Failed to open or create group 'TemporaryGroup' in Shared Parameter file.");
                    return null;
                }

                // DEFAULT baseline: Latest Revit API (2022+)
                var options = new ExternalDefinitionCreationOptions(model.Name, SpecTypeId.String.Text);
                definition = group.Definitions.Create(options);
            }

            if (definition == null)
            {
                SerializationResultModel.LogWarning($"Failed to resolve or create definition for parameter '{model.Name}'.");
                return null;
            }

            ExternalDefinition? extDef = definition as ExternalDefinition;
            if (extDef == null)
            {
                SerializationResultModel.LogWarning($"Definition for parameter '{model.Name}' is not an ExternalDefinition.");
                return null;
            }

            // Create CategorySet
            CategorySet categorySet = doc.Application.Create.NewCategorySet();
            if (model.Categories != null)
            {
                foreach (var catModel in model.Categories)
                {
                    Category cat = catModel.GetCategory(doc);
                    if (cat != null)
                    {
                        categorySet.Insert(cat);
                    }
                }
            }

            // Create Binding
            Autodesk.Revit.DB.Binding binding;
            if (model.IsInstanceBinding)
            {
                binding = doc.Application.Create.NewInstanceBinding(categorySet);
            }
            else
            {
                binding = doc.Application.Create.NewTypeBinding(categorySet);
            }

            // Execute doc.ParameterBindings.Insert() or ReInsert()
            bool inserted = doc.ParameterBindings.Insert(definition, binding);
            if (!inserted)
            {
                doc.ParameterBindings.ReInsert(definition, binding);
            }

            // Retrieve the newly bound SharedParameterElement
            SharedParameterElement? sharedParamElem = SharedParameterElement.Lookup(doc, extDef.GUID);
            if (sharedParamElem == null)
            {
                sharedParamElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(SharedParameterElement))
                    .Cast<SharedParameterElement>()
                    .FirstOrDefault(x => x.Name == model.Name);
            }

            if (sharedParamElem == null)
            {
                SerializationResultModel.LogWarning($"Failed to retrieve the bound SharedParameterElement for '{model.Name}'.");
                return null;
            }

            // If IsVaryByGroup is true, set varies across groups
            if (model.IsVaryByGroup)
            {
                InternalDefinition intDef = sharedParamElem.GetDefinition();
                if (intDef != null)
                {
                    try
                    {
                        intDef.SetAllowVaryBetweenGroups(doc, true);
                    }
                    catch (Exception ex)
                    {
                        SerializationResultModel.LogWarning($"Failed to set SetAllowVaryBetweenGroups for '{model.Name}': {ex.Message}");
                    }
                }
            }

            return sharedParamElem;
        }

        #region Explicit IModelTranslator implementations

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((ParameterElement)revitElement, (ParameterElementModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((ParameterElementModel)model, (ParameterElement?)revitElement, doc);
        }

        #endregion
    }
}
