using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Serialize and deserialize Revit DimensionTypes
    /// </summary>
    public class DimensionTypeModel : ElementTypeModel
    {
        #region Public Properties
        /// <summary>
        /// Revit DimensionStyle Type Enum
        /// </summary>
        public string DimensionStyle { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the underlying Revit DimensionType object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? DimensionType { get; set; }

        /// <summary>
        /// Gets or sets the base Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.DimensionType;
            set => this.DimensionType = value;
        }


        /// <summary>
        /// Constant for the hidden built in linear dimension style.
        /// These styles shouldn't be serialized or used as templates to create new styles.
        /// </summary>
        [JsonIgnoreAttribute]
        
        public const string InternalDimStyleName = "Linear Dimension Style";

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the DimensionTypeModel class.
        /// </summary>
        public DimensionTypeModel() : base()
        {
            DimensionStyle = "Linear";
        }

        #endregion

    }
}

