using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Platform.Revit;

namespace Synthetic.RevitDOM.Operations.Diffing
{
    /// <summary>
    /// Implementation of IDiffEngine that compares a collection of pure POCO ObjectModels against a live Revit Document.
    /// </summary>
    public class PocoToRevitDiffEngine : IDiffEngine<IEnumerable<ObjectModel>, Document>
    {
        private readonly IIdentityService _identityService;

        /// <summary>
        /// Initializes a new instance of the <see cref="PocoToRevitDiffEngine"/> class.
        /// </summary>
        public PocoToRevitDiffEngine() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PocoToRevitDiffEngine"/> class with a custom identity service.
        /// </summary>
        /// <param name="identityService">The identity service to use for resolving elements.</param>
        public PocoToRevitDiffEngine(IIdentityService? identityService)
        {
            _identityService = identityService ?? new RevitIdentityService();
        }

        /// <summary>
        /// Compares the source models against the live Revit document database and returns diff/duplicate report clusters.
        /// </summary>
        public IEnumerable<DuplicateClusterModel> Compare(
            IEnumerable<ObjectModel> source,
            Document target,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var clusters = new List<DuplicateClusterModel>();
            bool isFamily = target.IsFamilyDocument;
            long fakeIdCounter = -1000;

            progress?.Report("Grouping incoming models by Category...");

            var elementModels = source.OfType<ElementModel>();
            var modelsByCategory = elementModels
                .Where(m => !string.IsNullOrEmpty(m.Class) && StandardTypeRegistry.IsInDocumentScope(m, isFamily))
                .GroupBy(m => m.Category ?? "Unknown Category", StringComparer.OrdinalIgnoreCase);

            foreach (var categoryGroup in modelsByCategory)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                string categoryName = categoryGroup.Key;
                progress?.Report($"Comparing category: {categoryName}...");

                var cluster = new DuplicateClusterModel
                {
                    ClusterName = $"{categoryName}: Standards Comparison"
                };

                foreach (var incomingModel in categoryGroup)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (string.IsNullOrEmpty(incomingModel.Class)) continue;

                    Element? liveElement = StandardTypeRegistry.ResolveTargetElement(incomingModel, target);
                    if (liveElement == null) continue; // No match found, skip

                    // Generate unique fake target ID for this standard element
#if REVIT2023
                    ElementId fakeTargetId = new ElementId((int)(fakeIdCounter--));
#else
                    ElementId fakeTargetId = new ElementId(fakeIdCounter--);
#endif

                    // Standard/POCO wrapper
                    var targetType = new DuplicateTypeModel
                    {
                        RevitTypeId = fakeTargetId.ToModel(target, false),
                        Name = incomingModel.Name,
                        Parameters = new Dictionary<string, string>()
                    };

                    // Live/Existing wrapper
                    var sourceType = new DuplicateTypeModel
                    {
                        RevitTypeId = liveElement.Id.ToModel(target, false),
                        Name = liveElement.Name,
                        Parameters = new Dictionary<string, string>()
                    };

                    var mapping = new TypeMappingModel
                    {
                        SourceType = sourceType,
                        TargetType = targetType,
                        RecommendedAction = RecommendedAction.Merge
                    };

                    // Compare parameters
                    if (incomingModel.Parameters != null)
                    {
                        foreach (var paramModel in incomingModel.Parameters)
                        {
                            if (string.IsNullOrEmpty(paramModel.Name)) continue;

                            Parameter? p = GetLiveParameter(liveElement, paramModel);
                            string srcStorage = p != null ? p.StorageType.ToString() : (paramModel.StorageType ?? "String");
                            string srcVal = p != null ? GetParameterValueWithoutPrefix(p) : string.Empty;

                            string tgtStorage = paramModel.StorageType ?? "String";
                            string tgtVal = GetJsonParameterValue(target, paramModel, _identityService);

                            bool isSchemaMismatch = (srcStorage != tgtStorage);
                            bool hasConflict = (srcVal != tgtVal);

                            sourceType.Parameters[paramModel.Name] = $"{srcStorage}:{srcVal}";
                            targetType.Parameters[paramModel.Name] = $"{tgtStorage}:{tgtVal}";

                            if (hasConflict || isSchemaMismatch)
                            {
                                var row = new ParameterDiffRowModel
                                {
                                    ParameterName = paramModel.Name,
                                    IsSchemaMismatch = isSchemaMismatch,
                                    HasConflict = hasConflict,
                                    WinningValueElementId = fakeTargetId.ToModel(target, false),
                                    IsInjectEnabled = (p == null)
                                };

                                row.Values[liveElement.Id.ToModel(target, false)] = srcVal;
                                row.Values[fakeTargetId.ToModel(target, false)] = tgtVal;

                                row.ValueList = new List<string> { srcVal, tgtVal };

                                row.Options = new List<ParameterValueOption>
                                {
                                    new ParameterValueOption { ElementId = liveElement.Id.ToModel(target, false), DisplayText = srcVal },
                                    new ParameterValueOption { ElementId = fakeTargetId.ToModel(target, false), DisplayText = tgtVal }
                                };

                                mapping.ParameterResolutions.Add(row);
                            }
                        }
                    }

                    if (mapping.ParameterResolutions.Count > 0)
                    {
                        var targetItem = cluster.Items.FirstOrDefault(item => item.IsPrimary);
                        if (targetItem == null)
                        {
                            targetItem = new DuplicateItemModel
                            {
                                RevitElementId = ElementId.InvalidElementId.ToModel(target, false),
                                ItemName = $"{incomingModel.Name} (Standard)",
                                CategoryName = categoryName,
                                IsPrimary = true,
                                IsIncludedForMerge = true,
                                IsLoadableFamily = (liveElement is Family)
                            };
                            targetItem.Types.Add(targetType);
                            cluster.Items.Add(targetItem);
                        }
                        else
                        {
                            targetItem.Types.Add(targetType);
                        }

                        var sourceItem = new DuplicateItemModel
                        {
                            RevitElementId = liveElement.Id.ToModel(target, false),
                            ItemName = liveElement.Name,
                            CategoryName = categoryName,
                            IsPrimary = false,
                            IsIncludedForMerge = true,
                            IsLoadableFamily = (liveElement is Family)
                        };
                        sourceItem.Types.Add(sourceType);
                        cluster.Items.Add(sourceItem);

                        mapping.SourceFamily = sourceItem;
                        mapping.TargetFamily = targetItem;

                        cluster.TypeMappings.Add(mapping);
                    }
                }

                if (cluster.TypeMappings.Count > 0)
                {
                    clusters.Add(cluster);
                }
            }

            int conflictCount = clusters.Sum(c => c.TypeMappings.Sum(m => m.ParameterResolutions.Count));
            // [AG2_TEST_START: StandardsDiffEngineTotalConflictsQA]
            // REVERT_METHOD: To remove, safely delete this entire block.
            Console.WriteLine($"Jrn.Directive \"SyntheticQA\", \"StandardsDiffEngine_TotalConflictsDetected: [{conflictCount}]\"");
            // [AG2_TEST_END: StandardsDiffEngineTotalConflictsQA]

            return clusters;
        }

        internal static Parameter? GetLiveParameter(Element elem, ParameterModel paramModel)
        {
            Parameter? param = null;
            if (paramModel.IsShared && !string.IsNullOrEmpty(paramModel.GUID))
            {
                try
                {
                    param = elem.get_Parameter(new Guid(paramModel.GUID));
                }
                catch {}
            }
            if (param == null && paramModel.Id < 0)
            {
                try
                {
                    param = elem.get_Parameter((BuiltInParameter)paramModel.Id);
                }
                catch {}
            }
            if (param == null)
            {
                param = elem.LookupParameter(paramModel.Name);
            }
            return param;
        }

        internal static string GetParameterValueWithoutPrefix(Parameter p)
        {
            if (p == null) return string.Empty;
            switch (p.StorageType)
            {
                case StorageType.Double:
                    return p.AsDouble().ToString();
                case StorageType.Integer:
                    return p.AsInteger().ToString();
                case StorageType.String:
                    return p.AsString() ?? string.Empty;
                case StorageType.ElementId:
                    ElementId id = p.AsElementId();
                    if (id != null)
                    {
#if REVIT2023
                        return id.IntegerValue.ToString();
#else
                        return id.Value.ToString();
#endif
                    }
                    break;
            }
            return string.Empty;
        }

        internal static string GetJsonParameterValue(Document doc, ParameterModel paramModel, IIdentityService identityService)
        {
            if (paramModel.StorageType == "ElementId")
            {
                ElementId targetId = ElementId.InvalidElementId;
                if (paramModel.ValueElemId != null)
                {
                    if (paramModel.ValueElemId.Name == "Solid")
                    {
                        targetId = LinePatternElement.GetSolidPatternId();
                    }
                    else
                    {
                        Element? resolvedElem = identityService.ResolveElement(paramModel.ValueElemId, doc);
                        if (resolvedElem != null)
                        {
                            targetId = resolvedElem.Id;
                        }
                    }
                }
                if (targetId != ElementId.InvalidElementId)
                {
#if REVIT2023
                    return targetId.IntegerValue.ToString();
#else
                    return targetId.Value.ToString();
#endif
                }
                return string.Empty;
            }
            return paramModel.Value ?? string.Empty;
        }
    }
}

