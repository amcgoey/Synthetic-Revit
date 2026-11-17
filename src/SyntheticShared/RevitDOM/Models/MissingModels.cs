using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Synthetic.Modules.MergeDuplicates.Handlers;
using Synthetic.Modules.StandardsManagement.ViewModels;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a model for a Revit FillPatternElement, inheriting from ElementModel.
    /// </summary>
    public class FillPatternElementModel : ElementModel
    {
        /// <summary>
        /// Gets or sets the fill pattern model settings.
        /// </summary>
        public FillPatternModel? Pattern { get; set; }

        /// <summary>
        /// Initializes a new instance of the FillPatternElementModel class.
        /// </summary>
        public FillPatternElementModel() : base() { }
    }

    /// <summary>
    /// Represents a model segment for a Revit LinePattern.
    /// </summary>
    public class LinePatternSegmentModel
    {
        /// <summary>
        /// Gets or sets the segment type as a string (e.g. Dash, Space, Dot).
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the length of the segment.
        /// </summary>
        public double Length { get; set; }
    }

    /// <summary>
    /// Represents a model for a Revit LinePatternElement, inheriting from ElementModel.
    /// </summary>
    public class LinePatternElementModel : ElementModel
    {
        /// <summary>
        /// Gets or sets the list of segments defining the line pattern.
        /// </summary>
        public List<LinePatternSegmentModel> Segments { get; set; } = new List<LinePatternSegmentModel>();

        /// <summary>
        /// Initializes a new instance of the LinePatternElementModel class.
        /// </summary>
        public LinePatternElementModel() : base() { }
    }

    /// <summary>
    /// Represents a model for a Revit PropertySetElement (Material Asset), inheriting from ElementModel.
    /// </summary>
    public class PropertySetElementModel : ElementModel
    {
        /// <summary>
        /// Initializes a new instance of the PropertySetElementModel class.
        /// </summary>
        public PropertySetElementModel() : base() { }
    }

    /// <summary>
    /// Represents a model for Revit BrowserOrganization, inheriting from ElementModel.
    /// </summary>
    public class BrowserOrganizationModel : ElementModel
    {
        /// <summary>
        /// Initializes a new instance of the BrowserOrganizationModel class.
        /// </summary>
        public BrowserOrganizationModel() : base() { }
    }

    /// <summary>
    /// Represents a model for a Revit ParameterElement, inheriting from ElementModel.
    /// </summary>
    public class ParameterElementModel : ElementModel
    {
        /// <summary>
        /// Gets or sets the categories this parameter is bound to.
        /// </summary>
        public List<CategoryIdModel> Categories { get; set; } = new List<CategoryIdModel>();

        /// <summary>
        /// Gets or sets a value indicating whether the parameter is bound per-instance (true) or per-type (false).
        /// </summary>
        public bool IsInstanceBinding { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether an instance parameter's values can vary across group instances.
        /// </summary>
        public bool IsVaryByGroup { get; set; }

        /// <summary>
        /// Initializes a new instance of the ParameterElementModel class.
        /// </summary>
        public ParameterElementModel() : base() { }
    }

}


