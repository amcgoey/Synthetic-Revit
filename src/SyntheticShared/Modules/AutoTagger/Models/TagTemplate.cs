using Synthetic.Modules.AutoTagger.Commands;
using Synthetic.Modules.AutoTagger.Models;
using Synthetic.Modules.AutoTagger.ViewModels;
using Synthetic.Modules.AutoTagger.Views;
using Synthetic.Modules.AutoTagger.Repositories;

using System;
using Autodesk.Revit.DB;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.AutoTagger.Models
{
    /// <summary>
    /// Data Transfer Object (DTO) representing a user-defined tag offset template.
    /// </summary>
    public class TagTemplate
    {
        /// <summary>
        /// Gets or sets the unique identifier of the tag template.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Gets or sets the name of the template.
        /// </summary>
        public string TemplateName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target category name.
        /// </summary>
        public string TargetCategory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target family name.
        /// </summary>
        public string? TargetFamily { get; set; }

        /// <summary>
        /// Gets or sets the target type name.
        /// </summary>
        public string? TargetType { get; set; }
        
        /// <summary>
        /// Gets or sets the relative X offset in decimal feet.
        /// </summary>
        public double OffsetX { get; set; }

        /// <summary>
        /// Gets or sets the relative Y offset in decimal feet.
        /// </summary>
        public double OffsetY { get; set; }

        /// <summary>
        /// Gets or sets the relative Z offset in decimal feet.
        /// </summary>
        public double OffsetZ { get; set; }

        /// <summary>
        /// Gets or sets the orientation of the tag.
        /// </summary>
        public TagOrientation Orientation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether orientation change is allowed.
        /// </summary>
        public bool AllowOrientationChange { get; set; }

        /// <summary>
        /// Gets or sets the X component of the host's hand orientation vector.
        /// </summary>
        public double HostHandX { get; set; }

        /// <summary>
        /// Gets or sets the Y component of the host's hand orientation vector.
        /// </summary>
        public double HostHandY { get; set; }

        /// <summary>
        /// Gets or sets the Z component of the host's hand orientation vector.
        /// </summary>
        public double HostHandZ { get; set; }
    }
}
