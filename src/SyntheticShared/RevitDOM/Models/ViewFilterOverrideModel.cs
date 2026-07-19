using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model for Revit View Filter overrides, wrapping filter identity, visibility, and style overrides.
    /// </summary>
    public class ViewFilterOverrideModel : ObjectModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the ElementIdModel representing the Revit Filter element.
        /// </summary>
        public ElementIdModel? FilterId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the filter is visible in the view.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether surface background pattern is visible.
        /// </summary>
        public bool IsSurfaceBackgroundPatternVisible { get; set; }

        /// <summary>
        /// Gets or sets the surface background pattern color.
        /// </summary>
        public ColorModel? SurfaceBackgroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the surface background pattern element ID model.
        /// </summary>
        public ElementIdModel? SurfaceBackgroundPatternId { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether surface foreground pattern is visible.
        /// </summary>
        public bool IsSurfaceForegroundPatternVisible { get; set; }

        /// <summary>
        /// Gets or sets the surface foreground pattern color.
        /// </summary>
        public ColorModel? SurfaceForegroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the surface foreground pattern element ID model.
        /// </summary>
        public ElementIdModel? SurfaceForegroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the projection line color.
        /// </summary>
        public ColorModel? ProjectionLineColor { get; set; }

        /// <summary>
        /// Gets or sets the projection line pattern element ID model.
        /// </summary>
        public ElementIdModel? ProjectionLinePatternId { get; set; }

        /// <summary>
        /// Gets or sets the projection line weight.
        /// </summary>
        public int ProjectionLineWeight { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether cut background pattern is visible.
        /// </summary>
        public bool IsCutBackgroundPatternVisible { get; set; }

        /// <summary>
        /// Gets or sets the cut background pattern color.
        /// </summary>
        public ColorModel? CutBackgroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the cut background pattern element ID model.
        /// </summary>
        public ElementIdModel? CutBackgroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether cut foreground pattern is visible.
        /// </summary>
        public bool IsCutForegroundPatternVisible { get; set; }

        /// <summary>
        /// Gets or sets the cut foreground pattern color.
        /// </summary>
        public ColorModel? CutForegroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the cut foreground pattern element ID model.
        /// </summary>
        public ElementIdModel? CutForegroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the cut line color.
        /// </summary>
        public ColorModel? CutLineColor { get; set; }

        /// <summary>
        /// Gets or sets the cut line pattern element ID model.
        /// </summary>
        public ElementIdModel? CutLinePatternId { get; set; }

        /// <summary>
        /// Gets or sets the cut line weight.
        /// </summary>
        public int CutLineWeight { get; set; }

        /// <summary>
        /// Gets or sets the transparency percentage (0-100).
        /// </summary>
        public int Transparency { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether halftone is enabled.
        /// </summary>
        public bool Halftone { get; set; }

        /// <summary>
        /// Gets or sets the view detail level override.
        /// </summary>
        public EnumModel? DetailLevel { get; set; }

        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ViewFilterOverrideModel class.
        /// </summary>
        public ViewFilterOverrideModel () { }

        #endregion
    }

    public static class ViewFilterOverrideExtensions
    {
        public static ViewFilterOverrideModel ToModel(this ParameterFilterElement filter, OverrideGraphicSettings ogs, Document doc)
        {
            if (filter == null || ogs == null) return null;
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
#if REVIT2022 || REVIT2023
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
#if REVIT2022 || REVIT2023
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
            if (filter == null || ogs == null) return null;
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
#if REVIT2022 || REVIT2023
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
#if REVIT2022 || REVIT2023
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

