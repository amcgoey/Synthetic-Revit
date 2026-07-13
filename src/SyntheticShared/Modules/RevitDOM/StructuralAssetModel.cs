using System;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model for a Revit Structural Physical Asset (e.g. concrete/metal structural properties), inheriting from ObjectModel.
    /// </summary>
    public class StructuralAssetModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the structural asset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the physical behavior mode (e.g. Isotropic, Orthotropic).
        /// </summary>
        public string Behavior { get; set; } = "Isotropic";

        /// <summary>
        /// Gets or sets the material structural asset class (e.g. Metal, Concrete).
        /// </summary>
        public string StructuralAssetClass { get; set; } = "Generic";

        /// <summary>
        /// Gets or sets the density of the material.
        /// </summary>
        public double Density { get; set; }

        /// <summary>
        /// Gets or sets the Young's Modulus value (pa).
        /// </summary>
        public double YoungModulus { get; set; }

        /// <summary>
        /// Gets or sets the Poisson's Ratio value.
        /// </summary>
        public double PoissonRatio { get; set; }

        /// <summary>
        /// Initializes a new instance of the StructuralAssetModel class.
        /// </summary>
        public StructuralAssetModel() { }
    }
}
