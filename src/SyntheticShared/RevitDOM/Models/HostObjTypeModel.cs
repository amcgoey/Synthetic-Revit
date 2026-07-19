using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model for a Revit HostObjAttributes (e.g. WallType), inheriting from ElementTypeModel.
    /// </summary>
    public class HostObjTypeModel : ElementTypeModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the functional classification of the host object type.
        /// </summary>
        public EnumModel? Function { get; set; }

        /// <summary>
        /// Gets or sets the compound structure of the host object type.
        /// </summary>
        public CompoundStructureModel? Structure { get; set; }

        /// <summary>
        /// Gets or sets the Revit WallType / HostObjAttributes associated with this model. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? WallType { get; set; }

        /// <summary>
        /// Gets or sets the underlying Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.WallType;
            set => this.WallType = value;
        }

        #endregion
        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the HostObjTypeModel class.
        /// </summary>
        public HostObjTypeModel () : base () { }

        #endregion
    }
}

