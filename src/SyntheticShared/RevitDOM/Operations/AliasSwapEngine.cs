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

            bool isCallerManaged = trans != null && trans.GetStatus() == TransactionStatus.Started;

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
            // 1. Swap in all writeable ElementId parameters of all elements (instances and types)
            try
            {
                var instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements();

                var allElements = instances.Concat(types);

                foreach (Element elem in allElements)
                {
                    if (elem == null || !elem.IsValidObject) continue;

                    bool elemModified = false;
                    foreach (Parameter param in elem.Parameters)
                    {
                        if (param != null && !param.IsReadOnly && param.StorageType == StorageType.ElementId)
                        {
                            if (param.AsElementId() == oldId)
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

                    if (elemModified && !(elem is ElementType))
                    {
                        result.InstancesCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error swapping parameters: {ex.Message}");
            }

            // 2. Swap in Category default styles (Material, LinePatternId for cut/projection)
            try
            {
                foreach (Category cat in allCats)
                {
                    if (cat == null) continue;

                    // Swap Material reference
                    if (cat.Material != null && cat.Material.Id == oldId)
                    {
                        try
                        {
                            cat.Material = doc.GetElement(newId) as Material;
                            result.CategoryStylesCount++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add($"Error setting category material on category '{cat.Name}': {ex.Message}");
                        }
                    }

                    // Swap projection/cut LinePatternId references
                    try
                    {
                        if (cat.GetLinePatternId(GraphicsStyleType.Projection) == oldId)
                        {
                            cat.SetLinePatternId(newId, GraphicsStyleType.Projection);
                            result.CategoryStylesCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error setting projection line pattern on category '{cat.Name}': {ex.Message}");
                    }

                    try
                    {
                        if (cat.GetLinePatternId(GraphicsStyleType.Cut) == oldId)
                        {
                            cat.SetLinePatternId(newId, GraphicsStyleType.Cut);
                            result.CategoryStylesCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Error setting cut line pattern on category '{cat.Name}': {ex.Message}");
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
                            if (layer.MaterialId == oldId)
                            {
                                layer.MaterialId = newId;
                                changed = true;
                                result.CompoundStructureLayersCount++;
                            }
                            if (layer.DeckProfileId == oldId)
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

                var patternCheckers = new (Func<OverrideGraphicSettings, ElementId> GetPattern, Action<OverrideGraphicSettings, ElementId> SetPattern)[]
                {
                    (s => s.ProjectionLinePatternId, (s, id) => s.SetProjectionLinePatternId(id)),
                    (s => s.CutLinePatternId, (s, id) => s.SetCutLinePatternId(id)),
                    (s => s.SurfaceForegroundPatternId, (s, id) => s.SetSurfaceForegroundPatternId(id)),
                    (s => s.SurfaceBackgroundPatternId, (s, id) => s.SetSurfaceBackgroundPatternId(id)),
                    (s => s.CutForegroundPatternId, (s, id) => s.SetCutForegroundPatternId(id)),
                    (s => s.CutBackgroundPatternId, (s, id) => s.SetCutBackgroundPatternId(id))
                };

                foreach (RevitView view in views)
                {
                    if (view == null || !view.IsValidObject) continue;

                    if (view.IsTemplate || view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.CeilingPlan ||
                        view.ViewType == ViewType.Elevation || view.ViewType == ViewType.Section || view.ViewType == ViewType.ThreeD ||
                        view.ViewType == ViewType.DraftingView || view.ViewType == ViewType.AreaPlan)
                    {
                        foreach (Category cat in allCats)
                        {
                            if (cat == null) continue;
                            try
                            {
                                OverrideGraphicSettings settings = view.GetCategoryOverrides(cat.Id);
                                if (settings != null)
                                {
                                    bool changed = false;

                                    foreach (var (getPattern, setPattern) in patternCheckers)
                                    {
                                        if (getPattern(settings) == oldId)
                                        {
                                            setPattern(settings, newId);
                                            changed = true;
                                            result.ViewGraphicOverridesCount++;
                                        }
                                    }

                                    if (changed)
                                    {
                                        view.SetCategoryOverrides(cat.Id, settings);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                result.Warnings.Add($"Error applying view graphic override on view '{view.Name}' for category '{cat.Name}': {ex.Message}");
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

                /// <summary>
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
