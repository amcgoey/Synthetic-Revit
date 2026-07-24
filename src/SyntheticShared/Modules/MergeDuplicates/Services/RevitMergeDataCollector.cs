using Synthetic.Shared.RevitAPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations.Merge;

namespace Synthetic.Modules.MergeDuplicates.Services
{
    /// <summary>
    /// Service for querying the Revit document and extracting ElementModel POCOs for duplicate merging analysis.
    /// </summary>
    public static class RevitMergeDataCollector
    {
        /// <summary>
        /// Gets a string representation of a Revit Parameter's storage type and value.
        /// </summary>
        public static string GetParameterValueString(Parameter parameter, Document? doc = null)
        {
            if (parameter == null) return string.Empty;
            string storageType = parameter.StorageType.ToString();
            string valueString = string.Empty;
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    valueString = parameter.AsDouble().ToString();
                    break;
                case StorageType.Integer:
                    valueString = parameter.AsInteger().ToString();
                    break;
                case StorageType.String:
                    valueString = parameter.AsString() ?? string.Empty;
                    break;
                case StorageType.ElementId:
                    ElementId elementId = parameter.AsElementId();
                    if (elementId != null && elementId != ElementId.InvalidElementId)
                    {
                        if (doc != null)
                        {
                            Element element = doc.GetElement(elementId);
                            if (element != null)
                            {
                                valueString = element.Name;
                            }
                            else
                            {
#if REVIT2022 || REVIT2023
                                valueString = elementId.IntegerValue.ToString();
#else
                                valueString = elementId.Value.ToString();
#endif
                            }
                        }
                        else
                        {
#if REVIT2022 || REVIT2023
                            valueString = elementId.IntegerValue.ToString();
#else
                            valueString = elementId.Value.ToString();
#endif
                        }
                    }
                    break;
            }
            return $"{storageType}:{valueString}";
        }

        /// <summary>
        /// Gets the category name of the specified Revit element.
        /// </summary>
        public static string GetElementCategoryName(Document doc, Element element)
        {
            if (element == null) return "Unknown Category";

            if (element is Family family)
            {
                if (family.FamilyCategory != null) return family.FamilyCategory.Name;
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    var firstSymbol = doc.GetElement(symbolIds.First()) as FamilySymbol;
                    if (firstSymbol?.Category != null)
                    {
                        return firstSymbol.Category.Name;
                    }
                }
                return "Unknown Category";
            }
            else if (element is GroupType groupType)
            {
                return groupType.Category?.Name ?? "Model Groups";
            }
            else if (element is Autodesk.Revit.DB.Group group)
            {
                return group.GroupType?.Category?.Name ?? "Model Groups";
            }
            else if (element is AssemblyType assemblyType)
            {
                return assemblyType.Category?.Name ?? "Assemblies";
            }
            else if (element is AssemblyInstance assemblyInstance)
            {
                var typeElementId = assemblyInstance.GetTypeId();
                if (typeElementId != null && typeElementId != ElementId.InvalidElementId)
                {
                    var typeElement = doc.GetElement(typeElementId) as AssemblyType;
                    if (typeElement != null)
                    {
                        return typeElement.Category?.Name ?? "Assemblies";
                    }
                }
                return "Assemblies";
            }

            return element.Category?.Name ?? "Unknown Category";
        }

        /// <summary>
        /// Retrieves the target mergeable element (e.g. Family, GroupType, AssemblyType) from a given ElementId.
        /// </summary>
        public static Element? GetTargetElement(Document doc, ElementId elementId)
        {
            if (elementId == null || elementId == ElementId.InvalidElementId) return null;
            Element element = doc.GetElement(elementId);
            if (element == null) return null;

            // 1. Direct checks (typically selected in Project Browser or direct types)
            if (element is Family) return element;
            if (element is GroupType) return element;
            if (element is AssemblyType) return element;
            if (element is FamilySymbol patternFamilySymbol) return patternFamilySymbol.Family;
            if (element is ElementType elementType) return element;

            // 2. Instance checks (typically selected in Canvas)
            if (element is FamilyInstance familyInstance)
            {
                var symbolId = familyInstance.GetTypeId();
                if (symbolId != null && symbolId != ElementId.InvalidElementId)
                {
                    var familySymbolObj = doc.GetElement(symbolId) as FamilySymbol;
                    return familySymbolObj?.Family;
                }
            }
            if (element is Autodesk.Revit.DB.Group group)
            {
                return group.GroupType;
            }
            if (element is AssemblyInstance assemblyInstance)
            {
                var typeElementId = assemblyInstance.GetTypeId();
                if (typeElementId != null && typeElementId != ElementId.InvalidElementId)
                {
                    return doc.GetElement(typeElementId) as AssemblyType;
                }
            }

            // 3. Fallback using GetTypeId() for other instance elements
            var elemTypeId = element.GetTypeId();
            if (elemTypeId != null && elemTypeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(elemTypeId);
                if (typeElem is FamilySymbol fs) return fs.Family;
                if (typeElem is GroupType gt) return gt;
                if (typeElem is AssemblyType at) return at;
                if (typeElem is ElementType et2) return et2;
            }

            return null;
        }

        /// <summary>
        /// Converts a Revit Element to an ElementModel POCO, including instance counts, location, bounding box, and nested types.
        /// </summary>
        public static ElementModel ConvertToPoco(Document doc, Element elem)
        {
            var model = elem.ToModel(false);

            // Get instances to calculate count, location, bounding box using native quick filters first
            var instances = new List<Element>();
            if (elem is Family family)
            {
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    var symbolIdSet = new HashSet<ElementId>(symbolIds);
                    var familyInstances = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .WhereElementIsNotElementType()
                        .Where(inst => symbolIdSet.Contains(inst.GetTypeId()))
                        .ToList();
                    instances.AddRange(familyInstances);
                }
            }
            else if (elem is GroupType gt)
            {
                var groupInstances = Select.GetInstancesFromElemType(gt, doc).ToList();
                instances.AddRange(groupInstances);
            }
            else if (elem is AssemblyType at)
            {
                var assemblyInstances = Select.GetInstancesFromElemType(at, doc).ToList();
                instances.AddRange(assemblyInstances);
            }
            else if (elem is ElementType et)
            {
                var typeInstances = Select.GetInstancesFromElemType(et, doc).ToList();
                instances.AddRange(typeInstances);
            }

            model.InstanceCount = instances.Count;

            var firstInstance = instances.FirstOrDefault();
            if (firstInstance != null)
            {
                if (firstInstance.Location is LocationPoint lp)
                {
                    model.Location = lp.Point.ToModel();
                }
                else if (firstInstance is FamilyInstance fi)
                {
                    model.Location = fi.GetTransform().Origin.ToModel();
                }
                else
                {
                    var bbox = firstInstance.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        model.Location = ((bbox.Max + bbox.Min) * 0.5).ToModel();
                    }
                }
                model.BoundingBox = firstInstance.get_BoundingBox(null).ToModel();
            }

            // Populate NestedTypes if it is a Family
            if (elem is Family fam)
            {
                var symbolIds = fam.GetFamilySymbolIds();
                if (symbolIds != null)
                {
                    foreach (var sId in symbolIds)
                    {
                        var symbol = doc.GetElement(sId) as FamilySymbol;
                        if (symbol != null)
                        {
                            var nestedModel = symbol.ToModel(false);
                            model.NestedTypes.Add(nestedModel);
                        }
                    }
                }
            }

            return model;
        }

        /// <summary>
        /// Scans the Revit document quickly for duplicate elements.
        /// </summary>
        public static ObservableCollection<DuplicateClusterModel> RunFastScan(Document doc, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                token.ThrowIfCancellationRequested();
                if (elem is Family f)
                {
                    if (!f.IsInPlace)
                    {
                        elements.Add(f);
                    }
                }
                else
                {
                    elements.Add(elem);
                }
            }

            var models = new List<ElementModel>();
            foreach (var elem in elements)
            {
                token.ThrowIfCancellationRequested();
                models.Add(ConvertToPoco(doc, elem));
            }

            return MergeAnalysisEngine.BuildClustersFromModels(models, token);
        }

        /// <summary>
        /// Performs a targeted scan on specific selected element IDs in the Revit document.
        /// </summary>
        public static ObservableCollection<DuplicateClusterModel> RunTargetedScan(Document doc, ICollection<ElementId> selectedIds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var clusters = new ObservableCollection<DuplicateClusterModel>();

            if (selectedIds == null || selectedIds.Count == 0) return clusters;

            var selectedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in selectedIds)
            {
                token.ThrowIfCancellationRequested();
                var originalElem = doc.GetElement(id);
                if (originalElem == null) continue;

                var target = GetTargetElement(doc, id);
                if (target == null) continue;

                string categoryName = GetElementCategoryName(doc, originalElem);
                selectedCategories.Add(categoryName);
                selectedBaseNames.Add(MergeAnalysisEngine.GetBaseName(target.Name));
            }

            if (selectedCategories.Count == 0 || selectedBaseNames.Count == 0)
            {
                return clusters;
            }

            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements matching selection parameters using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                token.ThrowIfCancellationRequested();
                if (elem is Family fam)
                {
                    if (fam.IsInPlace) continue;
                    string cat = GetElementCategoryName(doc, fam);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(fam.Name)))
                    {
                        elements.Add(fam);
                    }
                }
                else
                {
                    string cat = GetElementCategoryName(doc, elem);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(elem.Name)))
                    {
                        elements.Add(elem);
                    }
                }
            }

            // Collect generic ElementTypes matching selection parameters (e.g. WallType)
            foreach (var id in selectedIds)
            {
                token.ThrowIfCancellationRequested();
                var target = GetTargetElement(doc, id);
                if (target == null) continue;

                if (target is ElementType && !(target is GroupType) && !(target is AssemblyType) && !(target is FamilySymbol))
                {
                    var typeClass = target.GetType();
                    var matchingTypes = new FilteredElementCollector(doc)
                        .WhereElementIsElementType()
                        .OfClass(typeClass)
                        .Cast<ElementType>();
                    foreach (var et in matchingTypes)
                    {
                        token.ThrowIfCancellationRequested();
                        string cat = GetElementCategoryName(doc, et);
                        if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(MergeAnalysisEngine.GetBaseName(et.Name)))
                        {
                            if (!elements.Any(x => x.Id == et.Id))
                            {
                                elements.Add(et);
                            }
                        }
                    }
                }
            }

            var models = new List<ElementModel>();
            foreach (var elem in elements)
            {
                token.ThrowIfCancellationRequested();
                models.Add(ConvertToPoco(doc, elem));
            }

            return MergeAnalysisEngine.BuildClustersFromModels(models, token);
        }

        /// <summary>
        /// Collects candidate straggler elements matching a given category name from the Revit document.
        /// </summary>
        public static List<Tuple<string, ElementId>> GetCandidateStragglers(Document doc, string categoryName, HashSet<ElementId> existingIds)
        {
            var candidateList = new List<Tuple<string, ElementId>>();

            // Use ElementMulticlassFilter to collect Families, GroupTypes, and AssemblyTypes
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc)
                .WherePasses(filter);

            foreach (var elem in collector)
            {
                if (existingIds.Contains(elem.Id)) continue;
                if (elem is Family fam && fam.IsInPlace) continue;

                string cat = GetElementCategoryName(doc, elem);
                if (cat.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    candidateList.Add(Tuple.Create($"{elem.Name} ({elem.GetType().Name})", elem.Id));
                }
            }

            // Also collect generic ElementTypes if the category matches
            var elementTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>();
            foreach (var et in elementTypes)
            {
                if (existingIds.Contains(et.Id)) continue;
                if (et is GroupType || et is AssemblyType || et is FamilySymbol) continue; // Exclude since already handled or nested in families

                string cat = GetElementCategoryName(doc, et);
                if (cat.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                {
                    candidateList.Add(Tuple.Create($"{et.Name} ({et.GetType().Name})", et.Id));
                }
            }

            return candidateList;
        }
    }
}


