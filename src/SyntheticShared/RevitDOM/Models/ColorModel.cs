using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json;

using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    
    /// <summary>
    /// Model representing a Revit color value (Red, Green, Blue components).
    /// </summary>
    public class ColorModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the Blue component value of the color (0-255).
        /// </summary>
        public Byte Blue { get; set; }

        /// <summary>
        /// Gets or sets the Green component value of the color (0-255).
        /// </summary>
        public Byte Green { get; set; }

        /// <summary>
        /// Gets or sets the Red component value of the color (0-255).
        /// </summary>
        public Byte Red { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the color is valid.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Initializes a new instance of the ColorModel class as an invalid color.
        /// </summary>
        public ColorModel()
        {
            this.IsValid = false;
        }

        /// <summary>
        /// Initializes a new instance of the ColorModel class with specific Red, Green, and Blue component values.
        /// </summary>
        /// <param name="Red">Red component (0-255).</param>
        /// <param name="Green">Green component (0-255).</param>
        /// <param name="Blue">Blue component (0-255).</param>
        public ColorModel (Byte Red, Byte Green, Byte Blue)
        {
            this.Red = Red;
            this.Green = Green;
            this.Blue = Blue;
            this.IsValid = true;
        }

        /// <summary>
        /// Deserializes a JSON string into a ColorModel instance.
        /// </summary>
        /// <param name="JSON">The JSON string.</param>
        /// <returns>A ColorModel instance.</returns>
        public static ColorModel? ByJSON (string JSON)
        {
            return JsonConvert.DeserializeObject<ColorModel>(JSON);
        }

        /// <summary>
        /// Serializes a ColorModel instance to a JSON string.
        /// </summary>
        /// <param name="color">The color model to serialize.</param>
        /// <returns>A JSON string.</returns>
        public static string ToJSON (ColorModel color)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(color, Formatting.Indented);
        }
    }

}

