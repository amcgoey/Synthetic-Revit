using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

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
    /// Analysis engine that scans duplicate POCO models and calculates merge recommendations headlessly.
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
                .GroupBy(element => element.Category ?? "Unknown Category");

            foreach (var categoryGroup in elementsByCategory)
            {
                token.ThrowIfCancellationRequested();
                string categoryName = categoryGroup.Key;

                // Group by base name within category
                var groupedByBaseName = categoryGroup
                    .GroupBy(element => GetBaseName(element.Name), StringComparer.OrdinalIgnoreCase);

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
                        foreach (var element in baseGroup)
                        {
                            token.ThrowIfCancellationRequested();
                            var item = CreateItemFromModel(element, categoryName);
                            items.Add(item);
                        }

                        // Flag the item with the shortest name as primary
                        if (items.Count > 0)
                        {
                            var primaryItem = items.OrderBy(item => item.ItemName.Length).First();
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
        public static string GetParameterValueString(ParameterModel parameter)
        {
            if (parameter == null) return string.Empty;
            string valueString = string.Empty;
            if (parameter.StorageType == "ElementId" && parameter.ValueElemId != null)
            {
                valueString = !string.IsNullOrEmpty(parameter.ValueElemId.Name) ? parameter.ValueElemId.Name : parameter.ValueElemId.Id.ToString();
            }
            else
            {
                valueString = parameter.Value ?? string.Empty;
            }
            return $"{parameter.StorageType}:{valueString}";
        }

        /// <summary>
        /// Creates a DuplicateItemModel from an ElementModel.
        /// </summary>
        public static DuplicateItemModel CreateItemFromModel(ElementModel element, string categoryName)
        {
            var item = new DuplicateItemModel
            {
                RevitElementId = element.ElementId,
                ItemName = element.Name,
                CategoryName = categoryName,
                IsPrimary = false,
                IsIncludedForMerge = true,
                IsLoadableFamily = (element.Class == "Autodesk.Revit.DB.Family" || element.Class == "Autodesk.Revit.DB.FamilySymbol")
            };

            item.InstanceCount = element.InstanceCount;
            item.Location = element.Location;
            item.BoundingBox = element.BoundingBox;

            // Populate parameters (legacy dict)
            item.Parameters = new Dictionary<string, string>();
            foreach (var parameter in element.Parameters)
            {
                if (!string.IsNullOrEmpty(parameter.Name) && !item.Parameters.ContainsKey(parameter.Name))
                {
                    item.Parameters[parameter.Name] = parameter.StorageType;
                }
            }

            // Populate Nested Collection of Types
            if (element.NestedTypes != null && element.NestedTypes.Count > 0)
            {
                foreach (var nestedType in element.NestedTypes)
                {
                    var typeModel = new DuplicateTypeModel
                    {
                        RevitTypeId = nestedType.ElementId,
                        Name = nestedType.Name,
                        Parameters = new Dictionary<string, string>()
                    };
                    foreach (var parameter in nestedType.Parameters)
                    {
                        if (!string.IsNullOrEmpty(parameter.Name) && !typeModel.Parameters.ContainsKey(parameter.Name))
                        {
                            if (IsIdentityParameter(parameter.Name)) continue;
                            typeModel.Parameters[parameter.Name] = GetParameterValueString(parameter);
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
                    RevitTypeId = element.ElementId,
                    Name = element.Name,
                    Parameters = new Dictionary<string, string>()
                };
                foreach (var parameter in element.Parameters)
                {
                    if (!string.IsNullOrEmpty(parameter.Name) && !typeModel.Parameters.ContainsKey(parameter.Name))
                    {
                        if (IsIdentityParameter(parameter.Name)) continue;
                        typeModel.Parameters[parameter.Name] = GetParameterValueString(parameter);
                    }
                }
                item.Types.Add(typeModel);
            }

            return item;
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
                    keyValuePair => keyValuePair.Key,
                    keyValuePair => keyValuePair.Value.Split(':')[0]
                );

                for (int typeIndex = 1; typeIndex < allTypes.Count; typeIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var currentType = allTypes[typeIndex];
                    if (firstSchema.Count != currentType.Parameters.Count)
                    {
                        schemaMismatch = true;
                        break;
                    }

                    foreach (var keyValuePair in currentType.Parameters)
                    {
                        string parameterName = keyValuePair.Key;
                        string currentStorageType = keyValuePair.Value.Split(':')[0];

                        if (!firstSchema.TryGetValue(parameterName, out string? expectedStorageType) || expectedStorageType != currentStorageType)
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
            var firstBoundingBox = firstItem.BoundingBox;
            var firstLocation = firstItem.Location;

            for (int itemIndex = 1; itemIndex < cluster.Items.Count; itemIndex++)
            {
                token.ThrowIfCancellationRequested();
                var currentItem = cluster.Items[itemIndex];
                var currentBoundingBox = currentItem.BoundingBox;
                var currentLocation = currentItem.Location;

                if (firstBoundingBox != null && currentBoundingBox != null && firstBoundingBox.IsValid && currentBoundingBox.IsValid &&
                    firstLocation != null && currentLocation != null)
                {
                    XYZModel? firstSize = firstBoundingBox.GetSize();
                    XYZModel? currentSize = currentBoundingBox.GetSize();

                    if (firstSize == null || currentSize == null ||
                        Math.Abs(firstSize.X - currentSize.X) > 1e-3 ||
                        Math.Abs(firstSize.Y - currentSize.Y) > 1e-3 ||
                        Math.Abs(firstSize.Z - currentSize.Z) > 1e-3)
                    {
                        originMismatch = true;
                        break;
                    }

                    // Compare Origin relative to Bounding Box center
                    XYZModel? firstCenter = firstBoundingBox.GetCenter();
                    XYZModel? currentCenter = currentBoundingBox.GetCenter();

                    XYZModel? firstOffset = (firstCenter != null) ? firstLocation - firstCenter : null;
                    XYZModel? currentOffset = (currentCenter != null) ? currentLocation - currentCenter : null;

                    if (!XYZModel.IsOffsetEqual(firstOffset, currentOffset, 1e-3))
                    {
                        originMismatch = true;
                        break;
                    }
                }
                else if ((firstBoundingBox == null) != (currentBoundingBox == null))
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

                foreach (var sourceType in item.Types)
                {
                    // Find target type with matching name in primary
                    var exactTargetType = primaryTypes!.FirstOrDefault(primaryType => primaryType.Name.Equals(sourceType.Name, StringComparison.Ordinal));
                    RecommendedAction recommendation = RecommendedAction.Merge;

                    if (exactTargetType == null)
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
                        SourceType = sourceType,
                        RecommendedAction = recommendation,
                        SourceFamily = item,
                        TargetFamily = primary
                    };

                    // Populate Available Types: For every TypeMappingModel being created, populate its AvailablePrimaryTypes collection with all DuplicateTypeModels found in the Primary Family (TargetFamily.Types).
                    if (primaryTypes != null)
                    {
                        foreach (var primaryType in primaryTypes)
                        {
                            mapping.AvailablePrimaryTypes.Add(primaryType);
                        }
                    }

                    // Auto-Match Logic: We want the dropdown to preselect the most likely match.
                    // Compare the base name of the SourceType against the base name of each type in AvailablePrimaryTypes. Use the existing GetBaseName() method.
                    // Set mapping.TargetType to the first type in the list that matches the base name.
                    // If no base names match, fallback to setting mapping.TargetType to AvailablePrimaryTypes.FirstOrDefault().
                    DuplicateTypeModel? matchedTarget = null;
                    string sourceBaseName = GetBaseName(sourceType.Name);
                    if (primaryTypes != null)
                    {
                        foreach (var availableType in mapping.AvailablePrimaryTypes)
                        {
                            if (GetBaseName(availableType.Name).Equals(sourceBaseName, StringComparison.OrdinalIgnoreCase))
                            {
                                matchedTarget = availableType;
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
                    mapping.MigrateRenameText = sourceType.Name;

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

            var sourceType = mapping.SourceType;
            if (sourceType == null) return;

            var targetType = mapping.TargetType;
            if (targetType == null)
            {
                // Fallback: If no target type is selected/available, load only source parameters with no highlights
                foreach (var keyValuePair in sourceType.Parameters)
                {
                    string parameterName = keyValuePair.Key;
                    string sourceRaw = keyValuePair.Value;

                    string? sourceStorageType = null, sourceValueString = null;
                    if (!string.IsNullOrEmpty(sourceRaw))
                    {
                        int colonIndex = sourceRaw.IndexOf(':');
                        if (colonIndex >= 0)
                        {
                            sourceStorageType = sourceRaw.Substring(0, colonIndex);
                            sourceValueString = sourceRaw.Substring(colonIndex + 1);
                        }
                    }

                    var row = new ParameterDiffRowModel
                    {
                        ParameterName = parameterName,
                        IsSchemaMismatch = false,
                        HasConflict = false,
                        WinningValueElementId = sourceType.RevitTypeId,
                        IsInjectEnabled = false
                    };

                    row.Values[sourceType.RevitTypeId] = sourceValueString ?? string.Empty;
                    row.ValueList = new List<string> { sourceValueString ?? string.Empty, string.Empty };

                    row.Options = new List<ParameterValueOption>
                    {
                        new ParameterValueOption { ElementId = sourceType.RevitTypeId, DisplayText = sourceValueString ?? string.Empty }
                    };

                    mapping.ParameterResolutions.Add(row);
                }
                return;
            }

            // Union of parameter names between source and target type to show all parameters
            var allParameterNames = sourceType.Parameters.Keys.Union(targetType.Parameters.Keys).ToList();
            foreach (var parameterName in allParameterNames)
            {
                string? sourceRaw = null;
                string? targetRaw = null;
                sourceType.Parameters.TryGetValue(parameterName, out sourceRaw);
                targetType.Parameters.TryGetValue(parameterName, out targetRaw);

                string? sourceStorageType = null, sourceValueString = null;
                if (!string.IsNullOrEmpty(sourceRaw))
                {
                    int colonIndex = sourceRaw.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        sourceStorageType = sourceRaw.Substring(0, colonIndex);
                        sourceValueString = sourceRaw.Substring(colonIndex + 1);
                    }
                }

                string? targetStorageType = null, targetValueString = null;
                if (!string.IsNullOrEmpty(targetRaw))
                {
                    int colonIndex = targetRaw.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        targetStorageType = targetRaw.Substring(0, colonIndex);
                        targetValueString = targetRaw.Substring(colonIndex + 1);
                    }
                }

                bool isSchemaMismatch = false;
                bool isInjectEnabled = false;

                if (sourceRaw != null && targetRaw != null)
                {
                    if (sourceStorageType != null && targetStorageType != null && sourceStorageType != targetStorageType)
                    {
                        isSchemaMismatch = true;
                        isInjectEnabled = true;
                    }
                }
                else if (sourceRaw != null) // targetRaw == null
                {
                    isSchemaMismatch = false;
                    isInjectEnabled = true;
                }
                else if (targetRaw != null) // sourceRaw == null
                {
                    isSchemaMismatch = false;
                    isInjectEnabled = false;
                }

                bool hasConflict = (sourceRaw != null && targetRaw != null) && (sourceValueString != targetValueString) && !isSchemaMismatch;

                var row = new ParameterDiffRowModel
                {
                    ParameterName = parameterName,
                    IsSchemaMismatch = isSchemaMismatch,
                    HasConflict = hasConflict,
                    WinningValueElementId = (mapping.RecommendedAction == RecommendedAction.Merge) ? targetType.RevitTypeId : sourceType.RevitTypeId,
                    IsInjectEnabled = isInjectEnabled
                };

                row.Values[sourceType.RevitTypeId] = sourceValueString ?? string.Empty;
                row.Values[targetType.RevitTypeId] = targetValueString ?? string.Empty;

                // If Migrate or Exclude is selected, then the Primary/Target family's parameter value column should show empty
                string primaryValue = (mapping.RecommendedAction == RecommendedAction.Merge) ? (targetValueString ?? string.Empty) : string.Empty;
                row.ValueList = new List<string> { sourceValueString ?? string.Empty, primaryValue };

                row.Options = new List<ParameterValueOption>
                {
                    new ParameterValueOption { ElementId = sourceType.RevitTypeId, DisplayText = sourceValueString ?? string.Empty }
                };
                if (mapping.RecommendedAction == RecommendedAction.Merge)
                {
                    row.Options.Add(new ParameterValueOption { ElementId = targetType.RevitTypeId, DisplayText = targetValueString ?? string.Empty });
                }

                mapping.ParameterResolutions.Add(row);
            }
        }
    }
}
