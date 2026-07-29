using System;
using Autodesk.Revit.DB;
using RevitView = Autodesk.Revit.DB.View;
using Synthetic.Shared;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Operations
{
    /// <summary>
    /// Stateless utility class for resolving pattern element IDs and modifying Revit View graphics and filters overrides.
    /// </summary>
    public static class GraphicOverrideUtility
    {
        /// <summary>
        /// Applies Category Graphic Overrides to a specific Revit View using the injected IIdentityService.
        /// </summary>
        public static void ModifyOverrideGraphicSettings(CategoryGraphicOverridesModel serialOverride, RevitView view, IIdentityService identityService)
        {
            if (serialOverride == null) return;
            if (view == null) return;

            Document document = view.Document;
            Category? category = serialOverride.Category.GetCategory(document);

            if (category != null)
            {
                if (view.CanCategoryBeHidden(category.Id))
                {
                    try
                    {
                        view.SetCategoryHidden(category.Id, serialOverride.IsHidden);
                    }
                    catch (Exception)
                    {
                        // Ignore if setting hidden fails (e.g. read-only views/categories)
                    }
                }

                if (serialOverride.GraphicOverride != null)
                {
                    try
                    {
                        OverrideGraphicSettings ogs = ToOverrideGraphicSettings(serialOverride.GraphicOverride, document, identityService);
                        view.SetCategoryOverrides(category.Id, ogs);
                    }
                    catch (Exception)
                    {
                        // Ignore if override graphics settings fail to apply
                    }
                }
            }
        }

        /// <summary>
        /// Legacy overload using default RevitIdentityService.
        /// </summary>
        public static void ModifyOverrideGraphicSettings(CategoryGraphicOverridesModel serialOverride, RevitView view)
        {
            ModifyOverrideGraphicSettings(serialOverride, view, new RevitIdentityService());
        }

        /// <summary>
        /// Converts the model representation back to a Revit OverrideGraphicSettings object using the injected IIdentityService.
        /// </summary>
        public static OverrideGraphicSettings ToOverrideGraphicSettings(OverrideGraphicSettingsModel model, Document document, IIdentityService identityService)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (document == null) throw new ArgumentNullException(nameof(document));

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            ogs.SetSurfaceBackgroundPatternVisible(model.IsSurfaceBackgroundPatternVisible);
            if (model.SurfaceBackgroundPatternColor != null)
            {
                ogs.SetSurfaceBackgroundPatternColor(model.SurfaceBackgroundPatternColor.ToColor());
            }
            ogs.SetSurfaceBackgroundPatternId(ResolveFillPatternId(model.SurfaceBackgroundPatternId, document, identityService));

            ogs.SetSurfaceForegroundPatternVisible(model.IsSurfaceForegroundPatternVisible);
            if (model.SurfaceForegroundPatternColor != null)
            {
                ogs.SetSurfaceForegroundPatternColor(model.SurfaceForegroundPatternColor.ToColor());
            }
            ogs.SetSurfaceForegroundPatternId(ResolveFillPatternId(model.SurfaceForegroundPatternId, document, identityService));

            if (model.ProjectionLineColor != null)
            {
                ogs.SetProjectionLineColor(model.ProjectionLineColor.ToColor());
            }
            ogs.SetProjectionLinePatternId(ResolveLinePatternId(model.ProjectionLinePatternId, document, identityService));
            ogs.SetProjectionLineWeight(model.ProjectionLineWeight);

            ogs.SetCutBackgroundPatternVisible(model.IsCutBackgroundPatternVisible);
            if (model.CutBackgroundPatternColor != null)
            {
                ogs.SetCutBackgroundPatternColor(model.CutBackgroundPatternColor.ToColor());
            }
            ogs.SetCutBackgroundPatternId(ResolveFillPatternId(model.CutBackgroundPatternId, document, identityService));

            ogs.SetCutForegroundPatternVisible(model.IsCutForegroundPatternVisible);
            if (model.CutForegroundPatternColor != null)
            {
                ogs.SetCutForegroundPatternColor(model.CutForegroundPatternColor.ToColor());
            }
            ogs.SetCutForegroundPatternId(ResolveFillPatternId(model.CutForegroundPatternId, document, identityService));

            if (model.CutLineColor != null)
            {
                ogs.SetCutLineColor(model.CutLineColor.ToColor());
            }
            ogs.SetCutLinePatternId(ResolveLinePatternId(model.CutLinePatternId, document, identityService));
            ogs.SetCutLineWeight(model.CutLineWeight);

            ogs.SetSurfaceTransparency(model.Transparency);
            ogs.SetHalftone(model.Halftone);
            if (model.DetailLevel != null)
            {
                ogs.SetDetailLevel((ViewDetailLevel)model.DetailLevel.ToEnum());
            }

            return ogs;
        }

        /// <summary>
        /// Legacy overload using default RevitIdentityService.
        /// </summary>
        public static OverrideGraphicSettings ToOverrideGraphicSettings(OverrideGraphicSettingsModel model, Document document)
        {
            return ToOverrideGraphicSettings(model, document, new RevitIdentityService());
        }

        /// <summary>
        /// Converts the ViewFilterOverrideModel back to a Revit OverrideGraphicSettings object using the injected IIdentityService.
        /// </summary>
        public static OverrideGraphicSettings ToOverrideGraphicSettings(ViewFilterOverrideModel model, Document document, IIdentityService identityService)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (document == null) throw new ArgumentNullException(nameof(document));

            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            ogs.SetSurfaceBackgroundPatternVisible(model.IsSurfaceBackgroundPatternVisible);
            if (model.SurfaceBackgroundPatternColor != null)
            {
                ogs.SetSurfaceBackgroundPatternColor(model.SurfaceBackgroundPatternColor.ToColor());
            }
            ogs.SetSurfaceBackgroundPatternId(ResolveFillPatternId(model.SurfaceBackgroundPatternId, document, identityService));

            ogs.SetSurfaceForegroundPatternVisible(model.IsSurfaceForegroundPatternVisible);
            if (model.SurfaceForegroundPatternColor != null)
            {
                ogs.SetSurfaceForegroundPatternColor(model.SurfaceForegroundPatternColor.ToColor());
            }
            ogs.SetSurfaceForegroundPatternId(ResolveFillPatternId(model.SurfaceForegroundPatternId, document, identityService));

            if (model.ProjectionLineColor != null)
            {
                ogs.SetProjectionLineColor(model.ProjectionLineColor.ToColor());
            }
            ogs.SetProjectionLinePatternId(ResolveLinePatternId(model.ProjectionLinePatternId, document, identityService));
            ogs.SetProjectionLineWeight(model.ProjectionLineWeight);

            ogs.SetCutBackgroundPatternVisible(model.IsCutBackgroundPatternVisible);
            if (model.CutBackgroundPatternColor != null)
            {
                ogs.SetCutBackgroundPatternColor(model.CutBackgroundPatternColor.ToColor());
            }
            ogs.SetCutBackgroundPatternId(ResolveFillPatternId(model.CutBackgroundPatternId, document, identityService));

            ogs.SetCutForegroundPatternVisible(model.IsCutForegroundPatternVisible);
            if (model.CutForegroundPatternColor != null)
            {
                ogs.SetCutForegroundPatternColor(model.CutForegroundPatternColor.ToColor());
            }
            ogs.SetCutForegroundPatternId(ResolveFillPatternId(model.CutForegroundPatternId, document, identityService));

            if (model.CutLineColor != null)
            {
                ogs.SetCutLineColor(model.CutLineColor.ToColor());
            }
            ogs.SetCutLinePatternId(ResolveLinePatternId(model.CutLinePatternId, document, identityService));
            ogs.SetCutLineWeight(model.CutLineWeight);

            ogs.SetSurfaceTransparency(model.Transparency);
            ogs.SetHalftone(model.Halftone);
            if (model.DetailLevel != null)
            {
                ogs.SetDetailLevel((ViewDetailLevel)model.DetailLevel.ToEnum());
            }

            return ogs;
        }

        /// <summary>
        /// Legacy overload using default RevitIdentityService.
        /// </summary>
        public static OverrideGraphicSettings ToOverrideGraphicSettings(ViewFilterOverrideModel model, Document document)
        {
            return ToOverrideGraphicSettings(model, document, new RevitIdentityService());
        }

        /// <summary>
        /// Resolves a FillPatternElement ID from an ElementIdModel using the injected IIdentityService.
        /// </summary>
        public static ElementId ResolveFillPatternId(ElementIdModel? patternModel, Document? document, IIdentityService identityService)
        {
            if (patternModel == null) return ElementId.InvalidElementId;
            if (patternModel.Id == -1 || patternModel.Id == 0) return ElementId.InvalidElementId;
            if (document != null)
            {
                var elem = identityService.ResolveElement(patternModel, document);
                if (elem is FillPatternElement fpe)
                {
                    return fpe.Id;
                }
            }
            return patternModel.ToElementId();
        }

        /// <summary>
        /// Legacy overload.
        /// </summary>
        public static ElementId ResolveFillPatternId(ElementIdModel? patternModel, Document? document)
        {
            return ResolveFillPatternId(patternModel, document, new RevitIdentityService());
        }

        /// <summary>
        /// Resolves a LinePatternElement ID from an ElementIdModel using the injected IIdentityService, supporting built-in Solid pattern.
        /// </summary>
        public static ElementId ResolveLinePatternId(ElementIdModel? patternModel, Document? document, IIdentityService identityService)
        {
            if (patternModel == null) return ElementId.InvalidElementId;
            long solidIdVal;
#if REVIT2022 || REVIT2023
            solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
            solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
            if (patternModel.Id == solidIdVal || string.Equals(patternModel.Name, "Solid", StringComparison.OrdinalIgnoreCase))
            {
                return LinePatternElement.GetSolidPatternId();
            }
            if (document != null)
            {
                var elem = identityService.ResolveElement(patternModel, document);
                if (elem is LinePatternElement lpe)
                {
                    return lpe.Id;
                }
            }
            return patternModel.ToElementId();
        }

        /// <summary>
        /// Legacy overload.
        /// </summary>
        public static ElementId ResolveLinePatternId(ElementIdModel? patternModel, Document? document)
        {
            return ResolveLinePatternId(patternModel, document, new RevitIdentityService());
        }
    }
}
