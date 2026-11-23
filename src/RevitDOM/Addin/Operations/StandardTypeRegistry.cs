using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Synthetic.Platform.Revit;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Binds a RevitDOM ObjectModel type and one or more Revit Element types to a translator factory.
    /// </summary>
    internal sealed class TranslatorBinding
    {
        public Type ModelType { get; }
        public Func<IIdentityService, IModelTranslator> CreateTranslator { get; }
        public IReadOnlyList<Type> RevitTypes { get; }
        public IReadOnlyList<string> ClassAliases { get; }

        public TranslatorBinding(
            Type modelType,
            Func<IIdentityService, IModelTranslator> createTranslator,
            IReadOnlyList<Type> revitTypes,
            IReadOnlyList<string>? classAliases = null)
        {
            ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
            CreateTranslator = createTranslator ?? throw new ArgumentNullException(nameof(createTranslator));
            RevitTypes = revitTypes ?? Array.Empty<Type>();
            ClassAliases = classAliases ?? Array.Empty<string>();
        }
    }

    internal sealed class StandardTypeRevitBinding
    {
        public Type? RevitType { get; }
        public IReadOnlyList<TranslatorBinding> TranslatorBindings { get; }
        public Func<Document, IReadOnlyCollection<string>?, CancellationToken, IEnumerable<Element>>? RootCollector { get; }

        public StandardTypeRevitBinding(
            Type? revitType,
            IReadOnlyList<TranslatorBinding>? translatorBindings = null,
            Func<Document, IReadOnlyCollection<string>?, CancellationToken, IEnumerable<Element>>? rootCollector = null)
        {
            RevitType = revitType;
            TranslatorBindings = translatorBindings ?? Array.Empty<TranslatorBinding>();
            RootCollector = rootCollector;
        }
    }

    /// <summary>
    /// Revit-connected registry facade composing pure <see cref="StandardTypeMetadataRegistry"/> metadata
    /// with live Revit types, translator bindings, root collectors, and element disambiguation.
    /// </summary>
    internal static class StandardTypeRegistry
    {
        private static readonly Dictionary<string, StandardTypeRevitBinding> _revitBindings;

        public static readonly IReadOnlyList<Type> IgnoredRevitTypes = new[]
        {
            typeof(InternalOrigin),
            typeof(BasePoint),
            typeof(SketchPlane),
            typeof(ProjectLocation),
            typeof(NumberingSchema)
        };

        public static readonly IReadOnlyList<Type> PendingRevitTypes = new[]
        {
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
            typeof(StairsRunType)
        };

        static StandardTypeRegistry()
        {
#if REVIT2023
            Type? toposolidType = null;
            TranslatorBinding[] toposolidBindings = Array.Empty<TranslatorBinding>();
#else
            Type? toposolidType = typeof(ToposolidType);
            TranslatorBinding[] toposolidBindings = new[]
            {
                new TranslatorBinding(
                    typeof(HostObjTypeModel),
                    id => new HostObjTypeTranslator(id),
                    new[] { typeof(ToposolidType) },
                    new[] { "Autodesk.Revit.DB.ToposolidType" })
            };
#endif

            _revitBindings = new Dictionary<string, StandardTypeRevitBinding>(StringComparer.OrdinalIgnoreCase)
            {
                ["FillPatternElements"] = new StandardTypeRevitBinding(
                    typeof(FillPatternElement),
                    new[] { new TranslatorBinding(typeof(FillPatternElementModel), _ => new FillPatternTranslator(), new[] { typeof(FillPatternElement) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(FillPatternElement), "Fill Patterns", groupings, ct)),

                ["LinePatternElements"] = new StandardTypeRevitBinding(
                    typeof(LinePatternElement),
                    new[] { new TranslatorBinding(typeof(LinePatternElementModel), _ => new LinePatternTranslator(), new[] { typeof(LinePatternElement) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(LinePatternElement), "Line Patterns", groupings, ct)),

                ["PropertySetElements"] = new StandardTypeRevitBinding(
                    typeof(PropertySetElement)),

                ["Materials"] = new StandardTypeRevitBinding(
                    typeof(Material),
                    new[] { new TranslatorBinding(typeof(MaterialModel), id => new MaterialTranslator(id), new[] { typeof(Material) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(Material), "Materials", groupings, ct)),

                ["ElementTypes"] = new StandardTypeRevitBinding(
                    typeof(FamilySymbol),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new FamilySymbolTranslator(), new[] { typeof(FamilySymbol) }) },
                    CollectFamilySymbols),

                ["Categories"] = new StandardTypeRevitBinding(
                    typeof(Autodesk.Revit.DB.Category),
                    new[] { new TranslatorBinding(typeof(CategoryModel), id => new CategoryTranslator(id), new[] { typeof(Autodesk.Revit.DB.Category) }) }),

                ["HostObjTypes"] = new StandardTypeRevitBinding(
                    typeof(WallType),
                    new[]
                    {
                        new TranslatorBinding(
                            typeof(HostObjTypeModel),
                            id => new HostObjTypeTranslator(id),
                            new[] { typeof(WallType), typeof(FloorType), typeof(RoofType), typeof(CeilingType), typeof(BuildingPadType) },
                            new[] { "Autodesk.Revit.DB.HostObjAttributes" })
                    },
                    CollectHostObjTypes),

                ["FilledRegionTypes"] = new StandardTypeRevitBinding(
                    typeof(FilledRegionType),
                    new[] { new TranslatorBinding(typeof(FilledRegionTypeModel), id => new FilledRegionTypeTranslator(id), new[] { typeof(FilledRegionType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(FilledRegionType), "Filled Region Types", groupings, ct)),

                ["DimensionTypes"] = new StandardTypeRevitBinding(
                    typeof(DimensionType),
                    new[] { new TranslatorBinding(typeof(DimensionTypeModel), _ => new DimensionTypeTranslator(), new[] { typeof(DimensionType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(DimensionType), "Dimension Types", groupings, ct)),

                ["TextNoteTypes"] = new StandardTypeRevitBinding(
                    typeof(TextNoteType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(TextNoteType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(TextNoteType), "Text Note Types", groupings, ct)),

                ["ViewTemplates"] = new StandardTypeRevitBinding(
                    typeof(Autodesk.Revit.DB.View),
                    rootCollector: (doc, groupings, ct) => CollectViews(doc, isTemplate: true, "View Templates", groupings, ct)),

                ["Views"] = new StandardTypeRevitBinding(
                    typeof(Autodesk.Revit.DB.View),
                    new[]
                    {
                        new TranslatorBinding(
                            typeof(ViewModel),
                            id => new ViewTranslator(id),
                            new[] { typeof(ViewDrafting), typeof(ViewSection) },
                            new[] { "Autodesk.Revit.DB.View", "Autodesk.Revit.DB.View3D" }),
                        new TranslatorBinding(typeof(ViewPlanModel), id => new ViewPlanTranslator(id), new[] { typeof(ViewPlan) }),
                        new TranslatorBinding(typeof(ViewSheetModel), id => new ViewSheetTranslator(id), new[] { typeof(ViewSheet) }),
                        new TranslatorBinding(typeof(ViewScheduleModel), id => new ViewScheduleTranslator(id), new[] { typeof(ViewSchedule) })
                    },
                    (doc, groupings, ct) => CollectViews(doc, isTemplate: false, "Views", groupings, ct)),

                ["GridTypes"] = new StandardTypeRevitBinding(
                    typeof(GridType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new GridTypeTranslator(), new[] { typeof(GridType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(GridType), "Grid Types", groupings, ct)),

                ["LevelTypes"] = new StandardTypeRevitBinding(
                    typeof(LevelType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new LevelTypeTranslator(), new[] { typeof(LevelType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(LevelType), "Level Types", groupings, ct)),

                ["CurtainSystemTypes"] = new StandardTypeRevitBinding(
                    typeof(CurtainSystemType),
                    new[] { new TranslatorBinding(typeof(HostObjTypeModel), id => new HostObjTypeTranslator(id), new[] { typeof(CurtainSystemType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(CurtainSystemType), "Curtain System Types", groupings, ct)),

                ["MullionTypes"] = new StandardTypeRevitBinding(
                    typeof(MullionType),
                    new[] { new TranslatorBinding(typeof(HostObjTypeModel), id => new HostObjTypeTranslator(id), new[] { typeof(MullionType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(MullionType), "Mullion Types", groupings, ct)),

                ["FasciaTypes"] = new StandardTypeRevitBinding(
                    typeof(FasciaType),
                    new[]
                    {
                        new TranslatorBinding(
                            typeof(HostObjTypeModel),
                            id => new HostObjTypeTranslator(id),
                            new[] { typeof(FasciaType) },
                            new[] { "Autodesk.Revit.DB.Architecture.FasciaType", "Autodesk.Revit.DB.FasciaType" })
                    },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(FasciaType), "Fascia Types", groupings, ct)),

                ["GutterTypes"] = new StandardTypeRevitBinding(
                    typeof(GutterType),
                    new[]
                    {
                        new TranslatorBinding(
                            typeof(HostObjTypeModel),
                            id => new HostObjTypeTranslator(id),
                            new[] { typeof(GutterType) },
                            new[] { "Autodesk.Revit.DB.Architecture.GutterType", "Autodesk.Revit.DB.GutterType" })
                    },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(GutterType), "Gutter Types", groupings, ct)),

                ["ViewFamilyTypes"] = new StandardTypeRevitBinding(
                    typeof(ViewFamilyType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new ViewFamilyTypeTranslator(), new[] { typeof(ViewFamilyType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(ViewFamilyType), "View Family Types", groupings, ct)),

                ["BrowserOrganizations"] = new StandardTypeRevitBinding(
                    typeof(BrowserOrganization),
                    new[] { new TranslatorBinding(typeof(BrowserOrganizationModel), _ => new BrowserOrganizationTranslator(), new[] { typeof(BrowserOrganization) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(BrowserOrganization), "Browser Organizations", groupings, ct)),

                ["ParameterElements"] = new StandardTypeRevitBinding(
                    typeof(ParameterElement),
                    new[]
                    {
                        new TranslatorBinding(
                            typeof(ParameterElementModel),
                            _ => new ParameterElementTranslator(),
                            new[] { typeof(ParameterElement), typeof(SharedParameterElement) },
                            new[] { "Autodesk.Revit.DB.ParameterElement", "Autodesk.Revit.DB.SharedParameterElement" })
                    },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(ParameterElement), "Shared Parameters", groupings, ct)),

                ["TextElementTypes"] = new StandardTypeRevitBinding(
                    typeof(TextElementType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(TextElementType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(TextElementType), "Label Types", groupings, ct)),

                ["ModelTextTypes"] = new StandardTypeRevitBinding(
                    typeof(ModelTextType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(ModelTextType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(ModelTextType), "Model Text Types", groupings, ct)),

                ["ParameterFilters"] = new StandardTypeRevitBinding(
                    typeof(ParameterFilterElement),
                    new[] { new TranslatorBinding(typeof(ParameterFilterElementModel), id => new ParameterFilterElementTranslator(id), new[] { typeof(ParameterFilterElement) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(ParameterFilterElement), "Filters", groupings, ct)),

                ["SpotDimensionTypes"] = new StandardTypeRevitBinding(
                    typeof(SpotDimensionType),
                    new[] { new TranslatorBinding(typeof(ElementTypeModel), _ => new SpotDimensionTypeTranslator(), new[] { typeof(SpotDimensionType) }) },
                    (doc, groupings, ct) => CollectOfClass(doc, typeof(SpotDimensionType), "Spot Dimension Types", groupings, ct)),

                ["ToposolidTypes"] = new StandardTypeRevitBinding(
                    toposolidType,
                    toposolidBindings,
                    (doc, groupings, ct) => CollectOfClass(doc, toposolidType, "Toposolid Types", groupings, ct))
            };
        }

        public static string FormatCompositeKey(string? prefix, string? name) =>
            StandardTypeMetadataRegistry.FormatCompositeKey(prefix, name);

        public static IReadOnlyList<StandardTypeDescriptor> GetAllDescriptors() =>
            StandardTypeMetadataRegistry.GetAllDescriptors();

        public static StandardTypeDescriptor? GetDescriptorByBucket(string bucketName) =>
            StandardTypeMetadataRegistry.GetDescriptorByBucket(bucketName);

        public static StandardTypeDescriptor? GetDescriptorByClass(string? className) =>
            StandardTypeMetadataRegistry.GetDescriptorByClass(className);

        public static StandardTypeDescriptor? GetDescriptorByModel(ObjectModel? model) =>
            StandardTypeMetadataRegistry.GetDescriptorByModel(model);

        public static int GetTier(ObjectModel? model) =>
            StandardTypeMetadataRegistry.GetTier(model);

        internal static DocumentScope GetDocumentScope(ObjectModel? model) =>
            StandardTypeMetadataRegistry.GetDocumentScope(model);

        internal static bool IsInDocumentScope(ObjectModel? model, bool isFamilyDocument) =>
            StandardTypeMetadataRegistry.IsInDocumentScope(model, isFamilyDocument);

        public static (string Group, string Class) GetTaxonomy(ElementModel? element) =>
            StandardTypeMetadataRegistry.GetTaxonomy(element);

        public static IReadOnlyList<(string Group, string Class)> GetAllTemplateTaxonomies() =>
            StandardTypeMetadataRegistry.GetAllTemplateTaxonomies();

        public static void RegisterAllTranslators(ModelDispatcher dispatcher, IIdentityService identityService)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (identityService == null) throw new ArgumentNullException(nameof(identityService));

            var sharedTranslators = new Dictionary<Type, IModelTranslator>();

            foreach (var desc in StandardTypeMetadataRegistry.GetAllDescriptors())
            {
                if (!_revitBindings.TryGetValue(desc.BucketName, out var revitBinding)) continue;

                foreach (var binding in revitBinding.TranslatorBindings)
                {
                    var translator = binding.CreateTranslator(identityService);
                    var translatorType = translator.GetType();
                    if (sharedTranslators.TryGetValue(translatorType, out var existing))
                    {
                        translator = existing;
                    }
                    else
                    {
                        sharedTranslators[translatorType] = translator;
                    }

                    dispatcher.Register(binding.ModelType, translator, binding.RevitTypes, binding.ClassAliases);
                }
            }

            dispatcher.RegisterIgnored(IgnoredRevitTypes.ToArray());
            dispatcher.RegisterPending(PendingRevitTypes.ToArray());
        }

        public static IEnumerable<Element> GatherRootElements(
            Document doc,
            IReadOnlyCollection<string>? selectedGroupings = null,
            CancellationToken cancellationToken = default)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            cancellationToken.ThrowIfCancellationRequested();

            var rootElements = new List<Element>();
            var seenIds = new HashSet<ElementId>();

            foreach (var desc in StandardTypeMetadataRegistry.GetAllDescriptors())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_revitBindings.TryGetValue(desc.BucketName, out var revitBinding) || revitBinding.RootCollector == null)
                {
                    continue;
                }

                try
                {
                    foreach (var elem in revitBinding.RootCollector(doc, selectedGroupings, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (elem != null && seenIds.Add(elem.Id))
                        {
                            rootElements.Add(elem);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error gathering {desc.BucketName}: {ex}");
                }
            }

            return rootElements;
        }

        private static IEnumerable<Element> CollectOfClass(
            Document doc,
            Type? revitType,
            string requiredClassName,
            IReadOnlyCollection<string>? selectedGroupings,
            CancellationToken cancellationToken,
            Func<Element, bool>? predicate = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (revitType == null) return Enumerable.Empty<Element>();
            if (selectedGroupings != null && !selectedGroupings.Contains(requiredClassName))
            {
                return Enumerable.Empty<Element>();
            }

            var elements = new FilteredElementCollector(doc)
                .OfClass(revitType)
                .ToElements();

            var results = new List<Element>();
            foreach (var elem in elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (elem != null && (predicate == null || predicate(elem)))
                {
                    results.Add(elem);
                }
            }
            return results;
        }

        private static IEnumerable<Element> CollectHostObjTypes(
            Document doc,
            IReadOnlyCollection<string>? selectedGroupings,
            CancellationToken cancellationToken)
        {
            var hostTypeMap = new (Type RevitType, string ClassName)[]
            {
                (typeof(WallType), "Wall Types"),
                (typeof(FloorType), "Floor Types"),
                (typeof(RoofType), "Roof Types"),
                (typeof(CeilingType), "Ceiling Types"),
                (typeof(BuildingPadType), "Host Object Types")
            };

            var results = new List<Element>();
            foreach (var (revitType, className) in hostTypeMap)
            {
                results.AddRange(CollectOfClass(doc, revitType, className, selectedGroupings, cancellationToken));
            }
            return results;
        }

        private static IEnumerable<Element> CollectViews(
            Document doc,
            bool isTemplate,
            string requiredClassName,
            IReadOnlyCollection<string>? selectedGroupings,
            CancellationToken cancellationToken)
        {
            var viewTypes = new[]
            {
                typeof(ViewDrafting),
                typeof(ViewSection),
                typeof(ViewPlan),
                typeof(ViewSheet),
                typeof(ViewSchedule)
            };

            var results = new List<Element>();
            foreach (var vt in viewTypes)
            {
                results.AddRange(CollectOfClass(
                    doc,
                    vt,
                    requiredClassName,
                    selectedGroupings,
                    cancellationToken,
                    e => e is Autodesk.Revit.DB.View v && v.IsTemplate == isTemplate));
            }
            return results;
        }

        private static IEnumerable<Element> CollectFamilySymbols(
            Document doc,
            IReadOnlyCollection<string>? selectedGroupings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = new List<Element>();
            var familySymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>();

            foreach (var fs in familySymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fs == null) continue;

                var category = fs.Category ?? fs.Family?.FamilyCategory;
                if (category == null) continue;

                long catIdVal;
#if REVIT2023
                catIdVal = category.Id.IntegerValue;
#else
                catIdVal = category.Id.Value;
#endif

                string grouping;
                if (catIdVal == (long)BuiltInCategory.OST_TitleBlocks)
                    grouping = "Title Blocks";
                else if (catIdVal == (long)BuiltInCategory.OST_DetailComponents)
                    grouping = "Detail Items";
                else if (catIdVal == (long)BuiltInCategory.OST_ProfileFamilies)
                    grouping = "Profiles";
                else if (category.CategoryType == CategoryType.Annotation)
                    grouping = "Label Types";
                else
                    grouping = "Element Types";

                if (selectedGroupings == null || selectedGroupings.Contains(grouping))
                {
                    results.Add(fs);
                }
            }

            return results;
        }

        /// <summary>
        /// Validates whether a live Revit candidate Element satisfies the disambiguation criteria
        /// of an incoming ElementModel (Category, FamilyName, ViewFamily, DimensionStyle, and Pattern.Target).
        /// </summary>
        public static bool MatchesDisambiguators(ElementModel incomingModel, Element liveElement)
        {
            if (incomingModel == null || liveElement == null) return false;

            // 1. Category check: when incomingModel.Category is non-empty and live category name is non-empty, require OrdinalIgnoreCase match.
            string? liveCategoryName = liveElement.Category?.Name;
            if (string.IsNullOrEmpty(liveCategoryName) && liveElement is FamilySymbol liveSymForCat)
            {
                liveCategoryName = liveSymForCat.Family?.FamilyCategory?.Name;
            }

            if (!string.IsNullOrEmpty(incomingModel.Category) && !string.IsNullOrEmpty(liveCategoryName))
            {
                if (!string.Equals(liveCategoryName, incomingModel.Category, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 2. ViewFamilyType / FamilySymbol / ElementType disambiguation
            if (incomingModel is ElementTypeModel etModel)
            {
                if (liveElement is ViewFamilyType liveVft)
                {
                    if (!string.IsNullOrWhiteSpace(etModel.ViewFamily))
                    {
                        if (!string.Equals(liveVft.ViewFamily.ToString(), etModel.ViewFamily, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(etModel.FamilyName))
                    {
                        bool matchesFamilyName = string.Equals(liveVft.FamilyName, etModel.FamilyName, StringComparison.OrdinalIgnoreCase);
                        bool matchesViewFamily = string.Equals(liveVft.ViewFamily.ToString(), etModel.FamilyName, StringComparison.OrdinalIgnoreCase);
                        if (!matchesFamilyName && !matchesViewFamily)
                        {
                            return false;
                        }
                    }
                }
                else if (liveElement is FamilySymbol liveSymbol)
                {
                    if (!string.IsNullOrEmpty(etModel.FamilyName))
                    {
                        string liveFamilyName = !string.IsNullOrEmpty(liveSymbol.Family?.Name)
                            ? liveSymbol.Family!.Name
                            : (liveSymbol.FamilyName ?? string.Empty);

                        if (!string.Equals(liveFamilyName, etModel.FamilyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
                else if (liveElement is ElementType liveEt && !string.IsNullOrEmpty(etModel.FamilyName))
                {
                    if (!string.Equals(liveEt.FamilyName, etModel.FamilyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            // 3. DimensionTypeModel (DimensionStyle): when !string.IsNullOrEmpty(dimModel.DimensionStyle) and candidate is DimensionType liveDim,
            // require string.Equals(liveDim.StyleType.ToString(), dimModel.DimensionStyle, StringComparison.OrdinalIgnoreCase).
            if (incomingModel is DimensionTypeModel dimModel && !string.IsNullOrEmpty(dimModel.DimensionStyle))
            {
                if (liveElement is DimensionType liveDim)
                {
                    if (!string.Equals(liveDim.StyleType.ToString(), dimModel.DimensionStyle, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            // 4. FillPatternElementModel (Pattern?.Target): when !string.IsNullOrEmpty(fpModel.Pattern?.Target) and candidate is FillPatternElement liveFp,
            // require string.Equals(liveFp.GetFillPattern()?.Target.ToString(), fpModel.Pattern.Target, StringComparison.OrdinalIgnoreCase).
            if (incomingModel is FillPatternElementModel fpModel && fpModel.Pattern != null && !string.IsNullOrEmpty(fpModel.Pattern.Target))
            {
                if (liveElement is FillPatternElement liveFp)
                {
                    var pat = liveFp.GetFillPattern();
                    if (pat != null && !string.Equals(pat.Target.ToString(), fpModel.Pattern.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Resolves the disambiguated target Revit Element for an incoming ElementModel.
        /// First checks UniqueId / guarded Id (if identityService is provided and allowPositiveId is set),
        /// discarding candidates that conflict with disambiguators.
        /// Then queries all elements of the expected Revit class matching Name and filters via MatchesDisambiguators.
        /// </summary>
        public static Element? ResolveTargetElement(
            ElementModel incomingModel,
            Document doc,
            IIdentityService? identityService = null,
            bool allowPositiveId = false)
        {
            if (incomingModel == null || doc == null) return null;

            // 1. Check UniqueId / guarded Id via identityService if provided
            if (identityService != null)
            {
                var identityModel = incomingModel.ElementId ?? new ElementIdModel();
                long guardedId = (identityModel.Id < 0 || allowPositiveId) ? identityModel.Id : 0;

                if (!string.IsNullOrEmpty(identityModel.UniqueId) || guardedId != 0)
                {
                    var idQueryModel = new ElementIdModel
                    {
                        Id = guardedId,
                        UniqueId = identityModel.UniqueId,
                        Class = incomingModel.Class,
                        IsTemplate = incomingModel.IsTemplate
                    };

                    var idCandidate = identityService.ResolveElement(idQueryModel, doc);
                    if (idCandidate != null)
                    {
                        if (MatchesDisambiguators(incomingModel, idCandidate))
                        {
                            return idCandidate;
                        }
                        // Disambiguator conflict - discard candidate to prevent false positive match
                    }
                }
            }

            // 2. Query all elements of the expected Revit class in doc matching e.Name == incomingModel.Name
            if (!string.IsNullOrEmpty(incomingModel.Name) && !string.IsNullOrEmpty(incomingModel.Class))
            {
                Type? elemClass = Select.RevitClassByString(incomingModel.Class);
                if (elemClass == null)
                {
                    var desc = GetDescriptorByClass(incomingModel.Class);
                    if (desc != null && _revitBindings.TryGetValue(desc.BucketName, out var revitBinding) && revitBinding.RevitType != null)
                    {
                        elemClass = revitBinding.RevitType;
                    }
                }

                if (elemClass != null && typeof(Element).IsAssignableFrom(elemClass))
                {
                    var candidates = new FilteredElementCollector(doc)
                        .OfClass(elemClass)
                        .Where(e => string.Equals(e.Name, incomingModel.Name, StringComparison.Ordinal))
                        .ToList();

                    var matchedList = candidates.Where(c => MatchesDisambiguators(incomingModel, c)).ToList();
                    if (matchedList.Count > 0)
                    {
                        if (incomingModel is ElementTypeModel fsModel &&
                            string.IsNullOrEmpty(fsModel.FamilyName) &&
                            matchedList.OfType<FamilySymbol>().Any())
                        {
                            var distinctFamilies = matchedList
                                .OfType<FamilySymbol>()
                                .Select(fs => !string.IsNullOrEmpty(fs.Family?.Name) ? fs.Family!.Name : (fs.FamilyName ?? string.Empty))
                                .Where(fn => !string.IsNullOrEmpty(fn))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (distinctFamilies.Count > 1)
                            {
                                return null;
                            }
                        }

                        return matchedList[0];
                    }
                }
            }

            return null;
        }
    }
}
