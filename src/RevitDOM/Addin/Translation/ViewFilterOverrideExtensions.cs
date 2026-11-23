using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;

namespace Synthetic.RevitDOM.Translation
{
    public static class ViewFilterOverrideExtensions
    {
        public static ViewFilterOverrideModel ToModel(this ParameterFilterElement filter, OverrideGraphicSettings ogs, Document doc)
        {
            if (filter == null || ogs == null) return null!;
            var model = new ViewFilterOverrideModel();
            model.FilterId = filter.Id.ToModel(doc, false);
            model.IsVisible = true; // default

            model.Halftone = ogs.Halftone;
            model.Transparency = ogs.Transparency;
            model.DetailLevel = new EnumModel(typeof(ViewDetailLevel), ogs.DetailLevel);

            model.ProjectionLineColor = ogs.ProjectionLineColor.ToModel();
            ElementId projectionLinePatternId = ogs.ProjectionLinePatternId;
            if (projectionLinePatternId == ElementId.InvalidElementId || projectionLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.ProjectionLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.ProjectionLinePatternId = projectionLinePatternId.ToModel(doc, false);
            }
            model.ProjectionLineWeight = ogs.ProjectionLineWeight;

            model.IsSurfaceBackgroundPatternVisible = ogs.IsSurfaceBackgroundPatternVisible;
            model.SurfaceBackgroundPatternColor = ogs.SurfaceBackgroundPatternColor.ToModel();
            model.SurfaceBackgroundPatternId = ogs.SurfaceBackgroundPatternId.ToModel(doc, false);

            model.IsSurfaceForegroundPatternVisible = ogs.IsSurfaceForegroundPatternVisible;
            model.SurfaceForegroundPatternColor = ogs.SurfaceForegroundPatternColor.ToModel();
            model.SurfaceForegroundPatternId = ogs.SurfaceForegroundPatternId.ToModel(doc, false);

            model.IsCutBackgroundPatternVisible = ogs.IsCutBackgroundPatternVisible;
            model.CutBackgroundPatternColor = ogs.CutBackgroundPatternColor.ToModel();
            model.CutBackgroundPatternId = ogs.CutBackgroundPatternId.ToModel(doc, false);

            model.IsCutForegroundPatternVisible = ogs.IsCutForegroundPatternVisible;
            model.CutForegroundPatternColor = ogs.CutForegroundPatternColor.ToModel();
            model.CutForegroundPatternId = ogs.CutForegroundPatternId.ToModel(doc, false);

            model.CutLineColor = ogs.CutLineColor.ToModel();
            ElementId cutLinePatternId = ogs.CutLinePatternId;
            if (cutLinePatternId == ElementId.InvalidElementId || cutLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.CutLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.CutLinePatternId = cutLinePatternId.ToModel(doc, false);
            }
            model.CutLineWeight = ogs.CutLineWeight;

            return model;
        }

        public static ViewFilterOverrideModel ToModel(this ParameterFilterElement filter, OverrideGraphicSettings ogs, Document doc, IIdentityService identityService)
        {
            if (filter == null || ogs == null) return null!;
            var model = new ViewFilterOverrideModel();
            model.FilterId = identityService.ToModel(filter.Id, doc, false);
            model.IsVisible = true; // default

            model.Halftone = ogs.Halftone;
            model.Transparency = ogs.Transparency;
            model.DetailLevel = new EnumModel(typeof(ViewDetailLevel), ogs.DetailLevel);

            model.ProjectionLineColor = ogs.ProjectionLineColor.ToModel();
            ElementId projectionLinePatternId = ogs.ProjectionLinePatternId;
            if (projectionLinePatternId == ElementId.InvalidElementId || projectionLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.ProjectionLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.ProjectionLinePatternId = identityService.ToModel(projectionLinePatternId, doc, false);
            }
            model.ProjectionLineWeight = ogs.ProjectionLineWeight;

            model.IsSurfaceBackgroundPatternVisible = ogs.IsSurfaceBackgroundPatternVisible;
            model.SurfaceBackgroundPatternColor = ogs.SurfaceBackgroundPatternColor.ToModel();
            model.SurfaceBackgroundPatternId = identityService.ToModel(ogs.SurfaceBackgroundPatternId, doc, false);

            model.IsSurfaceForegroundPatternVisible = ogs.IsSurfaceForegroundPatternVisible;
            model.SurfaceForegroundPatternColor = ogs.SurfaceForegroundPatternColor.ToModel();
            model.SurfaceForegroundPatternId = identityService.ToModel(ogs.SurfaceForegroundPatternId, doc, false);

            model.IsCutBackgroundPatternVisible = ogs.IsCutBackgroundPatternVisible;
            model.CutBackgroundPatternColor = ogs.CutBackgroundPatternColor.ToModel();
            model.CutBackgroundPatternId = identityService.ToModel(ogs.CutBackgroundPatternId, doc, false);

            model.IsCutForegroundPatternVisible = ogs.IsCutForegroundPatternVisible;
            model.CutForegroundPatternColor = ogs.CutForegroundPatternColor.ToModel();
            model.CutForegroundPatternId = identityService.ToModel(ogs.CutForegroundPatternId, doc, false);

            model.CutLineColor = ogs.CutLineColor.ToModel();
            ElementId cutLinePatternId = ogs.CutLinePatternId;
            if (cutLinePatternId == ElementId.InvalidElementId || cutLinePatternId == LinePatternElement.GetSolidPatternId())
            {
                long solidIdVal;
#if REVIT2023
                solidIdVal = LinePatternElement.GetSolidPatternId().IntegerValue;
#else
                solidIdVal = LinePatternElement.GetSolidPatternId().Value;
#endif
                model.CutLinePatternId = new ElementIdModel { Id = solidIdVal, Name = "Solid", Class = "Autodesk.Revit.DB.LinePatternElement", Category = "" };
            }
            else
            {
                model.CutLinePatternId = identityService.ToModel(cutLinePatternId, doc, false);
            }
            model.CutLineWeight = ogs.CutLineWeight;

            return model;
        }
    }
}
