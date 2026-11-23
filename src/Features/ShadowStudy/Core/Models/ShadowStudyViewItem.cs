using System.Collections.Generic;

namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Pure DTO representing an eligible shadow-capable Revit graphical view.
    /// </summary>
    public class ShadowStudyViewItem
    {
        /// <summary>
        /// Gets or sets the serialized Revit element UniqueId.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the raw Revit view name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Revit ViewType name (for example, FloorPlan, ThreeD, Section, Elevation).
        /// </summary>
        public string ViewType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sanitized output label used in composite filenames.
        /// </summary>
        public string ViewLabel { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether CropBoxActive is enabled on the view.
        /// </summary>
        public bool IsCropped { get; set; }

        /// <summary>
        /// Gets or sets the active DisplayStyle name on the view.
        /// </summary>
        public string DisplayStyleName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the view supports ViewDisplayBackground (ThreeD, Section, Elevation).
        /// </summary>
        public bool IsBackgroundSupported { get; set; }

        /// <summary>
        /// Gets or sets non-blocking view warning badges (e.g. Uncropped or Wireframe/Rendering).
        /// </summary>
        public List<ShadowStudyViewWarningBadge> Warnings { get; set; } = new List<ShadowStudyViewWarningBadge>();

        /// <summary>
        /// Gets a value indicating whether the view has any warning badges.
        /// </summary>
        public bool HasWarnings => Warnings != null && Warnings.Count > 0;
    }
}
