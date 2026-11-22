using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Specifies the valid document contexts where a Revit standard can be enforced or diffed.
    /// </summary>
    internal enum DocumentScope
    {
        Universal,
        ProjectOnly,
        FamilyOnly
    }

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

    /// <summary>
    /// Encapsulates the complete architectural metadata governing a Revit standard type.
    /// Internal strictly to RevitDOM operations; never exposed on public interfaces.
    /// </summary>
    internal class StandardTypeDescriptor
    {
        public string BucketName { get; }
        public Type? RevitType { get; }
        public string DefaultClass { get; }
        public Type ModelType { get; }
        public int Tier { get; }
        public DocumentScope Scope { get; }
        public string GroupName { get; }
        public string ClassName { get; }
        public IReadOnlyList<string> LegacyBucketAliases { get; }
        public IReadOnlyList<string> ClassAliases { get; }
        public Func<ObjectModel, string>? CustomKeySelector { get; }
        public Func<ElementModel, (string Group, string Class)>? DynamicTaxonomy { get; }
        public IReadOnlyList<TranslatorBinding> TranslatorBindings { get; }
        public Func<Document, IReadOnlyCollection<string>?, CancellationToken, IEnumerable<Element>>? RootCollector { get; }

        public StandardTypeDescriptor(
            string bucketName,
            Type? revitType,
            string defaultClass,
            Type modelType,
            int tier,
            DocumentScope scope,
            string groupName,
            string className,
            IReadOnlyList<string>? legacyBucketAliases = null,
            IReadOnlyList<string>? classAliases = null,
            Func<ObjectModel, string>? customKeySelector = null,
            Func<ElementModel, (string Group, string Class)>? dynamicTaxonomy = null,
            IReadOnlyList<TranslatorBinding>? translatorBindings = null,
            Func<Document, IReadOnlyCollection<string>?, CancellationToken, IEnumerable<Element>>? rootCollector = null)
        {
            BucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
            RevitType = revitType;
            DefaultClass = defaultClass ?? throw new ArgumentNullException(nameof(defaultClass));
            ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
            Tier = tier;
            Scope = scope;
            GroupName = groupName ?? throw new ArgumentNullException(nameof(groupName));
            ClassName = className ?? throw new ArgumentNullException(nameof(className));
            LegacyBucketAliases = legacyBucketAliases ?? Array.Empty<string>();
            ClassAliases = classAliases ?? Array.Empty<string>();
            CustomKeySelector = customKeySelector;
            DynamicTaxonomy = dynamicTaxonomy;
            TranslatorBindings = translatorBindings ?? Array.Empty<TranslatorBinding>();
            RootCollector = rootCollector;
        }

        public string GetKey(ObjectModel model)
        {
            if (CustomKeySelector != null)
            {
                return CustomKeySelector(model);
            }

            if (model is ElementModel em && !string.IsNullOrEmpty(em.Name))
            {
                return em.Name;
            }

            return string.Empty;
        }

        public (string Group, string Class) GetTaxonomy(ElementModel element)
        {
            if (DynamicTaxonomy != null)
            {
                return DynamicTaxonomy(element);
            }

            return (GroupName, ClassName);
        }
    }

    /// <summary>
    /// Central internal registry defining all 26 canonical Revit standard buckets, their topological dependency tiers,
    /// document scopes, POCO bindings, and Source Pane taxonomy mappings.
    /// </summary>
    internal static class StandardTypeRegistry
    {
        private static readonly List<StandardTypeDescriptor> _canonicalDescriptors;
        private static readonly Dictionary<string, StandardTypeDescriptor> _byBucket;
        private static readonly Dictionary<string, StandardTypeDescriptor> _byClass;

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

        public static string FormatCompositeKey(string? prefix, string? name)
        {
            string n = name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                return $"[{prefix}]_{n}";
            }
            return n;
        }

        private static string FamilyNameKeySelector(ObjectModel m)
        {
            return (m is ElementTypeModel et)
                ? FormatCompositeKey(et.FamilyName, et.Name)
                : FormatCompositeKey(null, (m as ElementModel)?.Name);
        }

        private static string FormatFamilySymbolKey(ObjectModel m)
        {
            if (m is not ElementModel em) return string.Empty;

            string cat = em.Category ?? string.Empty;
            string fam = (m is ElementTypeModel et) ? (et.FamilyName ?? string.Empty) : string.Empty;
            bool hasCat = !string.IsNullOrWhiteSpace(cat);
            bool hasFam = !string.IsNullOrWhiteSpace(fam);

            if (hasCat && hasFam) return FormatCompositeKey($"{cat} | {fam}", em.Name);
            if (hasCat) return FormatCompositeKey(cat, em.Name);
            if (hasFam) return FormatCompositeKey(fam, em.Name);
            return FormatCompositeKey(null, em.Name);
        }

        private static string FormatViewFamilyTypeKey(ObjectModel m)
        {
            if (m is ElementTypeModel et)
            {
                string? prefix = !string.IsNullOrWhiteSpace(et.ViewFamily)
                    ? et.ViewFamily
                    : (!string.IsNullOrWhiteSpace(et.FamilyName) ? et.FamilyName : null);
                return FormatCompositeKey(prefix, et.Name);
            }
            return FormatCompositeKey(null, (m as ElementModel)?.Name);
        }

        static StandardTypeRegistry()
        {
            _canonicalDescriptors = new List<StandardTypeDescriptor>();

            // 1. FillPatternElements
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "FillPatternElements",
                revitType: typeof(FillPatternElement),
                defaultClass: "Autodesk.Revit.DB.FillPatternElement",
                modelType: typeof(FillPatternElementModel),
                tier: 1,
                scope: DocumentScope.Universal,
                groupName: "Materials & Assets",
                className: "Fill Patterns",
                customKeySelector: m => (m is FillPatternElementModel fp) ? FormatCompositeKey(fp.Pattern?.Target, fp.Name) : FormatCompositeKey(null, (m as ElementModel)?.Name),
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(FillPatternElementModel), _ => new FillPatternTranslator(), new[] { typeof(FillPatternElement) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(FillPatternElement), "Fill Patterns", groupings, ct)));

            // 2. LinePatternElements
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "LinePatternElements",
                revitType: typeof(LinePatternElement),
                defaultClass: "Autodesk.Revit.DB.LinePatternElement",
                modelType: typeof(LinePatternElementModel),
                tier: 1,
                scope: DocumentScope.Universal,
                groupName: "Materials & Assets",
                className: "Line Patterns",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(LinePatternElementModel), _ => new LinePatternTranslator(), new[] { typeof(LinePatternElement) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(LinePatternElement), "Line Patterns", groupings, ct)));

            // 3. PropertySetElements
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "PropertySetElements",
                revitType: typeof(PropertySetElement),
                defaultClass: "Autodesk.Revit.DB.PropertySetElement",
                modelType: typeof(PropertySetElementModel),
                tier: 1,
                scope: DocumentScope.Universal,
                groupName: "Materials & Assets",
                className: "Appearance Assets"));

            // 4. Materials
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "Materials",
                revitType: typeof(Material),
                defaultClass: "Autodesk.Revit.DB.Material",
                modelType: typeof(MaterialModel),
                tier: 2,
                scope: DocumentScope.Universal,
                groupName: "Materials & Assets",
                className: "Materials",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(MaterialModel), id => new MaterialTranslator(id), new[] { typeof(Material) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(Material), "Materials", groupings, ct)));

            // 5. ElementTypes (FamilySymbol / Loadable Family Types)
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ElementTypes",
                revitType: typeof(FamilySymbol),
                defaultClass: "Autodesk.Revit.DB.FamilySymbol",
                modelType: typeof(ElementTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "Standards & Categories",
                className: "Element Types",
                customKeySelector: FormatFamilySymbolKey,
                dynamicTaxonomy: ResolveFamilySymbolTaxonomy,
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new FamilySymbolTranslator(), new[] { typeof(FamilySymbol) })
                },
                rootCollector: CollectFamilySymbols));

            // 6. Categories
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "Categories",
                revitType: typeof(Autodesk.Revit.DB.Category),
                defaultClass: "Autodesk.Revit.DB.Category",
                modelType: typeof(CategoryModel),
                tier: 4,
                scope: DocumentScope.Universal,
                groupName: "Standards & Categories",
                className: "Categories",
                customKeySelector: m => (m is CategoryModel cat) ? FormatCompositeKey(cat.ParentCategoryName, cat.Name) : FormatCompositeKey(null, (m as ElementModel)?.Name),
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(CategoryModel), id => new CategoryTranslator(id), new[] { typeof(Autodesk.Revit.DB.Category) })
                }));

            // 7. HostObjTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "HostObjTypes",
                revitType: typeof(WallType),
                defaultClass: "Autodesk.Revit.DB.WallType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Host Object Types",
                customKeySelector: FamilyNameKeySelector,
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.WallType",
                    "Autodesk.Revit.DB.FloorType",
                    "Autodesk.Revit.DB.RoofType",
                    "Autodesk.Revit.DB.CeilingType",
                    "Autodesk.Revit.DB.BuildingPadType",
                    "Autodesk.Revit.DB.Architecture.RailingType",
                    "Autodesk.Revit.DB.RailingType",
                    "Autodesk.Revit.DB.Architecture.StairsType",
                    "Autodesk.Revit.DB.StairsType",
                    "Autodesk.Revit.DB.HostObjAttributes"
                },
                dynamicTaxonomy: ResolveHostObjTypeTaxonomy,
                translatorBindings: new[]
                {
                    new TranslatorBinding(
                        typeof(HostObjTypeModel),
                        id => new HostObjTypeTranslator(id),
                        new[] { typeof(WallType), typeof(FloorType), typeof(RoofType), typeof(CeilingType), typeof(BuildingPadType) },
                        new[] { "Autodesk.Revit.DB.HostObjAttributes" })
                },
                rootCollector: CollectHostObjTypes));

            // 8. FilledRegionTypes (legacy read alias FillRegionTypes)
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "FilledRegionTypes",
                revitType: typeof(FilledRegionType),
                defaultClass: "Autodesk.Revit.DB.FilledRegionType",
                modelType: typeof(FilledRegionTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Filled Region Types",
                legacyBucketAliases: new[] { "FillRegionTypes" },
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(FilledRegionTypeModel), id => new FilledRegionTypeTranslator(id), new[] { typeof(FilledRegionType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(FilledRegionType), "Filled Region Types", groupings, ct)));

            // 9. DimensionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "DimensionTypes",
                revitType: typeof(DimensionType),
                defaultClass: "Autodesk.Revit.DB.DimensionType",
                modelType: typeof(DimensionTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Dimension Types",
                customKeySelector: m => (m is DimensionTypeModel dim) ? FormatCompositeKey(dim.DimensionStyle, dim.Name) : FormatCompositeKey(null, (m as ElementModel)?.Name),
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(DimensionTypeModel), _ => new DimensionTypeTranslator(), new[] { typeof(DimensionType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(DimensionType), "Dimension Types", groupings, ct)));

            // 10. TextNoteTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "TextNoteTypes",
                revitType: typeof(TextNoteType),
                defaultClass: "Autodesk.Revit.DB.TextNoteType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Text Note Types",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(TextNoteType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(TextNoteType), "Text Note Types", groupings, ct)));

            // 11. ViewTemplates
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ViewTemplates",
                revitType: typeof(Autodesk.Revit.DB.View),
                defaultClass: "Autodesk.Revit.DB.View",
                modelType: typeof(ViewModel),
                tier: 6,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "View Templates",
                rootCollector: (doc, groupings, ct) => CollectViews(doc, isTemplate: true, "View Templates", groupings, ct)));

            // 12. Views
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "Views",
                revitType: typeof(Autodesk.Revit.DB.View),
                defaultClass: "Autodesk.Revit.DB.View",
                modelType: typeof(ViewModel),
                tier: 6,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "Views",
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.ViewDrafting",
                    "Autodesk.Revit.DB.ViewSection",
                    "Autodesk.Revit.DB.ViewPlan",
                    "Autodesk.Revit.DB.ViewSheet",
                    "Autodesk.Revit.DB.ViewSchedule",
                    "Autodesk.Revit.DB.View3D"
                },
                translatorBindings: new[]
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
                rootCollector: (doc, groupings, ct) => CollectViews(doc, isTemplate: false, "Views", groupings, ct)));

            // 13. GridTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "GridTypes",
                revitType: typeof(GridType),
                defaultClass: "Autodesk.Revit.DB.GridType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Grid Types",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new GridTypeTranslator(), new[] { typeof(GridType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(GridType), "Grid Types", groupings, ct)));

            // 14. LevelTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "LevelTypes",
                revitType: typeof(LevelType),
                defaultClass: "Autodesk.Revit.DB.LevelType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Level Types",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new LevelTypeTranslator(), new[] { typeof(LevelType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(LevelType), "Level Types", groupings, ct)));

            // 15. CurtainSystemTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "CurtainSystemTypes",
                revitType: typeof(CurtainSystemType),
                defaultClass: "Autodesk.Revit.DB.CurtainSystemType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Curtain System Types",
                customKeySelector: FamilyNameKeySelector,
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(HostObjTypeModel), id => new HostObjTypeTranslator(id), new[] { typeof(CurtainSystemType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(CurtainSystemType), "Curtain System Types", groupings, ct)));

            // 16. MullionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "MullionTypes",
                revitType: typeof(MullionType),
                defaultClass: "Autodesk.Revit.DB.MullionType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Mullion Types",
                customKeySelector: FamilyNameKeySelector,
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(HostObjTypeModel), id => new HostObjTypeTranslator(id), new[] { typeof(MullionType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(MullionType), "Mullion Types", groupings, ct)));

            // 17. FasciaTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "FasciaTypes",
                revitType: typeof(FasciaType),
                defaultClass: "Autodesk.Revit.DB.Architecture.FasciaType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Fascia Types",
                customKeySelector: FamilyNameKeySelector,
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.Architecture.FasciaType",
                    "Autodesk.Revit.DB.FasciaType"
                },
                translatorBindings: new[]
                {
                    new TranslatorBinding(
                        typeof(HostObjTypeModel),
                        id => new HostObjTypeTranslator(id),
                        new[] { typeof(FasciaType) },
                        new[] { "Autodesk.Revit.DB.Architecture.FasciaType", "Autodesk.Revit.DB.FasciaType" })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(FasciaType), "Fascia Types", groupings, ct)));

            // 18. GutterTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "GutterTypes",
                revitType: typeof(GutterType),
                defaultClass: "Autodesk.Revit.DB.Architecture.GutterType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Gutter Types",
                customKeySelector: FamilyNameKeySelector,
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.Architecture.GutterType",
                    "Autodesk.Revit.DB.GutterType"
                },
                translatorBindings: new[]
                {
                    new TranslatorBinding(
                        typeof(HostObjTypeModel),
                        id => new HostObjTypeTranslator(id),
                        new[] { typeof(GutterType) },
                        new[] { "Autodesk.Revit.DB.Architecture.GutterType", "Autodesk.Revit.DB.GutterType" })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(GutterType), "Gutter Types", groupings, ct)));

            // 19. ViewFamilyTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ViewFamilyTypes",
                revitType: typeof(ViewFamilyType),
                defaultClass: "Autodesk.Revit.DB.ViewFamilyType",
                modelType: typeof(ElementTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "View Family Types",
                customKeySelector: FormatViewFamilyTypeKey,
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new ViewFamilyTypeTranslator(), new[] { typeof(ViewFamilyType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(ViewFamilyType), "View Family Types", groupings, ct)));

            // 20. BrowserOrganizations
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "BrowserOrganizations",
                revitType: typeof(BrowserOrganization),
                defaultClass: "Autodesk.Revit.DB.BrowserOrganization",
                modelType: typeof(BrowserOrganizationModel),
                tier: 6,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "Browser Organizations",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(BrowserOrganizationModel), _ => new BrowserOrganizationTranslator(), new[] { typeof(BrowserOrganization) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(BrowserOrganization), "Browser Organizations", groupings, ct)));

            // 21. ParameterElements
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ParameterElements",
                revitType: typeof(ParameterElement),
                defaultClass: "Autodesk.Revit.DB.ParameterElement",
                modelType: typeof(ParameterElementModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Standards & Categories",
                className: "Shared Parameters",
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.ParameterElement",
                    "Autodesk.Revit.DB.SharedParameterElement"
                },
                translatorBindings: new[]
                {
                    new TranslatorBinding(
                        typeof(ParameterElementModel),
                        _ => new ParameterElementTranslator(),
                        new[] { typeof(ParameterElement), typeof(SharedParameterElement) },
                        new[] { "Autodesk.Revit.DB.ParameterElement", "Autodesk.Revit.DB.SharedParameterElement" })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(ParameterElement), "Shared Parameters", groupings, ct)));

            // 22. TextElementTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "TextElementTypes",
                revitType: typeof(TextElementType),
                defaultClass: "Autodesk.Revit.DB.TextElementType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.FamilyOnly,
                groupName: "Annotations",
                className: "Label Types",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(TextElementType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(TextElementType), "Label Types", groupings, ct)));

            // 23. ModelTextTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ModelTextTypes",
                revitType: typeof(ModelTextType),
                defaultClass: "Autodesk.Revit.DB.ModelTextType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Model Text Types",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new TextElementTypeTranslator(), new[] { typeof(ModelTextType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(ModelTextType), "Model Text Types", groupings, ct)));

            // 24. ParameterFilters
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ParameterFilters",
                revitType: typeof(ParameterFilterElement),
                defaultClass: "Autodesk.Revit.DB.ParameterFilterElement",
                modelType: typeof(ParameterFilterElementModel),
                tier: 4,
                scope: DocumentScope.ProjectOnly,
                groupName: "Standards & Categories",
                className: "Filters",
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ParameterFilterElementModel), id => new ParameterFilterElementTranslator(id), new[] { typeof(ParameterFilterElement) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(ParameterFilterElement), "Filters", groupings, ct)));

            // 25. SpotDimensionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "SpotDimensionTypes",
                revitType: typeof(SpotDimensionType),
                defaultClass: "Autodesk.Revit.DB.SpotDimensionType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Spot Dimension Types",
                customKeySelector: FamilyNameKeySelector,
                translatorBindings: new[]
                {
                    new TranslatorBinding(typeof(ElementTypeModel), _ => new SpotDimensionTypeTranslator(), new[] { typeof(SpotDimensionType) })
                },
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, typeof(SpotDimensionType), "Spot Dimension Types", groupings, ct)));

            // 26. ToposolidTypes
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
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ToposolidTypes",
                revitType: toposolidType,
                defaultClass: "Autodesk.Revit.DB.ToposolidType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Toposolid Types",
                customKeySelector: FamilyNameKeySelector,
                translatorBindings: toposolidBindings,
                rootCollector: (doc, groupings, ct) => CollectOfClass(doc, toposolidType, "Toposolid Types", groupings, ct)));

            // Index descriptors for high-speed resolution
            _byBucket = new Dictionary<string, StandardTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
            _byClass = new Dictionary<string, StandardTypeDescriptor>(StringComparer.OrdinalIgnoreCase);

            foreach (var desc in _canonicalDescriptors)
            {
                _byBucket[desc.BucketName] = desc;
                foreach (var alias in desc.LegacyBucketAliases)
                {
                    _byBucket[alias] = desc;
                }

                _byClass[desc.DefaultClass] = desc;
                foreach (var alias in desc.ClassAliases)
                {
                    _byClass[alias] = desc;
                }
            }
        }

        public static void RegisterAllTranslators(ModelDispatcher dispatcher, IIdentityService identityService)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (identityService == null) throw new ArgumentNullException(nameof(identityService));

            var sharedTranslators = new Dictionary<Type, IModelTranslator>();

            foreach (var desc in _canonicalDescriptors)
            {
                foreach (var binding in desc.TranslatorBindings)
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

            foreach (var desc in _canonicalDescriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (desc.RootCollector == null) continue;

                try
                {
                    foreach (var elem in desc.RootCollector(doc, selectedGroupings, cancellationToken))
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

        public static IReadOnlyList<StandardTypeDescriptor> GetAllDescriptors() => _canonicalDescriptors;

        public static StandardTypeDescriptor? GetDescriptorByBucket(string bucketName)
        {
            if (string.IsNullOrEmpty(bucketName)) return null;
            _byBucket.TryGetValue(bucketName, out var desc);
            return desc;
        }

        public static StandardTypeDescriptor? GetDescriptorByClass(string? className)
        {
            if (string.IsNullOrEmpty(className)) return null;

            if (_byClass.TryGetValue(className, out var desc))
            {
                return desc;
            }

            foreach (var alias in Select.GetNamespaceAliasCandidates(className))
            {
                if (_byClass.TryGetValue(alias, out desc))
                {
                    return desc;
                }
            }

            return null;
        }

        public static StandardTypeDescriptor? GetDescriptorByModel(ObjectModel? model)
        {
            if (model == null) return null;

            // 1. If ElementModel with known Class, try exact class resolution first
            if (model is ElementModel em && !string.IsNullOrEmpty(em.Class))
            {
                // Disambiguate ViewTemplates vs Views
                if (model is ViewModel vm)
                {
                    return vm.IsTemplate ? GetDescriptorByBucket("ViewTemplates") : GetDescriptorByBucket("Views");
                }

                var byClass = GetDescriptorByClass(em.Class);
                if (byClass != null)
                {
                    return byClass;
                }
            }

            // 2. Check distinct POCO types
            if (model is FillPatternElementModel) return GetDescriptorByBucket("FillPatternElements");
            if (model is LinePatternElementModel) return GetDescriptorByBucket("LinePatternElements");
            if (model is PropertySetElementModel) return GetDescriptorByBucket("PropertySetElements");
            if (model is MaterialModel) return GetDescriptorByBucket("Materials");
            if (model is CategoryModel) return GetDescriptorByBucket("Categories");
            if (model is FilledRegionTypeModel) return GetDescriptorByBucket("FilledRegionTypes");
            if (model is DimensionTypeModel) return GetDescriptorByBucket("DimensionTypes");
            if (model is ParameterElementModel) return GetDescriptorByBucket("ParameterElements");
            if (model is ParameterFilterElementModel) return GetDescriptorByBucket("ParameterFilters");
            if (model is BrowserOrganizationModel) return GetDescriptorByBucket("BrowserOrganizations");

            if (model is ViewModel viewModel)
            {
                return viewModel.IsTemplate ? GetDescriptorByBucket("ViewTemplates") : GetDescriptorByBucket("Views");
            }

            if (model is HostObjTypeModel)
            {
                return GetDescriptorByBucket("HostObjTypes");
            }

            if (model is ElementTypeModel)
            {
                return GetDescriptorByBucket("ElementTypes");
            }

            return null;
        }

        public static int GetTier(ObjectModel? model)
        {
            if (model == null) return 8;

            var desc = GetDescriptorByModel(model);
            if (desc != null)
            {
                return desc.Tier;
            }

            if (model is ElementTypeModel) return 5;
            if (model is ElementModel) return 7;
            return 8;
        }

        /// <summary>
        /// Resolves the DocumentScope for a given ObjectModel.
        /// Unregistered or unknown fallback classes are treated as DocumentScope.ProjectOnly.
        /// </summary>
        internal static DocumentScope GetDocumentScope(ObjectModel? model)
        {
            if (model == null) return DocumentScope.ProjectOnly;

            var desc = GetDescriptorByModel(model);
            if (desc != null)
            {
                return desc.Scope;
            }

            return DocumentScope.ProjectOnly;
        }

        /// <summary>
        /// Determines whether an ObjectModel is within scope for the given document context (family or project).
        /// </summary>
        internal static bool IsInDocumentScope(ObjectModel? model, bool isFamilyDocument)
        {
            var scope = GetDocumentScope(model);
            if (isFamilyDocument)
            {
                return scope == DocumentScope.Universal || scope == DocumentScope.FamilyOnly;
            }

            return scope == DocumentScope.Universal || scope == DocumentScope.ProjectOnly;
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
                    if (desc != null && desc.RevitType != null)
                    {
                        elemClass = desc.RevitType;
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

        public static (string Group, string Class) GetTaxonomy(ElementModel? element)
        {
            if (element == null) return ("Other", "Unknown Class");

            var desc = GetDescriptorByModel(element);
            if (desc != null)
            {
                return desc.GetTaxonomy(element);
            }

            return ("Other", element.Class ?? "Unknown Class");
        }

        public static IReadOnlyList<(string Group, string Class)> GetAllTemplateTaxonomies()
        {
            return new List<(string Group, string Class)>
            {
                // Materials & Assets
                ("Materials & Assets", "Appearance Assets"),
                ("Materials & Assets", "Fill Patterns"),
                ("Materials & Assets", "Line Patterns"),
                ("Materials & Assets", "Materials"),

                // Annotations
                ("Annotations", "Detail Items"),
                ("Annotations", "Dimension Types"),
                ("Annotations", "Filled Region Types"),
                ("Annotations", "Grid Types"),
                ("Annotations", "Label Types"),
                ("Annotations", "Level Types"),
                ("Annotations", "Model Text Types"),
                ("Annotations", "Spot Dimension Types"),
                ("Annotations", "Text Note Types"),
                ("Annotations", "Title Blocks"),

                // System Types
                ("System Types", "Ceiling Types"),
                ("System Types", "Curtain System Types"),
                ("System Types", "Fascia Types"),
                ("System Types", "Floor Types"),
                ("System Types", "Gutter Types"),
                ("System Types", "Host Object Types"),
                ("System Types", "Mullion Types"),
                ("System Types", "Profiles"),
                ("System Types", "Railing Types"),
                ("System Types", "Roof Types"),
                ("System Types", "Stairs Types"),
                ("System Types", "Toposolid Types"),
                ("System Types", "Wall Types"),

                // Views
                ("Views", "Browser Organizations"),
                ("Views", "View Family Types"),
                ("Views", "View Templates"),
                ("Views", "Views"),

                // Standards & Categories
                ("Standards & Categories", "Categories"),
                ("Standards & Categories", "Element Types"),
                ("Standards & Categories", "Filters"),
                ("Standards & Categories", "Shared Parameters"),

                // Other
                ("Other", "Unknown Class")
            };
        }

        private static (string Group, string Class) ResolveFamilySymbolTaxonomy(ElementModel element)
        {
            string category = element.Category ?? string.Empty;

            if (string.Equals(category, "Title Blocks", StringComparison.OrdinalIgnoreCase))
                return ("Annotations", "Title Blocks");

            if (string.Equals(category, "Detail Items", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(category, "Detail Components", StringComparison.OrdinalIgnoreCase))
                return ("Annotations", "Detail Items");

            if (string.Equals(category, "Profiles", StringComparison.OrdinalIgnoreCase))
                return ("System Types", "Profiles");

            if (category.EndsWith("Tags", StringComparison.OrdinalIgnoreCase) ||
                category.EndsWith("Tag", StringComparison.OrdinalIgnoreCase) ||
                category.IndexOf("Annotation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ("Annotations", "Label Types");
            }

            return ("Standards & Categories", "Element Types");
        }

        private static (string Group, string Class) ResolveHostObjTypeTaxonomy(ElementModel element)
        {
            string cls = element.Class ?? string.Empty;
            if (cls.Contains("CurtainSystemType")) return ("System Types", "Curtain System Types");
            if (cls.Contains("MullionType")) return ("System Types", "Mullion Types");
            if (cls.Contains("FasciaType")) return ("System Types", "Fascia Types");
            if (cls.Contains("GutterType")) return ("System Types", "Gutter Types");
            if (cls.Contains("WallType")) return ("System Types", "Wall Types");
            if (cls.Contains("FloorType")) return ("System Types", "Floor Types");
            if (cls.Contains("RoofType")) return ("System Types", "Roof Types");
            if (cls.Contains("CeilingType")) return ("System Types", "Ceiling Types");
            if (cls.Contains("RailingType")) return ("System Types", "Railing Types");
            if (cls.Contains("StairsType")) return ("System Types", "Stairs Types");
            if (cls.Contains("ToposolidType")) return ("System Types", "Toposolid Types");

            return ("System Types", "Host Object Types");
        }
    }
}
