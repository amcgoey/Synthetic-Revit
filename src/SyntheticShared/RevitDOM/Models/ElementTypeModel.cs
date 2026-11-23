using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Synthetic.Platform.Revit;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Model representation of a Revit ElementType, subclass of ElementModel.
    /// </summary>
    public class ElementTypeModel : ElementModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the family name of the element type.
        /// </summary>
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the ViewFamily enum name for ViewFamilyType elements. Null for all other element types.
        /// </summary>
        public string? ViewFamily { get; set; }

        /// <summary>
        /// Determines whether the ViewFamily property should be serialized to JSON (omitted when null).
        /// </summary>
        public bool ShouldSerializeViewFamily() => ViewFamily != null;

        /// <summary>
        /// Gets or sets the underlying Revit ElementType object. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? ElementType { get; set; }
       

        /// <summary>
        /// Gets or sets the base Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.ElementType;
            set => this.ElementType = value;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ElementTypeModel class.
        /// </summary>
        public ElementTypeModel () : base () { }

        #endregion

    }
}

