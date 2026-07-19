using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

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
        public static void SwapElementReferences(Document doc, ElementId oldId, ElementId newId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (oldId == null) throw new ArgumentNullException(nameof(oldId));
            if (newId == null) throw new ArgumentNullException(nameof(newId));

            // 1. Swap in all writeable ElementId parameters of all elements (instances and types)
            using (Transaction trans = new Transaction(doc, "Swap Parameter References"))
            {
                trans.Start();
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

                        foreach (Parameter param in elem.Parameters)
                        {
                            if (param != null && !param.IsReadOnly && param.StorageType == StorageType.ElementId)
                            {
                                if (param.AsElementId() == oldId)
                                {
                                    try
                                    {
                                        param.Set(newId);
                                    }
                                    catch (Exception)
                                    {
                                        // Ignore parameter set errors (e.g. read-only parameter, type constraints)
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error swapping parameters: {ex.Message}");
                }
                trans.Commit();
            }

            // 2. Swap in Category default styles (Material, LinePatternId for cut/projection)
            using (Transaction trans = new Transaction(doc, "Swap Category default styles"))
            {
                trans.Start();
                try
                {
                    List<Category> allCats = new List<Category>();
                    foreach (Category cat in doc.Settings.Categories)
                    {
                        AddCategoryAndSubcategories(cat, allCats);
                    }

                    foreach (Category cat in allCats)
                    {
                        if (cat == null) continue;

                        // Swap Material reference
                        if (cat.Material != null && cat.Material.Id == oldId)
                        {
                            try
                            {
                                cat.Material = doc.GetElement(newId) as Material;
                            }
                            catch (Exception) { }
                        }

                        // Swap projection/cut LinePatternId references
                        try
                        {
                            if (cat.GetLinePatternId(GraphicsStyleType.Projection) == oldId)
                            {
                                cat.SetLinePatternId(newId, GraphicsStyleType.Projection);
                            }
                        }
                        catch (Exception) { }

                        try
                        {
                            if (cat.GetLinePatternId(GraphicsStyleType.Cut) == oldId)
                            {
                                cat.SetLinePatternId(newId, GraphicsStyleType.Cut);
                            }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error swapping categories: {ex.Message}");
                }
                trans.Commit();
            }

            // 3. Swap in Compound Structures (WallTypes, FloorTypes, RoofTypes, CeilingTypes)
            using (Transaction trans = new Transaction(doc, "Swap Compound Structure layers"))
            {
                trans.Start();
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
                                }
                                if (layer.DeckProfileId == oldId)
                                {
                                    layer.DeckProfileId = newId;
                                    changed = true;
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
                                    Console.WriteLine($"Error setting compound structure for '{hostType.Name}': {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error swapping compound structures: {ex.Message}");
                }
                trans.Commit();
            }

            // 4. Swap in View Graphic Overrides
            using (Transaction trans = new Transaction(doc, "Swap View Graphic overrides"))
            {
                trans.Start();
                try
                {
                    var views = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitView))
                        .Cast<RevitView>()
                        .ToList();

                    List<Category> allCats = new List<Category>();
                    foreach (Category cat in doc.Settings.Categories)
                    {
                        AddCategoryAndSubcategories(cat, allCats);
                    }

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

                                        if (settings.ProjectionLinePatternId == oldId)
                                        {
                                            settings.SetProjectionLinePatternId(newId);
                                            changed = true;
                                        }
                                        if (settings.CutLinePatternId == oldId)
                                        {
                                            settings.SetCutLinePatternId(newId);
                                            changed = true;
                                        }
                                        if (settings.SurfaceForegroundPatternId == oldId)
                                        {
                                            settings.SetSurfaceForegroundPatternId(newId);
                                            changed = true;
                                        }
                                        if (settings.SurfaceBackgroundPatternId == oldId)
                                        {
                                            settings.SetSurfaceBackgroundPatternId(newId);
                                            changed = true;
                                        }
                                        if (settings.CutForegroundPatternId == oldId)
                                        {
                                            settings.SetCutForegroundPatternId(newId);
                                            changed = true;
                                        }
                                        if (settings.CutBackgroundPatternId == oldId)
                                        {
                                            settings.SetCutBackgroundPatternId(newId);
                                            changed = true;
                                        }

                                        if (changed)
                                        {
                                            view.SetCategoryOverrides(cat.Id, settings);
                                        }
                                    }
                                }
                                catch (Exception) { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error swapping view overrides: {ex.Message}");
                }
                trans.Commit();
            }
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
