using System;
using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
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
        public CategoryGraphicOverridesModel() { }

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
}
