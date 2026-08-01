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
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (oldId == null) throw new ArgumentNullException(nameof(oldId));
            if (newId == null) throw new ArgumentNullException(nameof(newId));

            RedirectionResultModel result = new RedirectionResultModel();
            List<Category> allCats = GetAllCategories(doc);

            bool isCallerManaged = doc.IsModifiable || (trans != null && trans.GetStatus() == TransactionStatus.Started);

            if (isCallerManaged)
            {
                ExecuteSwapPhases(doc, oldId, newId, allCats, result);
            }
            else
            {
                using (Transaction internalTrans = new Transaction(doc, "Swap Element References"))
                {
                    internalTrans.Start();
                    ExecuteSwapPhases(doc, oldId, newId, allCats, result);
                    internalTrans.Commit();
                }
            }

            return result;
        }

        private static void ExecuteSwapPhases(Document doc, ElementId oldId, ElementId newId, List<Category> allCats, RedirectionResultModel result)
        {
            // 1a. Remap Group and AssemblyInstance element types
            try
            {
                GroupType targetGroupType = doc.GetElement(newId) as GroupType;
                if (targetGroupType != null)
                {
                    var groups = new FilteredElementCollector(doc)
                        .OfClass(typeof(Group))
                        .Cast<Group>();

                    foreach (Group group in groups)
                    {
                        if (group == null || !group.IsValidObject) continue;
                        if (oldId.Equals(group.GetTypeId()))
                        {
                            try
                            {
                                group.GroupType = targetGroupType;
                                result.InstancesCount++;
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error setting GroupType for group {group.Id}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error remapping groups: {ex.Message}");
            }

            try
            {
                AssemblyType targetAssemblyType = doc.GetElement(newId) as AssemblyType;
                if (targetAssemblyType != null)
                {
                    var assemblies = new FilteredElementCollector(doc)
                        .OfClass(typeof(AssemblyInstance))
                        .Cast<AssemblyInstance>();

                    foreach (AssemblyInstance assembly in assemblies)
                    {
                        if (assembly == null || !assembly.IsValidObject) continue;
                        if (oldId.Equals(assembly.GetTypeId()))
                        {
                            try
                            {
                                assembly.ChangeTypeId(newId);
                                result.InstancesCount++;
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error changing type for assembly instance {assembly.Id}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error remapping assemblies: {ex.Message}");
            }

            // 1b. Swap FamilyInstance symbols and ElementId parameters of all elements
            try
            {
                // Swap FamilyInstance symbols if newId resolves to a FamilySymbol
                FamilySymbol targetSymbol = doc.GetElement(newId) as FamilySymbol;
                if (targetSymbol != null)
                {
                    var familyInstances = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(fi => fi != null && fi.IsValidObject && oldId.Equals(fi.GetTypeId()))
                        .ToList();

                    if (familyInstances.Count > 0)
                    {
                        if (!targetSymbol.IsActive)
                        {
                            targetSymbol.Activate();
                            doc.Regenerate();
                        }

                        foreach (FamilyInstance fi in familyInstances)
                        {
                            try
                            {
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

                // 1b. Swap in all writeable ElementId parameters of all elements (instances and types)
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements();

                var allElements = instances.Concat(types).ToList();

                foreach (Element elem in allElements)
                {
                    if (elem == null || !elem.IsValidObject || elem.Parameters == null) continue;

                    bool elemModified = false;
                    foreach (Parameter param in elem.Parameters)
                    {
                        if (param != null && !param.IsReadOnly && param.StorageType == StorageType.ElementId)
                        {
                            ElementId valId = param.AsElementId();
                            if (oldId.Equals(valId))
                            {
                                try
                                {
                                    param.Set(newId);
                                    result.ParametersCount++;
                                    elemModified = true;
                                }
                                catch (Exception ex)
                                {
                                    result.Warnings.Add($"Parameter set error on element {elem.Id}: {ex.Message}");
                                }
                            }
                        }
                    }

                    if (!(elem is ElementType))
                    {
                        try
                        {
                            if (elem.GetTypeId() == oldId && elem.IsValidType(newId))
                            {
                                elem.ChangeTypeId(newId);
                                elemModified = true;
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
                    if (cat.Material != null && oldId.Equals(cat.Material.Id))
                    {
                        try
                        {
                            cat.Material = doc.GetElement(newId) as Material;
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
                        if (oldId.Equals(cat.GetLinePatternId(GraphicsStyleType.Projection)))
                        {
                            cat.SetLinePatternId(newId, GraphicsStyleType.Projection);
                            result.CategoryStylesCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error setting projection line pattern on category '{cat?.Name ?? "Unknown Category"}': {ex.Message}");
                    }

                    try
                    {
                        if (oldId.Equals(cat.GetLinePatternId(GraphicsStyleType.Cut)))
                        {
                            cat.SetLinePatternId(newId, GraphicsStyleType.Cut);
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
                            if (oldId.Equals(layer.MaterialId))
                            {
                                layer.MaterialId = newId;
                                changed = true;
                                result.CompoundStructureLayersCount++;
                            }
                            if (oldId.Equals(layer.DeckProfileId))
                            {
                                layer.DeckProfileId = newId;
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

            // 4. Swap in View Graphic Overrides
            try
            {
                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitView))
                    .Cast<RevitView>()
                    .ToList();



                foreach (RevitView view in views)
                {
                    if (view == null || !view.IsValidObject) continue;

                    bool supportsOverrides = false;
                    try
                    {
                        supportsOverrides = view.IsTemplate || view.AreGraphicsOverridesAllowed();
                    }
                    catch
                    {
                        // Some view types throw on AreGraphicsOverridesAllowed
                    }

                    if (supportsOverrides)
                    {
                        foreach (Category cat in allCats)
                        {
                            if (cat == null) continue;
                            try
                            {
                                OverrideGraphicSettings settings = view.GetCategoryOverrides(cat.Id);
                                if (SwapPatternOverrides(settings, oldId, newId, result, patternCheckers))
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
                                        if (SwapPatternOverrides(settings, oldId, newId, result, patternCheckers))
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
                    }

                    // 4b. Element-Level Graphic Overrides
                    if (!view.IsTemplate)
                    {
                        try
                        {
                            var elementsInView = new FilteredElementCollector(doc, view.Id)
                                .WhereElementIsNotElementType()
                                .ToElements();

                            foreach (Element elem in elementsInView)
                            {
                                if (elem == null || !elem.IsValidObject) continue;

                                try
                                {
                                    OverrideGraphicSettings settings = view.GetElementOverrides(elem.Id);
                                    if (SwapPatternOverrides(settings, oldId, newId, result, patternCheckers))
                                    {
                                        view.SetElementOverrides(elem.Id, settings);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    result.Warnings.Add($"Error applying element graphic override on view '{view.Name}' for element '{elem.Id}': {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Error retrieving elements in view '{view.Name}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error swapping view overrides: {ex.Message}");
            }
        }

        private static bool SwapPatternOverrides(
            OverrideGraphicSettings settings,
            ElementId oldId,
            ElementId newId,
            RedirectionResultModel result,
            (Func<OverrideGraphicSettings, ElementId> GetPattern, Action<OverrideGraphicSettings, ElementId> SetPattern)[] patternCheckers)
        {
            if (settings == null) return false;
            bool changed = false;
            foreach (var (getPattern, setPattern) in patternCheckers)
            {
                ElementId patternId = getPattern(settings);
                if (patternId != null && oldId.Equals(patternId))
                {
                    setPattern(settings, newId);
                    changed = true;
                    result.ViewGraphicOverridesCount++;
                }
            }
            return changed;
        }
        /// Recursively gathers all categories and subcategories in the document.
        /// </summary>
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
