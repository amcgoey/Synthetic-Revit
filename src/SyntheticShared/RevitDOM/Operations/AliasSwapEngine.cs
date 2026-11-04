using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Decoupled utility class for deep-scanning a Revit Document and replacing references 
    /// of an aliased (merged) element ID with a target standard element ID.
    /// </summary>
    public static class AliasSwapEngine
    {
        private static readonly (Func<OverrideGraphicSettings, ElementId> GetPattern, Action<OverrideGraphicSettings, ElementId> SetPattern)[] PatternCheckers =
            new (Func<OverrideGraphicSettings, ElementId> GetPattern, Action<OverrideGraphicSettings, ElementId> SetPattern)[]
            {
                (s => s.ProjectionLinePatternId, (s, id) => s.SetProjectionLinePatternId(id)),
                (s => s.CutLinePatternId, (s, id) => s.SetCutLinePatternId(id)),
                (s => s.SurfaceForegroundPatternId, (s, id) => s.SetSurfaceForegroundPatternId(id)),
                (s => s.SurfaceBackgroundPatternId, (s, id) => s.SetSurfaceBackgroundPatternId(id)),
                (s => s.CutForegroundPatternId, (s, id) => s.SetCutForegroundPatternId(id)),
                (s => s.CutBackgroundPatternId, (s, id) => s.SetCutBackgroundPatternId(id))
            };

        /// <summary>
        /// Swaps references from an old element ID to a new element ID across parameters, category styles, compound structures, and view overrides.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="oldId">The old element ID (alias to replace).</param>
        /// <param name="newId">The new element ID (standard to use).</param>
        /// <returns>A <see cref="RedirectionResultModel"/> capturing count metrics and telemetry logs.</returns>
        public static RedirectionResultModel SwapElementReferences(Document doc, ElementId oldId, ElementId newId)
        {
            return SwapElementReferences(doc, oldId, newId, null);
        }

        /// <summary>
        /// Swaps references from an old element ID to a new element ID across parameters, category styles, compound structures, and view overrides.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="oldId">The old element ID (alias to replace).</param>
        /// <param name="newId">The new element ID (standard to use).</param>
        /// <param name="trans">Optional caller-managed active transaction handle. If null, discrete internal transactions will be used.</param>
        /// <returns>A <see cref="RedirectionResultModel"/> capturing count metrics and telemetry logs.</returns>
        public static RedirectionResultModel SwapElementReferences(Document doc, ElementId oldId, ElementId newId, Transaction trans = null)
        {
            if (oldId == null) throw new ArgumentNullException(nameof(oldId));
            if (newId == null) throw new ArgumentNullException(nameof(newId));

            var map = new Dictionary<ElementId, ElementId> { { oldId, newId } };
            return SwapElementReferences(doc, map, trans);
        }

        /// <summary>
        /// Swaps references for a batch of element IDs across parameters, category styles, compound structures, and view overrides in a single pass.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="oldIdToNewIdMap">Dictionary mapping old element IDs (aliases) to new element IDs (standards).</param>
        /// <returns>A <see cref="RedirectionResultModel"/> capturing count metrics and telemetry logs.</returns>
        public static RedirectionResultModel SwapElementReferences(Document doc, IDictionary<ElementId, ElementId> oldIdToNewIdMap)
        {
            return SwapElementReferences(doc, oldIdToNewIdMap, null);
        }

        /// <summary>
        /// Swaps references for a batch of element IDs across parameters, category styles, compound structures, and view overrides in a single pass.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="oldIdToNewIdMap">Dictionary mapping old element IDs (aliases) to new element IDs (standards).</param>
        /// <param name="trans">Optional caller-managed active transaction handle. If null, discrete internal transactions will be used.</param>
        /// <returns>A <see cref="RedirectionResultModel"/> capturing count metrics and telemetry logs.</returns>
        public static RedirectionResultModel SwapElementReferences(Document doc, IDictionary<ElementId, ElementId> oldIdToNewIdMap, Transaction trans = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (oldIdToNewIdMap == null) throw new ArgumentNullException(nameof(oldIdToNewIdMap));

            RedirectionResultModel result = new RedirectionResultModel();
            if (oldIdToNewIdMap.Count == 0) return result;

            List<Category> allCats = GetAllCategories(doc);

            bool isCallerManaged = doc.IsModifiable || (trans != null && trans.GetStatus() == TransactionStatus.Started);

            if (isCallerManaged)
            {
                ExecuteSwapPhases(doc, oldIdToNewIdMap, allCats, result);
            }
            else
            {
                using (Transaction internalTrans = new Transaction(doc, "Swap Element References"))
                {
                    internalTrans.Start();
                    ExecuteSwapPhases(doc, oldIdToNewIdMap, allCats, result);
                    internalTrans.Commit();
                }
            }

            return result;
        }

        /// <summary>
        /// Executes all 4 reference swapping phases across elements, category styles, compound structures, and view graphic overrides for the redirection dictionary.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="oldIdToNewIdMap">Dictionary mapping old element IDs to target standard element IDs.</param>
        /// <param name="allCats">Pre-gathered list of all categories and subcategories in the document.</param>
        /// <param name="result">The redirection result model to update with telemetry metrics.</param>
        private static void ExecuteSwapPhases(Document doc, IDictionary<ElementId, ElementId> oldIdToNewIdMap, List<Category> allCats, RedirectionResultModel result)
        {
            // 1a. Remap Group and AssemblyInstance element types
            try
            {
                var groups = new FilteredElementCollector(doc)
                    .OfClass(typeof(Group))
                    .Cast<Group>();

                bool groupsChanged = false;
                foreach (Group group in groups)
                {
                    if (group == null || !group.IsValidObject) continue;
                    ElementId groupTypeId = group.GetTypeId();
                    if (groupTypeId != null && oldIdToNewIdMap.TryGetValue(groupTypeId, out ElementId targetId))
                    {
                        GroupType targetGroupType = doc.GetElement(targetId) as GroupType;
                        if (targetGroupType != null)
                        {
                            try
                            {
                                group.GroupType = targetGroupType;
                                result.InstancesCount++;
                                groupsChanged = true;
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error setting GroupType for group {group.Id}: {ex.Message}");
                            }
                        }
                    }
                }

                if (groupsChanged)
                {
                    doc.Regenerate();
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error remapping groups: {ex.Message}");
            }

            try
            {
                var assemblies = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>();

                bool assembliesChanged = false;
                foreach (AssemblyInstance assembly in assemblies)
                {
                    if (assembly == null || !assembly.IsValidObject) continue;
                    ElementId asmTypeId = assembly.GetTypeId();
                    if (asmTypeId != null && oldIdToNewIdMap.TryGetValue(asmTypeId, out ElementId targetId))
                    {
                        AssemblyType targetAssemblyType = doc.GetElement(targetId) as AssemblyType;
                        if (targetAssemblyType != null)
                        {
                            try
                            {
                                assembly.ChangeTypeId(targetId);
                                result.InstancesCount++;
                                assembliesChanged = true;
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error changing type for assembly instance {assembly.Id}: {ex.Message}");
                            }
                        }
                    }
                }

                if (assembliesChanged)
                {
                    doc.Regenerate();
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error remapping assemblies: {ex.Message}");
            }

            // 1b. Swap FamilyInstance symbols and ElementId parameters of all elements
            try
            {
                var familyInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi != null && fi.IsValidObject)
                    .ToList();

                var activatedSymbols = new HashSet<ElementId>();

                foreach (FamilyInstance fi in familyInstances)
                {
                    // Skip family instances inside a group to prevent group definition corruption
                    if (fi.GroupId != null && fi.GroupId != ElementId.InvalidElementId) continue;

                    ElementId currentTypeId = fi.GetTypeId();
                    if (currentTypeId != null && oldIdToNewIdMap.TryGetValue(currentTypeId, out ElementId targetId))
                    {
                        FamilySymbol targetSymbol = doc.GetElement(targetId) as FamilySymbol;
                        if (targetSymbol != null)
                        {
                            try
                            {
                                if (!targetSymbol.IsActive && !activatedSymbols.Contains(targetSymbol.Id))
                                {
                                    targetSymbol.Activate();
                                    doc.Regenerate();
                                    activatedSymbols.Add(targetSymbol.Id);
                                }

                                fi.Symbol = targetSymbol;
                                result.InstancesCount++;
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error remapping FamilyInstance {fi.Id} symbol: {ex.Message}");
                            }
                        }
                    }
                }

                // Swap in all writeable ElementId parameters of all elements (instances and types)
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements();

                var allElements = instances.Concat(types).ToList();

                foreach (Element elem in allElements)
                {
                    if (elem == null || !elem.IsValidObject) continue;

                    // Skip internal C++ unmanaged elements or elements without valid categories
                    try
                    {
                        if (elem.Category == null) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    // Skip Group instances, Group types, Assemblies, and member elements inside a group to avoid corrupting group definition consistency
                    if (elem is Group || elem is GroupType || elem is AssemblyInstance || elem is AssemblyType) continue;
                    try
                    {
                        if (elem.GroupId != null && elem.GroupId != ElementId.InvalidElementId) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    try
                    {
                        ElementId catId = elem.Category.Id;
                        if (catId == new ElementId(BuiltInCategory.OST_IOSModelGroups) ||
                            catId == new ElementId(BuiltInCategory.OST_IOSDetailGroups))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    ParameterSet? paramSet = null;
                    try
                    {
                        paramSet = elem.Parameters;
                    }
                    catch
                    {
                        continue;
                    }

                    if (paramSet == null) continue;

                    bool elemModified = false;
                    foreach (Parameter param in paramSet)
                    {
                        try
                        {
                            if (param == null || !param.HasValue || param.IsReadOnly || param.StorageType != StorageType.ElementId) continue;

                            // Skip internal group management or type definition built-in parameters
                            if (param.Definition is InternalDefinition internalDef)
                            {
                                BuiltInParameter bip = internalDef.BuiltInParameter;
                                if (bip == BuiltInParameter.ELEM_TYPE_PARAM ||
                                    bip == BuiltInParameter.ELEM_CATEGORY_PARAM ||
                                    bip == BuiltInParameter.ELEM_FAMILY_PARAM ||
                                    bip == BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM)
                                {
                                    continue;
                                }
                            }

                            ElementId valId = param.AsElementId();
                            if (valId != null && oldIdToNewIdMap.TryGetValue(valId, out ElementId targetId))
                            {
                                try
                                {
                                    param.Set(targetId);
                                    result.ParametersCount++;
                                    elemModified = true;
                                }
                                catch (Exception ex)
                                {
                                    result.Warnings.Add($"Parameter set error on element {elem.Id}: {ex.Message}");
                                }
                            }
                        }
                        catch
                        {
                            // Skip any unmanaged native C++ parameter read failures safely
                            continue;
                        }
                    }

                    if (!(elem is ElementType))
                    {
                        try
                        {
                            ElementId typeId = elem.GetTypeId();
                            if (typeId != null && oldIdToNewIdMap.TryGetValue(typeId, out ElementId targetTypeId))
                            {
                                if (elem.IsValidType(targetTypeId))
                                {
                                    elem.ChangeTypeId(targetTypeId);
                                    elemModified = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"ChangeTypeId error on element {elem.Id}: {ex.Message}");
                        }
                    }

                    if (elemModified && !(elem is ElementType))
                    {
                        result.InstancesCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error swapping parameters or family instances: {ex.Message}");
            }

            // 2. Swap in Category default styles (Material, LinePatternId for cut/projection)
            try
            {
                foreach (Category cat in allCats)
                {
                    if (cat == null) continue;

                    // Swap Material reference
                    if (cat.Material != null && oldIdToNewIdMap.TryGetValue(cat.Material.Id, out ElementId newMatId))
                    {
                        try
                        {
                            cat.Material = doc.GetElement(newMatId) as Material;
                            result.CategoryStylesCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Error setting category material on category '{cat?.Name ?? "Unknown Category"}': {ex.Message}");
                        }
                    }

                    // Swap projection/cut LinePatternId references
                    try
                    {
                        ElementId projPatternId = cat.GetLinePatternId(GraphicsStyleType.Projection);
                        if (projPatternId != null && oldIdToNewIdMap.TryGetValue(projPatternId, out ElementId newProjId))
                        {
                            cat.SetLinePatternId(newProjId, GraphicsStyleType.Projection);
                            result.CategoryStylesCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error setting projection line pattern on category '{cat?.Name ?? "Unknown Category"}': {ex.Message}");
                    }

                    try
                    {
                        ElementId cutPatternId = cat.GetLinePatternId(GraphicsStyleType.Cut);
                        if (cutPatternId != null && oldIdToNewIdMap.TryGetValue(cutPatternId, out ElementId newCutId))
                        {
                            cat.SetLinePatternId(newCutId, GraphicsStyleType.Cut);
                            result.CategoryStylesCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error setting cut line pattern on category '{cat?.Name ?? "Unknown Category"}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error swapping categories: {ex.Message}");
            }

            // 3. Swap in Compound Structures (WallTypes, FloorTypes, RoofTypes, CeilingTypes)
            try
            {
                var hostTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(HostObjAttributes))
                    .Cast<HostObjAttributes>()
                    .ToList();

                foreach (HostObjAttributes hostType in hostTypes)
                {
                    if (hostType == null || !hostType.IsValidObject) continue;

                    CompoundStructure cs = hostType.GetCompoundStructure();
                    if (cs != null)
                    {
                        IList<CompoundStructureLayer> layers = cs.GetLayers();
                        bool changed = false;

                        for (int i = 0; i < layers.Count; i++)
                        {
                            CompoundStructureLayer layer = layers[i];
                            if (layer.MaterialId != null && oldIdToNewIdMap.TryGetValue(layer.MaterialId, out ElementId newMatId))
                            {
                                layer.MaterialId = newMatId;
                                changed = true;
                                result.CompoundStructureLayersCount++;
                            }
                            if (layer.DeckProfileId != null && oldIdToNewIdMap.TryGetValue(layer.DeckProfileId, out ElementId newProfileId))
                            {
                                layer.DeckProfileId = newProfileId;
                                changed = true;
                                result.CompoundStructureLayersCount++;
                            }
                        }

                        if (changed)
                        {
                            try
                            {
                                cs.SetLayers(layers);
                                hostType.SetCompoundStructure(cs);
                            }
                            catch (Exception ex)
                            {
                                result.Errors.Add($"Error setting compound structure for '{hostType.Name}': {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error swapping compound structures: {ex.Message}");
            }

            // 4. Swap in View Graphic Overrides (only if map contains LinePattern or FillPattern elements)
            try
            {
                bool hasPatternKeys = oldIdToNewIdMap.Keys.Any(id =>
                {
                    if (id == null || id == ElementId.InvalidElementId) return false;
                    Element e = doc.GetElement(id);
                    return e is LinePatternElement || e is FillPatternElement;
                });

                if (hasPatternKeys)
                {
                    var views = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitView))
                        .Cast<RevitView>()
                        .ToList();

                    foreach (RevitView view in views)
                {
                    if (view == null || !view.IsValidObject) continue;

                    if (!IsAllowedGraphicalViewType(view.ViewType))
                    {
                        continue;
                    }

                    bool supportsOverrides = false;
                    try
                    {
                        supportsOverrides = view.IsTemplate || view.AreGraphicsOverridesAllowed();
                    }
                    catch
                    {
                        // Some view types throw on AreGraphicsOverridesAllowed
                    }

                    if (!supportsOverrides) continue;

                    foreach (Category cat in allCats)
                    {
                        if (cat == null) continue;
                        try
                        {
                            OverrideGraphicSettings settings = view.GetCategoryOverrides(cat.Id);
                            if (SwapPatternOverrides(settings, oldIdToNewIdMap, result, PatternCheckers))
                            {
                                view.SetCategoryOverrides(cat.Id, settings);
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Error applying view graphic override on view '{view?.Name ?? "Unknown View"}' for category '{cat?.Name ?? "Unknown Category"}': {ex.Message}");
                        }
                    }

                    // Filter overrides
                    try
                    {
                        ICollection<ElementId> filterIds = view.GetFilters();
                        if (filterIds != null && filterIds.Count > 0)
                        {
                            foreach (ElementId filterId in filterIds)
                            {
                                if (filterId == null || filterId == ElementId.InvalidElementId) continue;
                                try
                                {
                                    OverrideGraphicSettings settings = view.GetFilterOverrides(filterId);
                                    if (SwapPatternOverrides(settings, oldIdToNewIdMap, result, PatternCheckers))
                                    {
                                        view.SetFilterOverrides(filterId, settings);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    result.Warnings.Add($"Error applying view filter graphic override on view '{view.Name}' for filter '{filterId}': {ex.Message}");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Benign for view types that do not support filter retrieval
                    }

                    // 4b. Element-Level Graphic Overrides
                    if (!view.IsTemplate)
                    {
                        string viewName = "Unknown View";
                        try
                        {
                            viewName = view.Name;
                        }
                        catch { }

                        try
                        {
                            ICollection<ElementId> elementIdsInView = new FilteredElementCollector(doc, view.Id)
                                .WhereElementIsNotElementType()
                                .ToElementIds();

                            foreach (ElementId elemId in elementIdsInView)
                            {
                                if (elemId == null || elemId == ElementId.InvalidElementId) continue;

                                try
                                {
                                    Element elem = doc.GetElement(elemId);
                                    if (elem == null || !elem.IsValidObject) continue;

                                    // Skip Group instances and elements inside groups as they do not support individual view graphic overrides
                                    if (elem is Group || (elem.GroupId != null && elem.GroupId != ElementId.InvalidElementId)) continue;

                                    OverrideGraphicSettings settings = view.GetElementOverrides(elemId);
                                    if (SwapPatternOverrides(settings, oldIdToNewIdMap, result, PatternCheckers))
                                    {
                                        view.SetElementOverrides(elemId, settings);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    result.Warnings.Add($"Error applying element graphic override on view '{viewName}' for element '{elemId}': {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Error retrieving elements in view '{viewName}': {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
            {
                result.Errors.Add($"Error swapping view overrides: {ex.Message}");
            }
        }

        private static readonly HashSet<ViewType> AllowedGraphicalViewTypes = new HashSet<ViewType>
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.Elevation,
            ViewType.Section,
            ViewType.ThreeD,
            ViewType.EngineeringPlan,
            ViewType.AreaPlan,
            ViewType.DraftingView,
            ViewType.Detail,
            ViewType.Rendering,
            ViewType.Walkthrough
        };

        /// <summary>
        /// Determines whether a given view type supports graphical category and element overrides.
        /// </summary>
        /// <param name="viewType">The Revit view type.</param>
        /// <returns>True if the view type supports graphic overrides; otherwise false.</returns>
        private static bool IsAllowedGraphicalViewType(ViewType viewType)
        {
            return AllowedGraphicalViewTypes.Contains(viewType);
        }

        /// <summary>
        /// Swaps graphic line pattern overrides within OverrideGraphicSettings based on the redirection dictionary.
        /// </summary>
        /// <param name="settings">The OverrideGraphicSettings instance to inspect and update.</param>
        /// <param name="oldIdToNewIdMap">Dictionary mapping old element IDs to target standard element IDs.</param>
        /// <param name="result">The redirection result model to accumulate metrics.</param>
        /// <param name="PatternCheckers">Delegates to get and set pattern properties.</param>
        /// <returns>True if any pattern override was modified; otherwise false.</returns>
        private static bool SwapPatternOverrides(
            OverrideGraphicSettings settings,
            IDictionary<ElementId, ElementId> oldIdToNewIdMap,
            RedirectionResultModel result,
            (Func<OverrideGraphicSettings, ElementId> GetPattern, Action<OverrideGraphicSettings, ElementId> SetPattern)[] PatternCheckers)
        {
            if (settings == null) return false;
            bool changed = false;
            foreach (var (getPattern, setPattern) in PatternCheckers)
            {
                ElementId patternId = getPattern(settings);
                if (patternId != null && oldIdToNewIdMap.TryGetValue(patternId, out ElementId newId))
                {
                    setPattern(settings, newId);
                    changed = true;
                    result.ViewGraphicOverridesCount++;
                }
            }
            return changed;
        }

        /// <summary>
        /// Recursively gathers all categories and subcategories in the document.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <returns>A list of all Category objects present in the document.</returns>
        private static List<Category> GetAllCategories(Document doc)
        {
            List<Category> list = new List<Category>();
            if (doc?.Settings?.Categories != null)
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    AddCategoryAndSubcategories(cat, list);
                }
            }
            return list;
        }

        /// <summary>
        /// Recursively gathers a category and all of its subcategories.
        /// </summary>
        /// <param name="cat">The parent category.</param>
        /// <param name="list">The list to populate.</param>
        private static void AddCategoryAndSubcategories(Category cat, List<Category> list)
        {
            if (cat == null) return;
            list.Add(cat);
            if (cat.SubCategories != null)
            {
                foreach (Category subCat in cat.SubCategories)
                {
                    AddCategoryAndSubcategories(subCat, list);
                }
            }
        }
    }
}
