using System;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model for a Revit Thermal Physical Asset, inheriting from ObjectModel.
    /// </summary>
    public class ThermalAssetModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the name of the thermal asset.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the thermal material type (e.g. Solid, Liquid, Gas).
        /// </summary>
        public string ThermalMaterialType { get; set; } = "Solid";

        /// <summary>
        /// Gets or sets the density of the thermal asset.
        /// </summary>
        public double Density { get; set; }

        /// <summary>
        /// Gets or sets the thermal conductivity value (W / (m * K)).
        /// </summary>
        public double ThermalConductivity { get; set; }

        /// <summary>
        /// Gets or sets the specific heat value (J / (kg * K)).
        /// </summary>
        public double SpecificHeat { get; set; }

        /// <summary>
        /// Gets or sets the emissivity value (dimensionless).
        /// </summary>
        public double Emissivity { get; set; }

        /// <summary>
        /// Initializes a new instance of the ThermalAssetModel class.
        /// </summary>
        public ThermalAssetModel() { }
    }
}
