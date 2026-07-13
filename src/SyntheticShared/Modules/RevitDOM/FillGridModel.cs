using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json;

using Synthetic.Infrastructure.Serialization;

using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.RevitDOM
{
    
    /// <summary>
    /// Represents a model for a Revit FillGrid.
    /// </summary>
    public class FillGridModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the angle of the fill grid lines.
        /// </summary>
        public Double Angle { get; set; }

        /// <summary>
        /// Gets or sets the offset of the fill grid lines.
        /// </summary>
        public Double Offset { get; set; }

        /// <summary>
        /// Gets or sets the origin point of the fill grid.
        /// </summary>
        public UVModel? Origin { get; set; }

        /// <summary>
        /// Gets or sets the shift of the fill grid lines.
        /// </summary>
        public Double Shift { get; set; }

        /// <summary>
        /// Initializes a new instance of the FillGridModel class.
        /// </summary>
        public FillGridModel() { }

        /// <summary>
        /// Initializes a new instance of the FillGridModel class with specified angle, offset, origin, and shift.
        /// </summary>
        /// <param name="Angle">The angle of the lines.</param>
        /// <param name="Offset">The offset between lines.</param>
        /// <param name="Origin">The origin point.</param>
        /// <param name="Shift">The shift along the lines.</param>
        public FillGridModel (Double Angle, Double Offset, UVModel Origin, Double Shift)
        {
            this.Angle = Angle;
            this.Offset = Offset;
            this.Origin = Origin;
            this.Shift = Shift;
        }

        /// <summary>
        /// Deserializes a FillGridModel from a JSON string.
        /// </summary>
        /// <param name="JSON">The JSON representation.</param>
        /// <returns>A FillGridModel instance.</returns>
        public static FillGridModel? ByJSON (string JSON)
        {
            return JsonConvert.DeserializeObject<FillGridModel>(JSON);
        }

        /// <summary>
        /// Serializes a FillGridModel to a JSON string.
        /// </summary>
        /// <param name="color">The FillGridModel to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON (FillGridModel color)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(color, Formatting.Indented);
        }
    }
}

