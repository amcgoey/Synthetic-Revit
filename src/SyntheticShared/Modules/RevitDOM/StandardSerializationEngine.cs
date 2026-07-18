using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// The public interface and entry point for the RevitDOM serialization and transaction engine.
    /// Exposes methods to extract Revit elements to pure models, analyze differences, and write models back to Revit.
    /// </summary>
    public class StandardSerializationEngine
    {
        private readonly IIdentityService _identityService;
        private readonly ModelDispatcher _dispatcher;

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardSerializationEngine"/> class.
        /// </summary>
        public StandardSerializationEngine() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardSerializationEngine"/> class with a custom identity service.
        /// </summary>
        internal StandardSerializationEngine(IIdentityService? identityService)
        {
            _identityService = identityService ?? new RevitIdentityService();
            _dispatcher = new ModelDispatcher(_identityService);

            // Register primitive translators
            _dispatcher.Register<LinePatternElementModel, LinePatternTranslator>(typeof(LinePatternElement));
            _dispatcher.Register<FillPatternElementModel, FillPatternTranslator>(typeof(FillPatternElement));
            _dispatcher.Register<ParameterFilterElementModel, ParameterFilterElementTranslator>(new ParameterFilterElementTranslator(_identityService), typeof(ParameterFilterElement));
            _dispatcher.Register<FilledRegionTypeModel, FilledRegionTypeTranslator>(new FilledRegionTypeTranslator(_identityService), typeof(FilledRegionType));
            _dispatcher.Register<ParameterElementModel, ParameterElementTranslator>(typeof(ParameterElement), typeof(SharedParameterElement));
            _dispatcher.Register<BrowserOrganizationModel, BrowserOrganizationTranslator>(typeof(BrowserOrganization));
            var hostObjTypeTranslator = new HostObjTypeTranslator(_identityService);
#if REVIT2022 || REVIT2023
            _dispatcher.Register<HostObjTypeModel, HostObjTypeTranslator>(
                hostObjTypeTranslator,
                typeof(WallType), typeof(FloorType), typeof(RoofType), typeof(CeilingType), typeof(BuildingPadType),
                typeof(CurtainSystemType), typeof(MullionType), typeof(FasciaType), typeof(GutterType));
#else
            _dispatcher.Register<HostObjTypeModel, HostObjTypeTranslator>(
                hostObjTypeTranslator,
                typeof(WallType), typeof(FloorType), typeof(RoofType), typeof(CeilingType), typeof(BuildingPadType),
                typeof(CurtainSystemType), typeof(MullionType), typeof(FasciaType), typeof(GutterType),
                typeof(ToposolidType));
#endif

            // Register view translators
            var viewTranslator = new ViewTranslator(_identityService);
            _dispatcher.Register<ViewModel, ViewTranslator>(viewTranslator, typeof(ViewDrafting), typeof(ViewSection));
            _dispatcher.Register<ViewPlanModel, ViewPlanTranslator>(new ViewPlanTranslator(_identityService), typeof(ViewPlan));
            _dispatcher.Register<ViewSheetModel, ViewSheetTranslator>(new ViewSheetTranslator(_identityService), typeof(ViewSheet));
            _dispatcher.Register<ViewScheduleModel, ViewScheduleTranslator>(new ViewScheduleTranslator(_identityService), typeof(ViewSchedule));

            // Register remaining translators
            _dispatcher.Register<MaterialModel, MaterialTranslator>(new MaterialTranslator(_identityService), typeof(Material));
            _dispatcher.Register<CategoryModel, CategoryTranslator>(new CategoryTranslator(_identityService), typeof(Autodesk.Revit.DB.Category));
            _dispatcher.Register<DimensionTypeModel, DimensionTypeTranslator>(typeof(DimensionType));
            _dispatcher.Register<GridTypeModel, GridTypeTranslator>(typeof(GridType));
            _dispatcher.Register<LevelTypeModel, LevelTypeTranslator>(typeof(LevelType));
            var textElementTypeTranslator = new TextElementTypeTranslator();
            _dispatcher.Register<ElementTypeModel, TextElementTypeTranslator>(
                textElementTypeTranslator,
                typeof(TextNoteType), typeof(TextElementType), typeof(ModelTextType));
            _dispatcher.Register<ElementTypeModel, SpotDimensionTypeTranslator>(
                new SpotDimensionTypeTranslator(),
                typeof(SpotDimensionType));

            // Register Ignored types
            _dispatcher.RegisterIgnored(
                typeof(InternalOrigin),
                typeof(BasePoint),
                typeof(SketchPlane),
                typeof(ProjectLocation),
                typeof(NumberingSchema));

            // Register Pending types
            _dispatcher.RegisterPending(
                typeof(PhaseFilter),
                typeof(Phase),
                typeof(Revision),
                typeof(RevisionSettings),
                typeof(RevisionNumberingSequence),
                typeof(AreaScheme),
                typeof(ColorFillScheme),
                typeof(SunAndShadowSettings),
                typeof(WorksetDefaultVisibilitySettings),
                typeof(RailingType),
                typeof(StairsType),
                typeof(StairsLandingType),
                typeof(StairsRunType));
        }

        /// <summary>
        /// Gets the internal dispatcher orchestrating the type-specific translators.
        /// </summary>
        internal ModelDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// Extracts Revit elements into pure ObjectModel state containers.
        /// </summary>
        /// <param name="elements">The elements to extract.</param>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="isTemplate">Flag indicating if the elements should be extracted as document-agnostic templates.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of extracted ObjectModel instances.</returns>
        public IEnumerable<ObjectModel> ByRevit(
            IEnumerable<Element> elements,
            Document doc,
            bool isTemplate,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (elements == null) throw new ArgumentNullException(nameof(elements));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var results = new List<ObjectModel>();
            foreach (var elem in elements)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (elem == null) continue;

                string elemName = elem.Name;
                progress?.Report($"Extracting {elemName}...");

                var model = _dispatcher.Extract(elem, isTemplate);
                if (model != null)
                {
                    results.Add(model);
                }
            }

            return results;
        }

        /// <summary>
        /// Analyzes a set of ObjectModel instances against the live Revit database and returns diff/duplicate report clusters.
        /// </summary>
        /// <param name="models">The incoming models to analyze.</param>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of duplicate cluster models containing diff details.</returns>
        private static readonly HashSet<Type> _unsupportedInFamilyDoc = new HashSet<Type>
        {
            typeof(Autodesk.Revit.DB.WallType),
            typeof(Autodesk.Revit.DB.FloorType),
            typeof(Autodesk.Revit.DB.RoofType),
            typeof(Autodesk.Revit.DB.CeilingType),
            typeof(Autodesk.Revit.DB.BuildingPadType),
            typeof(Autodesk.Revit.DB.MullionType),
            typeof(Autodesk.Revit.DB.CurtainSystemType),
            typeof(Autodesk.Revit.DB.ViewFamilyType),
            typeof(Autodesk.Revit.DB.Architecture.FasciaType),
            typeof(Autodesk.Revit.DB.Architecture.GutterType),
            typeof(Autodesk.Revit.DB.Architecture.StairsType),
            typeof(Autodesk.Revit.DB.Architecture.RailingType),
            typeof(Autodesk.Revit.DB.Architecture.TopRailType),
            typeof(Autodesk.Revit.DB.Architecture.HandRailType),
            typeof(Autodesk.Revit.DB.Mechanical.DuctSystemType),
            typeof(Autodesk.Revit.DB.Plumbing.PipingSystemType)
        };

        public IEnumerable<DuplicateClusterModel> Analyze(
            IEnumerable<ObjectModel> models,
            Document doc,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var clusters = new List<DuplicateClusterModel>();
            bool isFamily = doc.IsFamilyDocument;
            long fakeIdCounter = -1000;

            progress?.Report("Grouping incoming models by Category...");

            var elementModels = models.OfType<ElementModel>();
            var modelsByCategory = elementModels
                .Where(m =>
                {
                    if (string.IsNullOrEmpty(m.Class)) return false;
                    Type? incomingType = Select.RevitClassByString(m.Class);
                    if (isFamily && incomingType != null && _unsupportedInFamilyDoc.Contains(incomingType))
                    {
                        return false; // Skip unsupported types in family document
                    }
                    return true;
                })
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
                    Type? elemClass = Select.RevitClassByString(incomingModel.Class);
                    if (elemClass == null) continue;

                    Element? liveElement = Select.ElementByNameClass(incomingModel.Name, elemClass, doc);
                    if (liveElement != null)
                    {
                        string? liveCategoryName = liveElement.Category?.Name;
                        string incomingCategoryName = incomingModel.Category;
                        bool categoryMatches = string.IsNullOrEmpty(incomingCategoryName) || 
                                              string.IsNullOrEmpty(liveCategoryName) || 
                                              string.Equals(liveCategoryName, incomingCategoryName, StringComparison.OrdinalIgnoreCase);
                        if (!categoryMatches)
                        {
                            liveElement = null; // Mismatch on category, skip
                        }
                    }

                    if (liveElement == null) continue; // No match found, skip

                    // Generate unique fake target ID for this standard element
#if REVIT2022 || REVIT2023
                    ElementId fakeTargetId = new ElementId((int)(fakeIdCounter--));
#else
                    ElementId fakeTargetId = new ElementId(fakeIdCounter--);
#endif

                    // Standard/POCO wrapper
                    var targetType = new DuplicateTypeModel
                    {
                        RevitTypeId = fakeTargetId,
                        Name = incomingModel.Name,
                        Parameters = new Dictionary<string, string>()
                    };

                    // Live/Existing wrapper
                    var sourceType = new DuplicateTypeModel
                    {
                        RevitTypeId = liveElement.Id,
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
                            string tgtVal = GetJsonParameterValue(doc, paramModel, _identityService);

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
                                    WinningValueElementId = fakeTargetId,
                                    IsInjectEnabled = (p == null)
                                };

                                row.Values[liveElement.Id] = srcVal;
                                row.Values[fakeTargetId] = tgtVal;

                                row.ValueList = new List<string> { srcVal, tgtVal };

                                row.Options = new List<ParameterValueOption>
                                {
                                    new ParameterValueOption { ElementId = liveElement.Id, DisplayText = srcVal },
                                    new ParameterValueOption { ElementId = fakeTargetId, DisplayText = tgtVal }
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
                                RevitElementId = ElementId.InvalidElementId,
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
                            RevitElementId = liveElement.Id,
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

        private static Parameter? GetLiveParameter(Element elem, ParameterModel paramModel)
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

        private static string GetParameterValueWithoutPrefix(Parameter p)
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
#if REVIT2022 || REVIT2023
                        return id.IntegerValue.ToString();
#else
                        return id.Value.ToString();
#endif
                    }
                    break;
            }
            return string.Empty;
        }

        private static string GetJsonParameterValue(Document doc, ParameterModel paramModel, IIdentityService identityService)
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
                if (targetId != null)
                {
#if REVIT2022 || REVIT2023
                    return targetId.IntegerValue.ToString();
#else
                    return targetId.Value.ToString();
#endif
                }
                return string.Empty;
            }
            return paramModel.Value ?? string.Empty;
        }

        /// <summary>
        /// Writes ObjectModel instances back to the Revit database, managing transaction assimilation and rollbacks.
        /// </summary>
        /// <param name="models">The models to inject/write.</param>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of serialization results.</returns>
        public IEnumerable<SerializationResultModel> ToRevit(
            IEnumerable<ObjectModel> models,
            Document doc,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (models == null) throw new ArgumentNullException(nameof(models));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var results = new List<SerializationResultModel>();
            var sortedModels = ImportExecutionRunner.Sort(models);

            // Establish the deferred post-commit queue
            var modelsWithAliases = new List<(ElementId PrimaryId, ElementModel Model)>();

            using (var txGroup = new TransactionGroup(doc, "Import Models"))
            {
                txGroup.Start();
                try
                {
                    foreach (var model in sortedModels)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            txGroup.RollBack();
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        string modelName = (model is ElementModel em) ? em.Name : model.GetType().Name;
                        progress?.Report($"Importing {modelName}...");

                        var translator = _dispatcher.GetTranslatorByModelType(model.GetType());
                        if (translator == null)
                        {
                            results.Add(new SerializationResultModel(model, $"No translator registered for model type: {model.GetType().Name}"));
                            continue;
                        }

                        if (model is ElementModel elementModel)
                        {
                            SerializationResultModel.ClearWarnings();
                            var identityModel = elementModel.ElementId;

                            // Preserve aliases list before it gets cleared by identity model updates
                            var aliases = elementModel.Aliases != null ? new List<string>(elementModel.Aliases) : null;

                            // Resolve the primary element using a clean identity model copy without aliases
                            // to prevent resolving the primary element to its own alias elements.
                            var resolveIdentity = new ElementIdModel
                            {
                                Id = identityModel.Id,
                                Name = identityModel.Name,
                                Class = identityModel.Class,
                                Category = identityModel.Category,
                                UniqueId = identityModel.UniqueId,
                                IsTemplate = identityModel.IsTemplate
                            };
                            var element = _identityService.ResolveElement(resolveIdentity, doc);

                            bool isUnchanged = false;
                            if (element != null)
                            {
                                isUnchanged = IsElementSameAsModel(elementModel, element, doc);
                            }

                            ElementId primaryId = ElementId.InvalidElementId;

                            if (isUnchanged)
                            {
                                object resultingElement = element;
                                if (resultingElement is Element revitElement)
                                {
                                    elementModel.Element = revitElement;
                                    elementModel.ElementId = _identityService.ToModel(revitElement.Id, doc);
                                    primaryId = revitElement.Id;
                                }
                                else if (resultingElement is Autodesk.Revit.DB.Category revitCategory)
                                {
                                    elementModel.Element = revitCategory;
                                    elementModel.ElementId = _identityService.ToModel(revitCategory.Id, doc);
                                    primaryId = revitCategory.Id;

                                    if (elementModel is CategoryModel categoryModel)
                                    {
                                        categoryModel.RevitCategory = revitCategory;
#if REVIT2022 || REVIT2023
                                        categoryModel.CategoryId.Id = revitCategory.Id.IntegerValue;
#else
                                        categoryModel.CategoryId.Id = revitCategory.Id.Value;
#endif
                                        categoryModel.CategoryId.Name = revitCategory.Name;
                                    }
                                }

                                // Restore aliases list on the elementModel so it is not lost
                                if (aliases != null)
                                {
                                    elementModel.Aliases = aliases;
                                }

                                results.Add(new SerializationResultModel(model, identityModel)
                                {
                                    Action = "Unchanged",
                                    Message = "Object is identical to the target model. Edit skipped."
                                });

                                // Queue for alias processing if successful and aliases exist
                                if (aliases != null && aliases.Count > 0 && primaryId != ElementId.InvalidElementId)
                                {
                                    modelsWithAliases.Add((primaryId, elementModel));
                                }
                            }
                            else
                            {
                                using (var tx = new Transaction(doc, $"Import {modelName}"))
                                {
                                    tx.Start();
                                    try
                                    {
                                        // Inject specific properties and handle creation
                                        var resultingElement = translator.InjectSpecifics(elementModel, element, doc);
                                        if (resultingElement == null)
                                        {
                                            throw new InvalidOperationException($"Element '{modelName}' could not be resolved or created.");
                                        }

                                        if (resultingElement is Element revitElement)
                                        {
                                            // Inject standard base parameters
                                            ParameterEngine.InjectParameters(elementModel, revitElement, _identityService);

                                            // Update model bindings
                                            elementModel.Element = revitElement;
                                            elementModel.ElementId = _identityService.ToModel(revitElement.Id, doc);
                                            primaryId = revitElement.Id;
                                        }
                                        else if (resultingElement is Autodesk.Revit.DB.Category revitCategory)
                                        {
                                            // Update model bindings
                                            elementModel.Element = revitCategory;
                                            elementModel.ElementId = _identityService.ToModel(revitCategory.Id, doc);
                                            primaryId = revitCategory.Id;

                                            if (elementModel is CategoryModel categoryModel)
                                            {
                                                categoryModel.RevitCategory = revitCategory;
#if REVIT2022 || REVIT2023
                                                categoryModel.CategoryId.Id = revitCategory.Id.IntegerValue;
#else
                                                categoryModel.CategoryId.Id = revitCategory.Id.Value;
#endif
                                                categoryModel.CategoryId.Name = revitCategory.Name;
                                            }
                                        }
                                        else
                                        {
                                            throw new InvalidOperationException($"Resulting object '{resultingElement.GetType().Name}' is neither a Revit Element nor a Category.");
                                        }

                                        // Restore aliases list on the elementModel so it is not lost
                                        if (aliases != null)
                                        {
                                            elementModel.Aliases = aliases;
                                        }

                                        tx.Commit();
                                        
                                        string actionStr = (element == null) ? "Created" : "Updated";
                                        results.Add(new SerializationResultModel(model, identityModel) { Action = actionStr });

                                        // Queue for alias processing if successful and aliases exist
                                        if (aliases != null && aliases.Count > 0 && primaryId != ElementId.InvalidElementId)
                                        {
                                            modelsWithAliases.Add((primaryId, elementModel));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        tx.RollBack();
                                        results.Add(new SerializationResultModel(model, ex.Message, ex));
                                    }
                                }
                            }
                        }
                        else
                        {
                            results.Add(new SerializationResultModel(model, "Model must inherit from ElementModel to be imported."));
                        }
                    }

                    txGroup.Assimilate();
                }
                catch (OperationCanceledException)
                {
                    if (txGroup.GetStatus() == TransactionStatus.Started)
                    {
                        txGroup.RollBack();
                    }
                    throw;
                }
                catch (Exception)
                {
                    if (txGroup.GetStatus() == TransactionStatus.Started)
                    {
                        txGroup.RollBack();
                    }
                    throw;
                }
            }

            // Post-commit alias processing
            foreach (var queuedItem in modelsWithAliases)
            {
                var primaryId = queuedItem.PrimaryId;
                var model = queuedItem.Model;

                if (model.Aliases == null) continue;

                foreach (var aliasString in model.Aliases)
                {
                    if (string.IsNullOrEmpty(aliasString)) continue;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    try
                    {
                        // Resolve the redundant alias element
                        var aliasIdModel = new ElementIdModel
                        {
                            Name = aliasString,
                            Class = model.Class
                        };
                        ElementId aliasId = _identityService.ResolveElementId(aliasIdModel, doc);

                        if (aliasId == null || aliasId == ElementId.InvalidElementId)
                        {
                            // Skip silently if the alias does not exist in the document
                            continue;
                        }

                        if (aliasId != primaryId)
                        {
                            // Deep-swap references using AliasSwapEngine (retains internal transaction boundaries)
                            AliasSwapEngine.SwapElementReferences(doc, aliasId, primaryId);

                            // Delete the redundant alias element wrapped in a transaction
                            using (var txDelete = new Transaction(doc, "Purge Alias Element"))
                            {
                                txDelete.Start();
                                doc.Delete(aliasId);
                                txDelete.Commit();
                            }

                            // Log success result
                            var successResult = new SerializationResultModel(model, _identityService.ToModel(primaryId, doc))
                            {
                                Action = "Merged Alias",
                                Message = $"Successfully swapped and purged alias '{aliasString}'"
                            };
                            results.Add(successResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Graceful degradation: catch local exception and log failure, leaving primary element intact
                        var failureResult = new SerializationResultModel(model, ex.Message, ex)
                        {
                            Action = "Alias Swap Failed",
                            Message = $"Failed to swap and purge alias '{aliasString}': {ex.Message}"
                        };
                        results.Add(failureResult);
                    }
                }
            }

            return results;
        }
        private class IgnoreIdContractResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type, Newtonsoft.Json.MemberSerialization memberSerialization)
            {
                IList<Newtonsoft.Json.Serialization.JsonProperty> properties = base.CreateProperties(type, memberSerialization);
                
                properties = properties.Where(p =>
                {
                    bool isIdOrUniqueId = p.PropertyName == "Id" || p.PropertyName == "UniqueId";
                    bool isTargetType = typeof(ElementIdModel).IsAssignableFrom(type) || 
                                       typeof(ElementModel).IsAssignableFrom(type);
                    return !(isIdOrUniqueId && isTargetType);
                }).ToList();

                return properties;
            }
        }

        private bool IsElementSameAsModel(ElementModel incomingModel, Element liveElement, Document doc)
        {
            // 1. Extract the live element into an ElementModel to compare translator-specific properties
            try
            {
                var liveModel = _dispatcher.Extract(liveElement, isTemplate: true) as ElementModel;
                if (liveModel == null) return false;

                // Clone both models to avoid modifying the original structures
                var c1 = (ElementModel)incomingModel.Clone();
                var c2 = (ElementModel)liveModel.Clone();

                // Set metadata/ignored fields to null/default on both clones
                c1.Parameters = null;
                c2.Parameters = null;
                c1.Element = null;
                c2.Element = null;
                c1.Document = null;
                c2.Document = null;
                c1.ElementId = null;
                c2.ElementId = null;
                c1.Id = 0;
                c2.Id = 0;
                c1.UniqueId = null;
                c2.UniqueId = null;
                c1.DependencyOrigin = null;
                c2.DependencyOrigin = null;

                // Compare JSON representations of the non-parameter properties
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new IgnoreIdContractResolver(),
                    Formatting = Formatting.None
                };
                string json1 = JsonConvert.SerializeObject(c1, settings);
                string json2 = JsonConvert.SerializeObject(c2, settings);

                if (json1 != json2)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                // Fallback to false if extraction/serialization errors out to be safe
                return false;
            }

            // 2. Compare parameters
            if (incomingModel.Parameters != null)
            {
                foreach (var paramModel in incomingModel.Parameters)
                {
                    if (string.IsNullOrEmpty(paramModel.Name)) continue;
                    if (paramModel.IsReadOnly) continue;

                    Parameter? p = GetLiveParameter(liveElement, paramModel);
                    if (p == null)
                    {
                        // Check if incoming parameter specifies a non-empty target value
                        bool hasTargetValue = (paramModel.Value != null) ||
                                             (paramModel.ValueElemId != null &&
                                              (!string.IsNullOrEmpty(paramModel.ValueElemId.Name) ||
                                               !string.IsNullOrEmpty(paramModel.ValueElemId.UniqueId) ||
                                               paramModel.ValueElemId.Id != 0));
                        if (hasTargetValue)
                        {
                            return false;
                        }
                        continue;
                    }

                    string srcStorage = p.StorageType.ToString();
                    string srcVal = GetParameterValueWithoutPrefix(p);

                    string tgtStorage = paramModel.StorageType ?? "String";
                    string tgtVal = GetJsonParameterValue(doc, paramModel, _identityService);

                    bool isSchemaMismatch = (srcStorage != tgtStorage);
                    bool hasConflict = (srcVal != tgtVal);

                    if (isSchemaMismatch || hasConflict)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
