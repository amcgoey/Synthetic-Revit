using System;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model for a Revit Appearance Asset (rendering data), inheriting from ObjectModel.
    /// </summary>
    public class AppearanceAssetModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the appearance asset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the color of the asset.
        /// </summary>
        public ColorModel? Color { get; set; }

        /// <summary>
        /// Gets or sets the transparency value (range 0.0 to 1.0).
        /// </summary>
        public double Transparency { get; set; }

        /// <summary>
        /// Gets or sets the smoothness/glossiness value (range 0.0 to 1.0).
        /// </summary>
        public double Smoothness { get; set; }

        /// <summary>
        /// Initializes a new instance of the AppearanceAssetModel class.
        /// </summary>
        public AppearanceAssetModel() { }
    }
}
