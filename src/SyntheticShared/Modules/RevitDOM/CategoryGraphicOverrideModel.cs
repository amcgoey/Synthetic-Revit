using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Model representing graphic override settings for a specific category within a Revit view.
    /// </summary>
    public class CategoryGraphicOverridesModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the target category model.
        /// </summary>
        public CategoryIdModel Category { get; set; } = new CategoryIdModel();

        /// <summary>
        /// Gets or sets the parent category model, if one exists.
        /// </summary>
        public CategoryIdModel? ParentCategory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the category is hidden in the view.
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// Gets or sets the graphic override settings for the category.
        /// </summary>
        public OverrideGraphicSettingsModel? GraphicOverride { get; set; }

        /// <summary>
        /// Initializes a new instance of the CategoryGraphicOverridesModel class.
        /// </summary>
        public CategoryGraphicOverridesModel () { }

        /// <summary>
        /// Determines if the graphic overrides have been modified from default.
        /// </summary>
        /// <returns>True if modified, false otherwise.</returns>
        public bool IsModified()
        {
            bool modified = false;

            if (this.GraphicOverride != null)
            {
                if (this.GraphicOverride.IsModified) { modified = true; }
            }
            if (this.IsHidden == true) { modified = true; }

            return modified;
        }
    }

    public static class CategoryGraphicOverrideExtensions
    {
        public static CategoryGraphicOverridesModel ToModel(this Category category, View view)
        {
            if (category == null || view == null) return null;
            Document document = view.Document;
            var model = new CategoryGraphicOverridesModel();
            model.Category = category.ToCategoryIdModel(document, false);
            model.IsHidden = view.GetCategoryHidden(category.Id);

            if (category.Parent != null)
            {
                model.ParentCategory = category.Parent.ToCategoryIdModel(document, false);
            }

            OverrideGraphicSettings overrideSettings = view.GetCategoryOverrides(category.Id);
            OverrideGraphicSettingsModel serialOverride = ToModel(overrideSettings, category, document);

            if (serialOverride != null && serialOverride.IsModified)
            {
                model.GraphicOverride = serialOverride;
            }

            return model;
        }

        public static OverrideGraphicSettingsModel ToModel(this OverrideGraphicSettings ogs, Category category, Document document)
        {
            if (ogs == null) return null;
            var model = new OverrideGraphicSettingsModel();
            model.IsModified = IsModified(ogs);
            if (model.IsModified)
            {
                PopulateProperties(model, ogs, document);
            }
            return model;
        }

        public static OverrideGraphicSettingsModel ToModel(this OverrideGraphicSettings ogs, Document document)
        {
            if (ogs == null) return null;
            var model = new OverrideGraphicSettingsModel();
            PopulateProperties(model, ogs, document);
            model.IsModified = IsModified(ogs);
            return model;
        }

        private static void PopulateProperties(OverrideGraphicSettingsModel model, OverrideGraphicSettings ogs, Document document)
        {
            model.IsSurfaceBackgroundPatternVisible = ogs.IsSurfaceBackgroundPatternVisible;
            model.SurfaceBackgroundPatternColor = ogs.SurfaceBackgroundPatternColor.ToModel();
            model.SurfaceBackgroundPatternId = ogs.SurfaceBackgroundPatternId.ToModel(document, false);

            model.IsSurfaceForegroundPatternVisible = ogs.IsSurfaceForegroundPatternVisible;
            model.SurfaceForegroundPatternColor = ogs.SurfaceForegroundPatternColor.ToModel();
            model.SurfaceForegroundPatternId = ogs.SurfaceForegroundPatternId.ToModel(document, false);

            model.ProjectionLineColor = ogs.ProjectionLineColor.ToModel();
            ElementId projectionLinePatternId = ogs.ProjectionLinePatternId;
            if (projectionLinePatternId == ElementId.InvalidElementId || projectionLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2022 || REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.ProjectionLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.ProjectionLinePatternId = projectionLinePatternId.ToModel(document, false);
            }
            model.ProjectionLineWeight = ogs.ProjectionLineWeight;

            model.IsCutBackgroundPatternVisible = ogs.IsCutBackgroundPatternVisible;
            model.CutBackgroundPatternColor = ogs.CutBackgroundPatternColor.ToModel();
            model.CutBackgroundPatternId = ogs.CutBackgroundPatternId.ToModel(document, false);

            model.IsCutForegroundPatternVisible = ogs.IsCutForegroundPatternVisible;
            model.CutForegroundPatternColor = ogs.CutForegroundPatternColor.ToModel();
            model.CutForegroundPatternId = ogs.CutForegroundPatternId.ToModel(document, false);

            model.CutLineColor = ogs.CutLineColor.ToModel();
            ElementId cutLinePatternId = ogs.CutLinePatternId;
            if (cutLinePatternId == ElementId.InvalidElementId || cutLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2022 || REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.CutLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.CutLinePatternId = cutLinePatternId.ToModel(document, false);
            }
            model.CutLineWeight = ogs.CutLineWeight;

            model.Transparency = ogs.Transparency;
            model.Halftone = ogs.Halftone;
            model.DetailLevel = new EnumModel(typeof(ViewDetailLevel), ogs.DetailLevel);
        }

        public static CategoryGraphicOverridesModel ToModel(this Category category, View view, IIdentityService identityService)
        {
            if (category == null || view == null) return null;
            Document document = view.Document;
            var model = new CategoryGraphicOverridesModel();
            model.Category = category.ToCategoryIdModel(document, false);
            model.IsHidden = view.GetCategoryHidden(category.Id);

            if (category.Parent != null)
            {
                model.ParentCategory = category.Parent.ToCategoryIdModel(document, false);
            }

            OverrideGraphicSettings overrideSettings = view.GetCategoryOverrides(category.Id);
            OverrideGraphicSettingsModel serialOverride = ToModel(overrideSettings, category, document, identityService);

            if (serialOverride != null && serialOverride.IsModified)
            {
                model.GraphicOverride = serialOverride;
            }

            return model;
        }

        public static OverrideGraphicSettingsModel ToModel(this OverrideGraphicSettings ogs, Category category, Document document, IIdentityService identityService)
        {
            if (ogs == null) return null;
            var model = new OverrideGraphicSettingsModel();
            model.IsModified = IsModified(ogs);
            if (model.IsModified)
            {
                PopulateProperties(model, ogs, document, identityService);
            }
            return model;
        }

        public static OverrideGraphicSettingsModel ToModel(this OverrideGraphicSettings ogs, Document document, IIdentityService identityService)
        {
            if (ogs == null) return null;
            var model = new OverrideGraphicSettingsModel();
            PopulateProperties(model, ogs, document, identityService);
            model.IsModified = IsModified(ogs);
            return model;
        }

        private static void PopulateProperties(OverrideGraphicSettingsModel model, OverrideGraphicSettings ogs, Document document, IIdentityService identityService)
        {
            model.IsSurfaceBackgroundPatternVisible = ogs.IsSurfaceBackgroundPatternVisible;
            model.SurfaceBackgroundPatternColor = ogs.SurfaceBackgroundPatternColor.ToModel();
            model.SurfaceBackgroundPatternId = identityService.ToModel(ogs.SurfaceBackgroundPatternId, document, false);

            model.IsSurfaceForegroundPatternVisible = ogs.IsSurfaceForegroundPatternVisible;
            model.SurfaceForegroundPatternColor = ogs.SurfaceForegroundPatternColor.ToModel();
            model.SurfaceForegroundPatternId = identityService.ToModel(ogs.SurfaceForegroundPatternId, document, false);

            model.ProjectionLineColor = ogs.ProjectionLineColor.ToModel();
            ElementId projectionLinePatternId = ogs.ProjectionLinePatternId;
            if (projectionLinePatternId == ElementId.InvalidElementId || projectionLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2022 || REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.ProjectionLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.ProjectionLinePatternId = identityService.ToModel(projectionLinePatternId, document, false);
            }
            model.ProjectionLineWeight = ogs.ProjectionLineWeight;

            model.IsCutBackgroundPatternVisible = ogs.IsCutBackgroundPatternVisible;
            model.CutBackgroundPatternColor = ogs.CutBackgroundPatternColor.ToModel();
            model.CutBackgroundPatternId = identityService.ToModel(ogs.CutBackgroundPatternId, document, false);

            model.IsCutForegroundPatternVisible = ogs.IsCutForegroundPatternVisible;
            model.CutForegroundPatternColor = ogs.CutForegroundPatternColor.ToModel();
            model.CutForegroundPatternId = identityService.ToModel(ogs.CutForegroundPatternId, document, false);

            model.CutLineColor = ogs.CutLineColor.ToModel();
            ElementId cutLinePatternId = ogs.CutLinePatternId;
            if (cutLinePatternId == ElementId.InvalidElementId || cutLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2022 || REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.CutLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.CutLinePatternId = identityService.ToModel(cutLinePatternId, document, false);
            }
            model.CutLineWeight = ogs.CutLineWeight;

            model.Transparency = ogs.Transparency;
            model.Halftone = ogs.Halftone;
            model.DetailLevel = new EnumModel(typeof(ViewDetailLevel), ogs.DetailLevel);
        }

        private static bool IsModified(OverrideGraphicSettings ogs)
        {
            bool isMod = false;
            if (!ogs.IsSurfaceBackgroundPatternVisible) isMod = true;
            if (ogs.SurfaceBackgroundPatternColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.SurfaceBackgroundPatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.SurfaceBackgroundPatternId.Value != -1) isMod = true;
#endif

            if (!ogs.IsSurfaceForegroundPatternVisible) isMod = true;
            if (ogs.SurfaceForegroundPatternColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.SurfaceForegroundPatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.SurfaceForegroundPatternId.Value != -1) isMod = true;
#endif

            if (ogs.ProjectionLineColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.ProjectionLinePatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.ProjectionLinePatternId.Value != -1) isMod = true;
#endif
            if (ogs.ProjectionLineWeight != -1) isMod = true;

            if (!ogs.IsCutBackgroundPatternVisible) isMod = true;
            if (ogs.CutBackgroundPatternColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.CutBackgroundPatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.CutBackgroundPatternId.Value != -1) isMod = true;
#endif

            if (!ogs.IsCutForegroundPatternVisible) isMod = true;
            if (ogs.CutForegroundPatternColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.CutForegroundPatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.CutForegroundPatternId.Value != -1) isMod = true;
#endif

            if (ogs.CutLineColor.IsValid) isMod = true;
#if REVIT2022 || REVIT2023
            if (ogs.CutLinePatternId.IntegerValue != -1) isMod = true;
#else
            if (ogs.CutLinePatternId.Value != -1) isMod = true;
#endif
            if (ogs.CutLineWeight != -1) isMod = true;

            if (ogs.Transparency != 0) isMod = true;
            if (ogs.Halftone) isMod = true;
            if (ogs.DetailLevel != ViewDetailLevel.Undefined) isMod = true;

            return isMod;
        }

        public static List<CategoryGraphicOverridesModel> GetCategoryGraphicOverrides(View view)
        {
            List<CategoryGraphicOverridesModel> overrides = new List<CategoryGraphicOverridesModel>();
            Document document = view.Document;
            Categories categories = document.Settings.Categories;

            foreach (Category category in categories)
            {
                if (!category.IsVisibleInUI) continue;

                CategoryGraphicOverridesModel catOverride = category.ToModel(view);
                if (catOverride != null)
                {
                    overrides.Add(catOverride);
                }

                CategoryNameMap subcategories = category.SubCategories;
                foreach (Category subCategory in subcategories)
                {
                    CategoryGraphicOverridesModel subCatOverride = subCategory.ToModel(view);
                    if (subCatOverride != null)
                    {
                        overrides.Add(subCatOverride);
                    }
                }
            }
            return overrides;
        }

        public static List<CategoryGraphicOverridesModel> GetCategoryGraphicOverrides(View view, IIdentityService identityService)
        {
            List<CategoryGraphicOverridesModel> overrides = new List<CategoryGraphicOverridesModel>();
            Document document = view.Document;
            Categories categories = document.Settings.Categories;

            foreach (Category category in categories)
            {
                if (!category.IsVisibleInUI) continue;

                CategoryGraphicOverridesModel catOverride = category.ToModel(view, identityService);
                if (catOverride != null)
                {
                    overrides.Add(catOverride);
                }

                CategoryNameMap subcategories = category.SubCategories;
                foreach (Category subCategory in subcategories)
                {
                    CategoryGraphicOverridesModel subCatOverride = subCategory.ToModel(view, identityService);
                    if (subCatOverride != null)
                    {
                        overrides.Add(subCatOverride);
                    }
                }
            }
            return overrides;
        }
    }
}
