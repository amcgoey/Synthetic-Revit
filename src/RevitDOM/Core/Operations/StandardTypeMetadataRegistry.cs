using System;
using System.Collections.Generic;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Specifies the valid document contexts where a Revit standard can be enforced or diffed.
    /// </summary>
    public enum DocumentScope
    {
        Universal,
        ProjectOnly,
        FamilyOnly
    }

    /// <summary>
    /// Encapsulates the pure architectural metadata governing a Revit standard type
    /// (bucket name, topological tier, document scope, POCO type, composite keys, aliases, and Source Pane taxonomy).
    /// </summary>
    public sealed class StandardTypeDescriptor
    {
        public string BucketName { get; }
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

        public StandardTypeDescriptor(
            string bucketName,
            string defaultClass,
            Type modelType,
            int tier,
            DocumentScope scope,
            string groupName,
            string className,
            IReadOnlyList<string>? legacyBucketAliases = null,
            IReadOnlyList<string>? classAliases = null,
            Func<ObjectModel, string>? customKeySelector = null,
            Func<ElementModel, (string Group, string Class)>? dynamicTaxonomy = null)
        {
            BucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
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
    /// Pure headless registry defining all 26 canonical Revit standard buckets, their topological dependency tiers,
    /// document scopes, POCO bindings, composite keys, legacy aliases, and Source Pane taxonomy mappings.
    /// </summary>
    public static class StandardTypeMetadataRegistry
    {
        private static readonly List<StandardTypeDescriptor> _canonicalDescriptors;
        private static readonly Dictionary<string, StandardTypeDescriptor> _byBucket;
        private static readonly Dictionary<string, StandardTypeDescriptor> _byClass;

        public static string FormatCompositeKey(string? prefix, string? name)
        {
            string n = name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                return $"[{prefix}]_{n}";
            }
            return n;
        }

        public static string FamilyNameKeySelector(ObjectModel m)
        {
            return (m is ElementTypeModel et)
                ? FormatCompositeKey(et.FamilyName, et.Name)
                : FormatCompositeKey(null, (m as ElementModel)?.Name);
        }

        public static string FormatFamilySymbolKey(ObjectModel m)
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

        public static string FormatViewFamilyTypeKey(ObjectModel m)
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

        /// <summary>
        /// Yields Revit namespace alias candidates for a class name (Architecture &lt;-&gt; DB, and bare short-name qualification).
        /// </summary>
        public static IEnumerable<string> GetNamespaceAliasCandidates(string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) yield break;

            if (typeName!.StartsWith("Autodesk.Revit.DB.Architecture.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = typeName.Substring("Autodesk.Revit.DB.Architecture.".Length);
                yield return "Autodesk.Revit.DB." + shortName;
            }
            else if (typeName.StartsWith("Autodesk.Revit.DB.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = typeName.Substring("Autodesk.Revit.DB.".Length);
                yield return "Autodesk.Revit.DB.Architecture." + shortName;
            }
            else if (!typeName.Contains("."))
            {
                yield return "Autodesk.Revit.DB." + typeName;
                yield return "Autodesk.Revit.DB.Architecture." + typeName;
            }
        }

        static StandardTypeMetadataRegistry()
        {
            _canonicalDescriptors = new List<StandardTypeDescriptor>
            {
                // 1. FillPatternElements
                new StandardTypeDescriptor(
                    bucketName: "FillPatternElements",
                    defaultClass: "Autodesk.Revit.DB.FillPatternElement",
                    modelType: typeof(FillPatternElementModel),
                    tier: 1,
                    scope: DocumentScope.Universal,
                    groupName: "Materials & Assets",
                    className: "Fill Patterns",
                    customKeySelector: m => (m is FillPatternElementModel fp)
                        ? FormatCompositeKey(fp.Pattern?.Target, fp.Name)
                        : FormatCompositeKey(null, (m as ElementModel)?.Name)),

                // 2. LinePatternElements
                new StandardTypeDescriptor(
                    bucketName: "LinePatternElements",
                    defaultClass: "Autodesk.Revit.DB.LinePatternElement",
                    modelType: typeof(LinePatternElementModel),
                    tier: 1,
                    scope: DocumentScope.Universal,
                    groupName: "Materials & Assets",
                    className: "Line Patterns"),

                // 3. PropertySetElements
                new StandardTypeDescriptor(
                    bucketName: "PropertySetElements",
                    defaultClass: "Autodesk.Revit.DB.PropertySetElement",
                    modelType: typeof(PropertySetElementModel),
                    tier: 1,
                    scope: DocumentScope.Universal,
                    groupName: "Materials & Assets",
                    className: "Appearance Assets"),

                // 4. Materials
                new StandardTypeDescriptor(
                    bucketName: "Materials",
                    defaultClass: "Autodesk.Revit.DB.Material",
                    modelType: typeof(MaterialModel),
                    tier: 2,
                    scope: DocumentScope.Universal,
                    groupName: "Materials & Assets",
                    className: "Materials"),

                // 5. ElementTypes (FamilySymbol / Loadable Family Types)
                new StandardTypeDescriptor(
                    bucketName: "ElementTypes",
                    defaultClass: "Autodesk.Revit.DB.FamilySymbol",
                    modelType: typeof(ElementTypeModel),
                    tier: 5,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Standards & Categories",
                    className: "Element Types",
                    customKeySelector: FormatFamilySymbolKey,
                    dynamicTaxonomy: ResolveFamilySymbolTaxonomy),

                // 6. Categories
                new StandardTypeDescriptor(
                    bucketName: "Categories",
                    defaultClass: "Autodesk.Revit.DB.Category",
                    modelType: typeof(CategoryModel),
                    tier: 4,
                    scope: DocumentScope.Universal,
                    groupName: "Standards & Categories",
                    className: "Categories",
                    customKeySelector: m => (m is CategoryModel cat)
                        ? FormatCompositeKey(cat.ParentCategoryName, cat.Name)
                        : FormatCompositeKey(null, (m as ElementModel)?.Name)),

                // 7. HostObjTypes
                new StandardTypeDescriptor(
                    bucketName: "HostObjTypes",
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
                    dynamicTaxonomy: ResolveHostObjTypeTaxonomy),

                // 8. FilledRegionTypes (legacy read alias FillRegionTypes)
                new StandardTypeDescriptor(
                    bucketName: "FilledRegionTypes",
                    defaultClass: "Autodesk.Revit.DB.FilledRegionType",
                    modelType: typeof(FilledRegionTypeModel),
                    tier: 3,
                    scope: DocumentScope.Universal,
                    groupName: "Annotations",
                    className: "Filled Region Types",
                    legacyBucketAliases: new[] { "FillRegionTypes" }),

                // 9. DimensionTypes
                new StandardTypeDescriptor(
                    bucketName: "DimensionTypes",
                    defaultClass: "Autodesk.Revit.DB.DimensionType",
                    modelType: typeof(DimensionTypeModel),
                    tier: 3,
                    scope: DocumentScope.Universal,
                    groupName: "Annotations",
                    className: "Dimension Types",
                    customKeySelector: m => (m is DimensionTypeModel dim)
                        ? FormatCompositeKey(dim.DimensionStyle, dim.Name)
                        : FormatCompositeKey(null, (m as ElementModel)?.Name)),

                // 10. TextNoteTypes
                new StandardTypeDescriptor(
                    bucketName: "TextNoteTypes",
                    defaultClass: "Autodesk.Revit.DB.TextNoteType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.Universal,
                    groupName: "Annotations",
                    className: "Text Note Types"),

                // 11. ViewTemplates
                new StandardTypeDescriptor(
                    bucketName: "ViewTemplates",
                    defaultClass: "Autodesk.Revit.DB.View",
                    modelType: typeof(ViewModel),
                    tier: 6,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Views",
                    className: "View Templates"),

                // 12. Views
                new StandardTypeDescriptor(
                    bucketName: "Views",
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
                    }),

                // 13. GridTypes
                new StandardTypeDescriptor(
                    bucketName: "GridTypes",
                    defaultClass: "Autodesk.Revit.DB.GridType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Annotations",
                    className: "Grid Types"),

                // 14. LevelTypes
                new StandardTypeDescriptor(
                    bucketName: "LevelTypes",
                    defaultClass: "Autodesk.Revit.DB.LevelType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Annotations",
                    className: "Level Types"),

                // 15. CurtainSystemTypes
                new StandardTypeDescriptor(
                    bucketName: "CurtainSystemTypes",
                    defaultClass: "Autodesk.Revit.DB.CurtainSystemType",
                    modelType: typeof(HostObjTypeModel),
                    tier: 5,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "System Types",
                    className: "Curtain System Types",
                    customKeySelector: FamilyNameKeySelector),

                // 16. MullionTypes
                new StandardTypeDescriptor(
                    bucketName: "MullionTypes",
                    defaultClass: "Autodesk.Revit.DB.MullionType",
                    modelType: typeof(HostObjTypeModel),
                    tier: 5,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "System Types",
                    className: "Mullion Types",
                    customKeySelector: FamilyNameKeySelector),

                // 17. FasciaTypes
                new StandardTypeDescriptor(
                    bucketName: "FasciaTypes",
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
                    }),

                // 18. GutterTypes
                new StandardTypeDescriptor(
                    bucketName: "GutterTypes",
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
                    }),

                // 19. ViewFamilyTypes
                new StandardTypeDescriptor(
                    bucketName: "ViewFamilyTypes",
                    defaultClass: "Autodesk.Revit.DB.ViewFamilyType",
                    modelType: typeof(ElementTypeModel),
                    tier: 5,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Views",
                    className: "View Family Types",
                    customKeySelector: FormatViewFamilyTypeKey),

                // 20. BrowserOrganizations
                new StandardTypeDescriptor(
                    bucketName: "BrowserOrganizations",
                    defaultClass: "Autodesk.Revit.DB.BrowserOrganization",
                    modelType: typeof(BrowserOrganizationModel),
                    tier: 6,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Views",
                    className: "Browser Organizations"),

                // 21. ParameterElements
                new StandardTypeDescriptor(
                    bucketName: "ParameterElements",
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
                    }),

                // 22. TextElementTypes
                new StandardTypeDescriptor(
                    bucketName: "TextElementTypes",
                    defaultClass: "Autodesk.Revit.DB.TextElementType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.FamilyOnly,
                    groupName: "Annotations",
                    className: "Label Types"),

                // 23. ModelTextTypes
                new StandardTypeDescriptor(
                    bucketName: "ModelTextTypes",
                    defaultClass: "Autodesk.Revit.DB.ModelTextType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.Universal,
                    groupName: "Annotations",
                    className: "Model Text Types"),

                // 24. ParameterFilters
                new StandardTypeDescriptor(
                    bucketName: "ParameterFilters",
                    defaultClass: "Autodesk.Revit.DB.ParameterFilterElement",
                    modelType: typeof(ParameterFilterElementModel),
                    tier: 4,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Standards & Categories",
                    className: "Filters"),

                // 25. SpotDimensionTypes
                new StandardTypeDescriptor(
                    bucketName: "SpotDimensionTypes",
                    defaultClass: "Autodesk.Revit.DB.SpotDimensionType",
                    modelType: typeof(ElementTypeModel),
                    tier: 3,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "Annotations",
                    className: "Spot Dimension Types",
                    customKeySelector: FamilyNameKeySelector),

                // 26. ToposolidTypes
                new StandardTypeDescriptor(
                    bucketName: "ToposolidTypes",
                    defaultClass: "Autodesk.Revit.DB.ToposolidType",
                    modelType: typeof(HostObjTypeModel),
                    tier: 5,
                    scope: DocumentScope.ProjectOnly,
                    groupName: "System Types",
                    className: "Toposolid Types",
                    customKeySelector: FamilyNameKeySelector)
            };

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

            if (_byClass.TryGetValue(className!, out var desc))
            {
                return desc;
            }

            foreach (var alias in GetNamespaceAliasCandidates(className))
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
        public static DocumentScope GetDocumentScope(ObjectModel? model)
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
        public static bool IsInDocumentScope(ObjectModel? model, bool isFamilyDocument)
        {
            var scope = GetDocumentScope(model);
            if (isFamilyDocument)
            {
                return scope == DocumentScope.Universal || scope == DocumentScope.FamilyOnly;
            }

            return scope == DocumentScope.Universal || scope == DocumentScope.ProjectOnly;
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
