using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.RevitDOM.Operations.Merge
{
    /// <summary>
    /// Analysis engine that scans the document for duplicates and calculates merge recommendations.
    /// </summary>
    public static class MergeAnalysisEngine
    {
        /// <summary>
        /// Retrieves the base name from a name string by stripping optional separators and trailing numbers.
        /// </summary>
        /// <param name="name">The name to process.</param>
        /// <returns>The base name string without trailing numbers or separators.</returns>
        public static string GetBaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            // Match base name and strip optional separator (space, underscore, hyphen, dot, hash) followed by trailing numbers
            var match = Regex.Match(name, @"^(.*?)(?:[\s_#\-\.]+)?\d+$");
            return match.Success ? match.Groups[1].Value.Trim() : name.Trim();
        }

        /// <summary>
        /// Gets a string representation of a Parameter's storage type and value.
        /// </summary>
        /// <param name="p">The Parameter to read.</param>
        /// <param name="doc">The active Revit document to resolve element references.</param>
        /// <returns>A string representation of the parameter value.</returns>
        public static string GetParameterValueString(Parameter p, Document? doc = null)
        {
            if (p == null) return string.Empty;
            string storageType = p.StorageType.ToString();
            string valueString = string.Empty;
            switch (p.StorageType)
            {
                case StorageType.Double:
                    valueString = p.AsDouble().ToString();
                    break;
                case StorageType.Integer:
                    valueString = p.AsInteger().ToString();
                    break;
                case StorageType.String:
                    valueString = p.AsString() ?? string.Empty;
                    break;
                case StorageType.ElementId:
                    ElementId id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        if (doc != null)
                        {
                            Element elem = doc.GetElement(id);
                            if (elem != null)
                            {
                                valueString = elem.Name;
                            }
                            else
                            {
#if REVIT2022 || REVIT2023
                                valueString = id.IntegerValue.ToString();
#else
                                valueString = id.Value.ToString();
#endif
                            }
                        }
                        else
                        {
#if REVIT2022 || REVIT2023
                            valueString = id.IntegerValue.ToString();
#else
                            valueString = id.Value.ToString();
#endif
                        }
                    }
                    break;
            }
            return $"{storageType}:{valueString}";
        }

        /// <summary>
        /// Determines if the specified parameter name corresponds to an identity parameter.
        /// </summary>
        /// <param name="name">The name of the parameter to check.</param>
        /// <returns>True if the parameter name is an identity parameter; otherwise, false.</returns>
        public static bool IsIdentityParameter(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Equals("Family Name", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Type Name", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Type IfcGUID", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("IfcGUID", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Family", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Type", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Type Mark", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the category name of the specified Revit element.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="elem">The element whose category name should be retrieved.</param>
        /// <returns>The name of the category, or a default name if category is not found.</returns>
        public static string GetElementCategoryName(Document doc, Element elem)
        {
            if (elem == null) return "Unknown Category";

            if (elem is Family f)
            {
                if (f.FamilyCategory != null) return f.FamilyCategory.Name;
                var symbolIds = f.GetFamilySymbolIds();
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
            else if (elem is GroupType gt)
            {
                return gt.Category?.Name ?? "Model Groups";
            }
            else if (elem is Autodesk.Revit.DB.Group g)
            {
                return g.GroupType?.Category?.Name ?? "Model Groups";
            }
            else if (elem is AssemblyType at)
            {
                return at.Category?.Name ?? "Assemblies";
            }
            else if (elem is AssemblyInstance ai)
            {
                var typeId = ai.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    var typeElem = doc.GetElement(typeId) as AssemblyType;
                    if (typeElem != null)
                    {
                        return typeElem.Category?.Name ?? "Assemblies";
                    }
                }
                return "Assemblies";
            }

            return elem.Category?.Name ?? "Unknown Category";
        }

        /// <summary>
        /// Retrieves the target mergeable element (e.g. Family, GroupType, AssemblyType) from a given ElementId.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="id">The ElementId to resolve.</param>
        /// <returns>The resolved target element, or null if not found.</returns>
        public static Element? GetTargetElement(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            Element elem = doc.GetElement(id);
            if (elem == null) return null;

            // 1. Direct checks (typically selected in Project Browser or direct types)
            if (elem is Family) return elem;
            if (elem is GroupType) return elem;
            if (elem is AssemblyType) return elem;
            if (elem is FamilySymbol symbol) return symbol.Family;
            if (elem is ElementType et) return et;

            // 2. Instance checks (typically selected in Canvas)
            if (elem is FamilyInstance fi)
            {
                var symId = fi.GetTypeId();
                if (symId != null && symId != ElementId.InvalidElementId)
                {
                    var sym = doc.GetElement(symId) as FamilySymbol;
                    return sym?.Family;
                }
            }
            if (elem is Autodesk.Revit.DB.Group g)
            {
                return g.GroupType;
            }
            if (elem is AssemblyInstance ai)
            {
                var typeId = ai.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId)
                {
                    return doc.GetElement(typeId) as AssemblyType;
                }
            }

            // 3. Fallback using GetTypeId() for other instance elements
            var elemTypeId = elem.GetTypeId();
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
        /// Groups POCO element models into duplicate clusters headlessly.
        /// </summary>
        /// <param name="elements">The list of ElementModels to group.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A collection of duplicate cluster models.</returns>
        public static ObservableCollection<DuplicateClusterModel> BuildClustersFromModels(IEnumerable<ElementModel> elements, CancellationToken token)
        {
            var clusters = new ObservableCollection<DuplicateClusterModel>();

            // Group by Category Name
            var elementsByCategory = elements
                .GroupBy(elem => elem.Category ?? "Unknown Category");

            foreach (var categoryGroup in elementsByCategory)
            {
                token.ThrowIfCancellationRequested();
                string categoryName = categoryGroup.Key;

                // Group by base name within category
                var groupedByBaseName = categoryGroup
                    .GroupBy(elem => GetBaseName(elem.Name), StringComparer.OrdinalIgnoreCase);

                foreach (var baseGroup in groupedByBaseName)
                {
                    token.ThrowIfCancellationRequested();
                    // Only clusters with more than 1 item are duplicates
                    if (baseGroup.Count() > 1)
                    {
                        var cluster = new DuplicateClusterModel
                        {
                            ClusterName = $"{categoryName}: {baseGroup.Key} Duplicates"
                        };

                        var items = new List<DuplicateItemModel>();
                        foreach (var elem in baseGroup)
                        {
                            token.ThrowIfCancellationRequested();
                            var item = CreateItemFromModel(elem, categoryName);
                            items.Add(item);
                        }

                        // Flag the item with the shortest name as primary
                        if (items.Count > 0)
                        {
                            var primaryItem = items.OrderBy(i => i.ItemName.Length).First();
                            primaryItem.IsPrimary = true;
                        }

                        foreach (var item in items)
                        {
                            cluster.Items.Add(item);
                        }

                        clusters.Add(cluster);
                    }
                }
            }
            return clusters;
        }

        /// <summary>
        /// Gets a string representation of a ParameterModel's value.
        /// </summary>
        public static string GetParameterValueString(ParameterModel p)
        {
            if (p == null) return string.Empty;
            string valueString = string.Empty;
            if (p.StorageType == "ElementId" && p.ValueElemId != null)
            {
                valueString = !string.IsNullOrEmpty(p.ValueElemId.Name) ? p.ValueElemId.Name : p.ValueElemId.Id.ToString();
            }
            else
            {
                valueString = p.Value ?? string.Empty;
            }
            return $"{p.StorageType}:{valueString}";
        }

        /// <summary>
        /// Creates a DuplicateItemModel from an ElementModel.
        /// </summary>
        public static DuplicateItemModel CreateItemFromModel(ElementModel elem, string categoryName)
        {
            var item = new DuplicateItemModel
            {
                RevitElementId = elem.ElementId,
                ItemName = elem.Name,
                CategoryName = categoryName,
                IsPrimary = false,
                IsIncludedForMerge = true,
                IsLoadableFamily = (elem.Class == "Autodesk.Revit.DB.Family" || elem.Class == "Autodesk.Revit.DB.FamilySymbol")
            };

            item.InstanceCount = elem.InstanceCount;
            item.Location = elem.Location;
            item.BoundingBox = elem.BoundingBox;

            // Populate parameters (legacy dict)
            item.Parameters = new Dictionary<string, string>();
            foreach (var p in elem.Parameters)
            {
                if (!string.IsNullOrEmpty(p.Name) && !item.Parameters.ContainsKey(p.Name))
                {
                    item.Parameters[p.Name] = p.StorageType;
                }
            }

            // Populate Nested Collection of Types
            if (elem.NestedTypes != null && elem.NestedTypes.Count > 0)
            {
                foreach (var nestedType in elem.NestedTypes)
                {
                    var typeModel = new DuplicateTypeModel
                    {
                        RevitTypeId = nestedType.ElementId,
                        Name = nestedType.Name,
                        Parameters = new Dictionary<string, string>()
                    };
                    foreach (var p in nestedType.Parameters)
                    {
                        if (!string.IsNullOrEmpty(p.Name) && !typeModel.Parameters.ContainsKey(p.Name))
                        {
                            if (IsIdentityParameter(p.Name)) continue;
                            typeModel.Parameters[p.Name] = GetParameterValueString(p);
                        }
                    }
                    item.Types.Add(typeModel);
                }
            }
            else
            {
                // Fallback: add the element itself as the single type
                var typeModel = new DuplicateTypeModel
                {
                    RevitTypeId = elem.ElementId,
                    Name = elem.Name,
                    Parameters = new Dictionary<string, string>()
                };
                foreach (var p in elem.Parameters)
                {
                    if (!string.IsNullOrEmpty(p.Name) && !typeModel.Parameters.ContainsKey(p.Name))
                    {
                        if (IsIdentityParameter(p.Name)) continue;
                        typeModel.Parameters[p.Name] = GetParameterValueString(p);
                    }
                }
                item.Types.Add(typeModel);
            }

            return item;
        }

        public static ElementModel ConvertToPoco(Document doc, Element elem)
        {
            var model = elem.ToModel(false);
            
            // Get instances to calculate count, location, bounding box
            var instances = new List<Element>();
            if (elem is Family family)
            {
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    var symbolIdSet = new HashSet<ElementId>(symbolIds);
                    var familyInstances = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .Where(inst => symbolIdSet.Contains(inst.GetTypeId()))
                        .ToList();
                    instances.AddRange(familyInstances);
                }
            }
            else if (elem is GroupType gt)
            {
                var groupInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Group))
                    .Where(g => g.GetTypeId() == gt.Id)
                    .ToList();
                instances.AddRange(groupInstances);
            }
            else if (elem is AssemblyType at)
            {
                var assemblyInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Where(a => a.GetTypeId() == at.Id)
                    .ToList();
                instances.AddRange(assemblyInstances);
            }
            else if (elem is ElementType et)
            {
                var typeInstances = new FilteredElementCollector(doc)
                    .WherePasses(new ElementIsElementTypeFilter(true)) // Instances
                    .Where(x => x.GetTypeId() == et.Id)
                    .ToList();
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
        /// <param name="doc">The active Revit document.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        /// <returns>A collection of duplicate cluster models found during the scan.</returns>
        public static ObservableCollection<DuplicateClusterModel> RunFastScan(Document doc, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc).WherePasses(filter);

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

            return BuildClustersFromModels(models, token);
        }

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
                selectedBaseNames.Add(GetBaseName(target.Name));
            }

            if (selectedCategories.Count == 0 || selectedBaseNames.Count == 0)
            {
                return clusters;
            }

            var elements = new List<Element>();

            // Collect Family, GroupType, and AssemblyType elements matching selection parameters using ElementMulticlassFilter
            var classes = new List<Type> { typeof(Family), typeof(GroupType), typeof(AssemblyType) };
            var filter = new ElementMulticlassFilter(classes);
            var collector = new FilteredElementCollector(doc).WherePasses(filter);

            foreach (var elem in collector)
            {
                token.ThrowIfCancellationRequested();
                if (elem is Family fam)
                {
                    if (fam.IsInPlace) continue;
                    string cat = GetElementCategoryName(doc, fam);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(GetBaseName(fam.Name)))
                    {
                        elements.Add(fam);
                    }
                }
                else
                {
                    string cat = GetElementCategoryName(doc, elem);
                    if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(GetBaseName(elem.Name)))
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
                        .OfClass(typeClass)
                        .Cast<ElementType>();
                    foreach (var et in matchingTypes)
                    {
                        token.ThrowIfCancellationRequested();
                        string cat = GetElementCategoryName(doc, et);
                        if (selectedCategories.Contains(cat) && selectedBaseNames.Contains(GetBaseName(et.Name)))
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

            return BuildClustersFromModels(models, token);
        }
        /// <summary>
        /// Performs a deep comparison of the schemas and geometry in the given duplicate cluster.
        /// </summary>
        /// <param name="cluster">The duplicate cluster model to analyze.</param>
        /// <param name="token">A cancellation token to monitor for cancellation requests.</param>
        public static void RunDeepScan(DuplicateClusterModel cluster, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (cluster == null || cluster.Items == null || cluster.Items.Count <= 1)
            {
                return;
            }

            // 1. Compare parameter schemas (names and storage types) of the DuplicateTypeModel items in the cluster
            bool schemaMismatch = false;
            var allTypes = cluster.Items.SelectMany(item => item.Types).ToList();
            if (allTypes.Count > 1)
            {
                var firstType = allTypes[0];
                var firstSchema = firstType.Parameters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Split(':')[0]
                );

                for (int i = 1; i < allTypes.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var currentType = allTypes[i];
                    if (firstSchema.Count != currentType.Parameters.Count)
                    {
                        schemaMismatch = true;
                        break;
                    }

                    foreach (var kvp in currentType.Parameters)
                    {
                        string paramName = kvp.Key;
                        string currentStorageType = kvp.Value.Split(':')[0];

                        if (!firstSchema.TryGetValue(paramName, out string? expectedStorageType) || expectedStorageType != currentStorageType)
                        {
                            schemaMismatch = true;
                            break;
                        }
                    }
                    if (schemaMismatch) break;
                }
            }
            cluster.HasSchemaMismatch = schemaMismatch;

            // 2. Compare physical origins / bounding boxes of instances
            bool originMismatch = false;
            var firstItem = cluster.Items[0];
            var firstBBox = firstItem.BoundingBox;
            var firstLoc = firstItem.Location;

            for (int i = 1; i < cluster.Items.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var currentItem = cluster.Items[i];
                var currentBBox = currentItem.BoundingBox;
                var currentLoc = currentItem.Location;

                if (firstBBox != null && currentBBox != null && firstBBox.IsValid && currentBBox.IsValid &&
                    firstLoc != null && currentLoc != null)
                {
                    XYZModel? firstSize = firstBBox.GetSize();
                    XYZModel? currentSize = currentBBox.GetSize();

                    if (firstSize == null || currentSize == null ||
                        Math.Abs(firstSize.X - currentSize.X) > 1e-3 ||
                        Math.Abs(firstSize.Y - currentSize.Y) > 1e-3 ||
                        Math.Abs(firstSize.Z - currentSize.Z) > 1e-3)
                    {
                        originMismatch = true;
                        break;
                    }

                    // Compare Origin relative to Bounding Box center
                    XYZModel? firstCenter = firstBBox.GetCenter();
                    XYZModel? currentCenter = currentBBox.GetCenter();

                    XYZModel? firstOffset = (firstCenter != null) ? firstLoc - firstCenter : null;
                    XYZModel? currentOffset = (currentCenter != null) ? currentLoc - currentCenter : null;

                    if (!XYZModel.IsOffsetEqual(firstOffset, currentOffset, 1e-3))
                    {
                        originMismatch = true;
                        break;
                    }
                }
                else if ((firstBBox == null) != (currentBBox == null))
                {
                    // One has instances and the other doesn't
                    originMismatch = true;
                    break;
                }
            }
            cluster.HasOriginMismatch = originMismatch;
        }

        /// <summary>
        /// Generates recommended mapping actions and highlights parameter conflicts within the duplicate cluster.
        /// </summary>
        /// <param name="cluster">The duplicate cluster model for which recommendations are generated.</param>
        public static void GenerateRecommendations(DuplicateClusterModel cluster)
        {
            if (cluster == null) return;
            cluster.TypeMappings.Clear();

            var primary = cluster.SelectedPrimary;
            if (primary == null) return;

            var primaryTypes = primary.Types;
            if (primaryTypes == null || primaryTypes.Count == 0) return;

            foreach (var item in cluster.Items)
            {
                if (item.IsPrimary) continue; // Skip primary itself

                foreach (var srcType in item.Types)
                {
                    // Find target type with matching name in primary
                    var exactTgtType = primaryTypes!.FirstOrDefault(t => t.Name.Equals(srcType.Name, StringComparison.Ordinal));
                    RecommendedAction recommendation = RecommendedAction.Merge;

                    if (exactTgtType == null)
                    {
                        // No exact match. For loadable families, recommend Migrate. For system/flat families (like groups/assemblies), we must Merge.
                        if (item.IsLoadableFamily)
                        {
                            recommendation = RecommendedAction.Migrate;
                        }
                        else
                        {
                            recommendation = RecommendedAction.Merge;
                        }
                    }

                    var mapping = new TypeMappingModel
                    {
                        SourceType = srcType,
                        RecommendedAction = recommendation,
                        SourceFamily = item,
                        TargetFamily = primary
                    };

                    // Populate Available Types: For every TypeMappingModel being created, populate its AvailablePrimaryTypes collection with all DuplicateTypeModels found in the Primary Family (TargetFamily.Types).
                    if (primaryTypes != null)
                    {
                        foreach (var t in primaryTypes)
                        {
                            mapping.AvailablePrimaryTypes.Add(t);
                        }
                    }

                    // Auto-Match Logic: We want the dropdown to preselect the most likely match.
                    // Compare the base name of the SourceType against the base name of each type in AvailablePrimaryTypes. Use the existing GetBaseName() method.
                    // Set mapping.TargetType to the first type in the list that matches the base name.
                    // If no base names match, fallback to setting mapping.TargetType to AvailablePrimaryTypes.FirstOrDefault().
                    DuplicateTypeModel? matchedTarget = null;
                    string srcBaseName = GetBaseName(srcType.Name);
                    if (primaryTypes != null)
                    {
                        foreach (var availType in mapping.AvailablePrimaryTypes)
                        {
                            if (GetBaseName(availType.Name).Equals(srcBaseName, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedTarget = availType;
                                break;
                            }
                        }
                    }

                    if (matchedTarget == null)
                    {
                        matchedTarget = mapping.AvailablePrimaryTypes.FirstOrDefault();
                    }

                    if (matchedTarget == null) continue; // Safety check

                    mapping.TargetType = matchedTarget;

                    // Default Rename Text: Set mapping.MigrateRenameText to mapping.SourceType.Name by default.
                    mapping.MigrateRenameText = srcType.Name;

                    UpdateParameterResolutions(mapping);

                    cluster.TypeMappings.Add(mapping);
                }
            }
        }

        /// <summary>
        /// Updates parameter resolutions for a given type mapping model based on the recommended action.
        /// </summary>
        /// <param name="mapping">The type mapping model to update.</param>
        public static void UpdateParameterResolutions(TypeMappingModel mapping)
        {
            if (mapping == null) return;

            mapping.ParameterResolutions.Clear();

            var srcType = mapping.SourceType;
            if (srcType == null) return;

            var tgtType = mapping.TargetType;
            if (tgtType == null)
            {
                // Fallback: If no target type is selected/available, load only source parameters with no highlights
                foreach (var kvp in srcType.Parameters)
                {
                    string paramName = kvp.Key;
                    string srcRaw = kvp.Value;

                    string? srcStorage = null, srcVal = null;
                    if (!string.IsNullOrEmpty(srcRaw))
                    {
                        int colonIndex = srcRaw.IndexOf(':');
                        if (colonIndex >= 0)
                        {
                            srcStorage = srcRaw.Substring(0, colonIndex);
                            srcVal = srcRaw.Substring(colonIndex + 1);
                        }
                    }

                    var row = new ParameterDiffRowModel
                    {
                        ParameterName = paramName,
                        IsSchemaMismatch = false,
                        HasConflict = false,
                        WinningValueElementId = srcType.RevitTypeId,
                        IsInjectEnabled = false
                    };

                    row.Values[srcType.RevitTypeId] = srcVal ?? string.Empty;
                    row.ValueList = new List<string> { srcVal ?? string.Empty, string.Empty };

                    row.Options = new List<ParameterValueOption>
                    {
                        new ParameterValueOption { ElementId = srcType.RevitTypeId, DisplayText = srcVal ?? string.Empty }
                    };

                    mapping.ParameterResolutions.Add(row);
                }
                return;
            }

            // Union of parameter names between source and target type to show all parameters
            var allParamNames = srcType.Parameters.Keys.Union(tgtType.Parameters.Keys).ToList();
            foreach (var paramName in allParamNames)
            {
                string? srcRaw = null;
                string? tgtRaw = null;
                srcType.Parameters.TryGetValue(paramName, out srcRaw);
                tgtType.Parameters.TryGetValue(paramName, out tgtRaw);

                string? srcStorage = null, srcVal = null;
                if (!string.IsNullOrEmpty(srcRaw))
                {
                    int colonIndex = srcRaw.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        srcStorage = srcRaw.Substring(0, colonIndex);
                        srcVal = srcRaw.Substring(colonIndex + 1);
                    }
                }

                string? tgtStorage = null, tgtVal = null;
                if (!string.IsNullOrEmpty(tgtRaw))
                {
                    int colonIndex = tgtRaw.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        tgtStorage = tgtRaw.Substring(0, colonIndex);
                        tgtVal = tgtRaw.Substring(colonIndex + 1);
                    }
                }

                bool isSchemaMismatch = false;
                bool isInjectEnabled = false;

                if (srcRaw != null && tgtRaw != null)
                {
                    if (srcStorage != null && tgtStorage != null && srcStorage != tgtStorage)
                    {
                        isSchemaMismatch = true;
                        isInjectEnabled = true;
                    }
                }
                else if (srcRaw != null) // tgtRaw == null
                {
                    isSchemaMismatch = false;
                    isInjectEnabled = true;
                }
                else if (tgtRaw != null) // srcRaw == null
                {
                    isSchemaMismatch = false;
                    isInjectEnabled = false;
                }

                bool hasConflict = (srcRaw != null && tgtRaw != null) && (srcVal != tgtVal) && !isSchemaMismatch;

                var row = new ParameterDiffRowModel
                {
                    ParameterName = paramName,
                    IsSchemaMismatch = isSchemaMismatch,
                    HasConflict = hasConflict,
                    WinningValueElementId = (mapping.RecommendedAction == RecommendedAction.Merge) ? tgtType.RevitTypeId : srcType.RevitTypeId,
                    IsInjectEnabled = isInjectEnabled
                };

                row.Values[srcType.RevitTypeId] = srcVal ?? string.Empty;
                row.Values[tgtType.RevitTypeId] = tgtVal ?? string.Empty;

                // If Migrate or Exclude is selected, then the Primary/Target family's parameter value column should show empty
                string primaryValue = (mapping.RecommendedAction == RecommendedAction.Merge) ? (tgtVal ?? string.Empty) : string.Empty;
                row.ValueList = new List<string> { srcVal ?? string.Empty, primaryValue };

                row.Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = srcType.RevitTypeId, DisplayText = srcVal ?? string.Empty }
                };
                if (mapping.RecommendedAction == RecommendedAction.Merge)
                {
                    row.Options.Add(new ParameterValueOption { ElementId = tgtType.RevitTypeId, DisplayText = tgtVal ?? string.Empty });
                }

                mapping.ParameterResolutions.Add(row);
            }
        }
    }
}
