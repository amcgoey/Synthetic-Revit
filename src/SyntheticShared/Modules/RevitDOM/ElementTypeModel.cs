using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Synthetic.Shared.RevitAPI;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Model representation of a Revit ElementType, subclass of ElementModel.
    /// </summary>
    public class ElementTypeModel : ElementModel
    {
        #region Public Properties

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

