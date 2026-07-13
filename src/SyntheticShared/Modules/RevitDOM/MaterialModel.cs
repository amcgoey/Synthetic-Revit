using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model for a Revit Material, inheriting from ElementModel.
    /// </summary>
    public class MaterialModel : ElementModel
    {
        /// <summary>
        /// The class name identifier for materials.
        /// </summary>
        public const string ClassName = "Material";

        #region Public Properties

        /// <summary>
        /// Gets or sets the appearance asset element ID model.
        /// </summary>
        public ElementIdModel? AppearanceAssetId { get; set; }

        /// <summary>
        /// Gets or sets the nested appearance asset model.
        /// </summary>
        public AppearanceAssetModel? AppearanceAsset { get; set; }

        /// <summary>
        /// Gets or sets the nested structural physical asset model.
        /// </summary>
        public StructuralAssetModel? StructuralAsset { get; set; }

        /// <summary>
        /// Gets or sets the nested thermal physical asset model.
        /// </summary>
        public ThermalAssetModel? ThermalAsset { get; set; }

        /// <summary>
        /// Gets or sets the shading color of the material.
        /// </summary>
        public ColorModel? Color { get; set; }

        /// <summary>
        /// Gets or sets the cut foreground pattern color.
        /// </summary>
        public ColorModel? CutForegroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the cut foreground pattern element ID model.
        /// </summary>
        public ElementIdModel? CutForegroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the cut background pattern color.
        /// </summary>
        public ColorModel? CutBackgroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the cut background pattern element ID model.
        /// </summary>
        public ElementIdModel? CutBackgroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the surface foreground pattern color.
        /// </summary>
        public ColorModel? SurfaceForegroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the surface foreground pattern element ID model.
        /// </summary>
        public ElementIdModel? SurfaceForegroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the surface background pattern color.
        /// </summary>
        public ColorModel? SurfaceBackgroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the surface background pattern element ID model.
        /// </summary>
        public ElementIdModel? SurfaceBackgroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the Revit Material object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? Material { get; set; }

        /// <summary>
        /// Gets or sets the underlying Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.Material;
            set => this.Material = value;
        }
        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the MaterialModel class.
        /// </summary>
        public MaterialModel () : base () { }

        #endregion

        #region Public Methods
        
        /// <summary>
        /// Deserializes a MaterialModel from a JSON string.
        /// </summary>
        /// <param name="JSON">The JSON representation.</param>
        /// <returns>A MaterialModel instance.</returns>
        public new static MaterialModel? ByJSON(string JSON)
        {
            return JsonConvert.DeserializeObject<MaterialModel>(JSON);
        }

        /// <summary>
        /// Serializes a MaterialModel to a JSON string.
        /// </summary>
        /// <param name="materialJSON">The MaterialModel to serialize.</param>
        /// <returns>A JSON string representation.</returns>
        public static string ToJSON(MaterialModel materialJSON)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(materialJSON, Formatting.Indented);
        }
        #endregion
    }
}

