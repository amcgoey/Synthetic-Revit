using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model for Revit OverrideGraphicSettings, mapping visibility and style overrides.
    /// </summary>
    public class OverrideGraphicSettingsModel : ObjectModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets a value indicating whether any settings have been modified. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public bool IsModified { get; set; }

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
        /// Initializes a new instance of the OverrideGraphicSettingsModel class.
        /// </summary>
        public OverrideGraphicSettingsModel () { }

        #endregion

    }
}

