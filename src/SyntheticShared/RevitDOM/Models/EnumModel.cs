using System;
using System.Collections.Generic;
using System.Linq;

using RevitDB = Autodesk.Revit.DB;
using RevitDoc = Autodesk.Revit.DB.Document;

using SynthEnum = Synthetic.Shared.EnumUtil;

using Newtonsoft.Json;

using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    
    /// <summary>
    /// Represents a serialized Enum value with its type information.
    /// </summary>
    public class EnumModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the full name of the Enum type.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the string value of the Enum.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the EnumModel class.
        /// </summary>
        public EnumModel () { }

        /// <summary>
        /// Initializes a new instance of the EnumModel class using the specified type and value.
        /// </summary>
        /// <param name="type">The Type of the Enum.</param>
        /// <param name="value">The Enum value.</param>
        public EnumModel (Type type, Enum value)
        {
            this.Type = type.FullName ?? string.Empty;
            this.Value = value.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the EnumModel class using the type name and value string.
        /// </summary>
        /// <param name="type">The full name of the Enum type.</param>
        /// <param name="value">The Enum value as a string.</param>
        public EnumModel (string type, string value)
        {
            this.Type = type;
            this.Value = value;
        }
    }
}
