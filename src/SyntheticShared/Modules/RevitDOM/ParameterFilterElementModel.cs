using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model representation of a Revit ParameterFilterElement (View Filter), inheriting from ElementModel.
    /// Deeply serializes categories and filter rules.
    /// </summary>
    public class ParameterFilterElementModel : ElementModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the list of category models associated with this filter.
        /// </summary>
        public List<CategoryIdModel> Categories { get; set; } = new List<CategoryIdModel>();

        /// <summary>
        /// Gets or sets the recursive root rule model representing the logic tree of the filter.
        /// </summary>
        public FilterRuleModel? RootRule { get; set; }

        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the ParameterFilterElementModel class.
        /// </summary>
        public ParameterFilterElementModel() { }

        #endregion
    }
}

