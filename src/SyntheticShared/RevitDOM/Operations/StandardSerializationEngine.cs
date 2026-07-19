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

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// The public interface and entry point for the RevitDOM serialization and transaction engine.
    /// Exposes methods to extract Revit elements to pure models, analyze differences, and write models back to Revit.
    /// </summary>
    public class StandardSerializationEngine : IStandardSerializationEngine
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
        /// Gets the internal identity service instance.
        /// </summary>
        internal IIdentityService IdentityService => _identityService;

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

        public IEnumerable<DuplicateClusterModel> Analyze(
            IEnumerable<ObjectModel> models,
            Document doc,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var diffEngine = new Synthetic.Modules.DiffEngine.PocoToRevitDiffEngine(_identityService);
            return diffEngine.Compare(models, doc, progress, cancellationToken);
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
            CancellationToken cancellationToken = default,
            IFailuresPreprocessor? failuresPreprocessor = null)
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

                            if (isUnchanged)
                            {
                                BindResultingElementToModel(element, elementModel, doc, aliases, modelsWithAliases);

                                results.Add(new SerializationResultModel(model, identityModel)
                                {
                                    Action = "Unchanged",
                                    Message = "Object is identical to the target model. Edit skipped."
                                });
                            }
                            else
                            {
                                using (var tx = new Transaction(doc, $"Import {modelName}"))
                                {
                                    if (failuresPreprocessor != null)
                                    {
                                        FailureHandlingOptions options = tx.GetFailureHandlingOptions();
                                        options.SetFailuresPreprocessor(failuresPreprocessor);
                                        tx.SetFailureHandlingOptions(options);
                                    }
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
                                        }

                                        BindResultingElementToModel(resultingElement, elementModel, doc, aliases, modelsWithAliases);

                                        tx.Commit();
                                        
                                        string actionStr = (element == null) ? "Created" : "Updated";
                                        results.Add(new SerializationResultModel(model, identityModel) { Action = actionStr });
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
                var incomingClone = (ElementModel)incomingModel.Clone();
                var liveClone = (ElementModel)liveModel.Clone();

                // Clear metadata/ignored fields on both clones
                ClearMetadataForComparison(incomingClone);
                ClearMetadataForComparison(liveClone);

                // Compare JSON representations of the non-parameter properties
                var settings = new JsonSerializerSettings
                {
                    ContractResolver = new IgnoreIdContractResolver(),
                    Formatting = Formatting.None
                };
                string json1 = JsonConvert.SerializeObject(incomingClone, settings);
                string json2 = JsonConvert.SerializeObject(liveClone, settings);

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

                    Parameter? p = Synthetic.Modules.DiffEngine.PocoToRevitDiffEngine.GetLiveParameter(liveElement, paramModel);
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
                    string srcVal = Synthetic.Modules.DiffEngine.PocoToRevitDiffEngine.GetParameterValueWithoutPrefix(p);

                    string tgtStorage = paramModel.StorageType ?? "String";
                    string tgtVal = Synthetic.Modules.DiffEngine.PocoToRevitDiffEngine.GetJsonParameterValue(doc, paramModel, _identityService);

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

        private void ClearMetadataForComparison(ElementModel model)
        {
            if (model == null) return;
            model.Parameters = null;
            model.Element = null;
            model.Document = null;
            model.DependencyOrigin = null;
        }

        private ElementId BindResultingElementToModel(object resultingElement, ElementModel elementModel, Document doc, List<string>? aliases, List<(ElementId PrimaryId, ElementModel Model)> modelsWithAliases)
        {
            ElementId primaryId = ElementId.InvalidElementId;
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
            else
            {
                throw new InvalidOperationException($"Resulting object '{resultingElement.GetType().Name}' is neither a Revit Element nor a Category.");
            }

            // Restore aliases list on the elementModel so it is not lost
            if (aliases != null)
            {
                elementModel.Aliases = aliases;
            }

            // Queue for alias processing if successful and aliases exist
            if (aliases != null && aliases.Count > 0 && primaryId != ElementId.InvalidElementId)
            {
                modelsWithAliases.Add((primaryId, elementModel));
            }

            return primaryId;
        }

        public ObjectModel? ExtractCategory(Autodesk.Revit.DB.Category category, Document doc, bool isTemplate)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            return _dispatcher.Extract(category, doc, isTemplate);
        }
    }
}
