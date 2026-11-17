using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Synthetic.RevitDOM.Models;

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
            Func<ElementModel, (string Group, string Class)>? dynamicTaxonomy = null)
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
                className: "Fill Patterns"));

            // 2. LinePatternElements
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "LinePatternElements",
                revitType: typeof(LinePatternElement),
                defaultClass: "Autodesk.Revit.DB.LinePatternElement",
                modelType: typeof(LinePatternElementModel),
                tier: 1,
                scope: DocumentScope.Universal,
                groupName: "Materials & Assets",
                className: "Line Patterns"));

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
                className: "Materials"));

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
                dynamicTaxonomy: ResolveFamilySymbolTaxonomy));

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
                customKeySelector: m =>
                {
                    if (m is CategoryModel cat)
                    {
                        string parent = cat.ParentCategoryName ?? string.Empty;
                        string name = cat.Name ?? string.Empty;
                        return $"[{parent}]_{name}";
                    }
                    return (m as ElementModel)?.Name ?? string.Empty;
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
                    "Autodesk.Revit.DB.StairsType"
                },
                dynamicTaxonomy: ResolveHostObjTypeTaxonomy));

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
                legacyBucketAliases: new[] { "FillRegionTypes" }));

            // 9. DimensionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "DimensionTypes",
                revitType: typeof(DimensionType),
                defaultClass: "Autodesk.Revit.DB.DimensionType",
                modelType: typeof(DimensionTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Dimension Types"));

            // 10. TextNoteTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "TextNoteTypes",
                revitType: typeof(TextNoteType),
                defaultClass: "Autodesk.Revit.DB.TextNoteType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Text Note Types"));

            // 11. ViewTemplates
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ViewTemplates",
                revitType: typeof(Autodesk.Revit.DB.View),
                defaultClass: "Autodesk.Revit.DB.View",
                modelType: typeof(ViewModel),
                tier: 6,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "View Templates"));

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
                }));

            // 13. GridTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "GridTypes",
                revitType: typeof(GridType),
                defaultClass: "Autodesk.Revit.DB.GridType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Grid Types"));

            // 14. LevelTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "LevelTypes",
                revitType: typeof(LevelType),
                defaultClass: "Autodesk.Revit.DB.LevelType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Level Types"));

            // 15. CurtainSystemTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "CurtainSystemTypes",
                revitType: typeof(CurtainSystemType),
                defaultClass: "Autodesk.Revit.DB.CurtainSystemType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Curtain System Types"));

            // 16. MullionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "MullionTypes",
                revitType: typeof(MullionType),
                defaultClass: "Autodesk.Revit.DB.MullionType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Mullion Types"));

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
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.Architecture.FasciaType",
                    "Autodesk.Revit.DB.FasciaType"
                }));

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
                classAliases: new[]
                {
                    "Autodesk.Revit.DB.Architecture.GutterType",
                    "Autodesk.Revit.DB.GutterType"
                }));

            // 19. ViewFamilyTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ViewFamilyTypes",
                revitType: typeof(ViewFamilyType),
                defaultClass: "Autodesk.Revit.DB.ViewFamilyType",
                modelType: typeof(ElementTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "View Family Types"));

            // 20. BrowserOrganizations
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "BrowserOrganizations",
                revitType: typeof(BrowserOrganization),
                defaultClass: "Autodesk.Revit.DB.BrowserOrganization",
                modelType: typeof(BrowserOrganizationModel),
                tier: 6,
                scope: DocumentScope.ProjectOnly,
                groupName: "Views",
                className: "Browser Organizations"));

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
                }));

            // 22. TextElementTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "TextElementTypes",
                revitType: typeof(TextElementType),
                defaultClass: "Autodesk.Revit.DB.TextElementType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.FamilyOnly,
                groupName: "Annotations",
                className: "Label Types"));

            // 23. ModelTextTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ModelTextTypes",
                revitType: typeof(ModelTextType),
                defaultClass: "Autodesk.Revit.DB.ModelTextType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.Universal,
                groupName: "Annotations",
                className: "Model Text Types"));

            // 24. ParameterFilters
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ParameterFilters",
                revitType: typeof(ParameterFilterElement),
                defaultClass: "Autodesk.Revit.DB.ParameterFilterElement",
                modelType: typeof(ParameterFilterElementModel),
                tier: 4,
                scope: DocumentScope.ProjectOnly,
                groupName: "Standards & Categories",
                className: "Filters"));

            // 25. SpotDimensionTypes
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "SpotDimensionTypes",
                revitType: typeof(SpotDimensionType),
                defaultClass: "Autodesk.Revit.DB.SpotDimensionType",
                modelType: typeof(ElementTypeModel),
                tier: 3,
                scope: DocumentScope.ProjectOnly,
                groupName: "Annotations",
                className: "Spot Dimension Types"));

            // 26. ToposolidTypes
#if REVIT2022 || REVIT2023
            Type? toposolidType = null;
#else
            Type? toposolidType = typeof(ToposolidType);
#endif
            _canonicalDescriptors.Add(new StandardTypeDescriptor(
                bucketName: "ToposolidTypes",
                revitType: toposolidType,
                defaultClass: "Autodesk.Revit.DB.ToposolidType",
                modelType: typeof(HostObjTypeModel),
                tier: 5,
                scope: DocumentScope.ProjectOnly,
                groupName: "System Types",
                className: "Toposolid Types"));

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

            // Normalization for architecture namespace differences
            if (className.StartsWith("Autodesk.Revit.DB.Architecture.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = className.Substring("Autodesk.Revit.DB.Architecture.".Length);
                if (_byClass.TryGetValue("Autodesk.Revit.DB." + shortName, out desc))
                {
                    return desc;
                }
            }
            else if (className.StartsWith("Autodesk.Revit.DB.", StringComparison.OrdinalIgnoreCase))
            {
                string shortName = className.Substring("Autodesk.Revit.DB.".Length);
                if (_byClass.TryGetValue("Autodesk.Revit.DB.Architecture." + shortName, out desc))
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

            // 2. Check temporary marker POCOs (expand step support)
            if (model is GridTypeModel) return GetDescriptorByBucket("GridTypes");
            if (model is LevelTypeModel) return GetDescriptorByBucket("LevelTypes");
            if (model is CurtainSystemTypeModel) return GetDescriptorByBucket("CurtainSystemTypes");
            if (model is MullionTypeModel) return GetDescriptorByBucket("MullionTypes");
            if (model is FasciaTypeModel) return GetDescriptorByBucket("FasciaTypes");
            if (model is GutterTypeModel) return GetDescriptorByBucket("GutterTypes");
            if (model is ViewFamilyTypeModel) return GetDescriptorByBucket("ViewFamilyTypes");

            // 3. Check distinct POCO types
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

        public static (string Group, string Class) GetTaxonomy(ElementModel? element)
        {
            if (element == null) return ("Other", "Unknown Class");

            // 1. HostObjTypeModel (must be checked BEFORE generic ElementTypeModel since HostObjTypeModel inherits from ElementTypeModel)
            if (element is HostObjTypeModel hostObj)
            {
                return ResolveHostObjTypeTaxonomy(hostObj);
            }

            // 2. ElementTypeModel (handles specific classes and 5-way loadable family taxonomy)
            if (element is ElementTypeModel elemType)
            {
                string cls = elemType.Class ?? string.Empty;
                if (element is FilledRegionTypeModel || string.Equals(cls, "Autodesk.Revit.DB.FilledRegionType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Filled Region Types");
                if (element is DimensionTypeModel || string.Equals(cls, "Autodesk.Revit.DB.DimensionType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Dimension Types");
                if (string.Equals(cls, "Autodesk.Revit.DB.TextNoteType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Text Note Types");
                if (string.Equals(cls, "Autodesk.Revit.DB.TextElementType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Label Types");
                if (string.Equals(cls, "Autodesk.Revit.DB.ModelTextType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Model Text Types");
                if (string.Equals(cls, "Autodesk.Revit.DB.SpotDimensionType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Spot Dimension Types");
                if (element is GridTypeModel || string.Equals(cls, "Autodesk.Revit.DB.GridType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Grid Types");
                if (element is LevelTypeModel || string.Equals(cls, "Autodesk.Revit.DB.LevelType", StringComparison.OrdinalIgnoreCase))
                    return ("Annotations", "Level Types");
                if (element is ViewFamilyTypeModel || string.Equals(cls, "Autodesk.Revit.DB.ViewFamilyType", StringComparison.OrdinalIgnoreCase))
                    return ("Views", "View Family Types");

                return ResolveFamilySymbolTaxonomy(elemType);
            }

            // 3. ViewModel
            if (element is ViewModel vm)
            {
                return ("Views", vm.IsTemplate ? "View Templates" : "Views");
            }

            // 4. Descriptor lookup for other standard models
            var desc = GetDescriptorByModel(element);
            if (desc != null)
            {
                return desc.GetTaxonomy(element);
            }

            // 5. Fallback for unrecognized models
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
            if (element is CurtainSystemTypeModel) return ("System Types", "Curtain System Types");
            if (element is MullionTypeModel) return ("System Types", "Mullion Types");
            if (element is FasciaTypeModel) return ("System Types", "Fascia Types");
            if (element is GutterTypeModel) return ("System Types", "Gutter Types");

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
