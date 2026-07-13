using System;
using Newtonsoft.Json;

namespace Synthetic.Modules.RevitDOM
{
    /// <summary>
    /// Represents a model for Revit FilledRegionType, inheriting from ElementTypeModel.
    /// </summary>
    public class FilledRegionTypeModel : ElementTypeModel
    {
        #region Public Properties

        /// <summary>
        /// Gets or sets the Revit FilledRegionType associated with this model. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public object? FilledRegionType { get; set; }

        /// <summary>
        /// Gets or sets the underlying Revit Element. Ignored in JSON.
        /// </summary>
        [JsonIgnoreAttribute]
        public override object? Element
        {
            get => this.FilledRegionType;
            set => this.FilledRegionType = value;
        }

        /// <summary>
        /// Gets or sets the background pattern color.
        /// </summary>
        public ColorModel? BackgroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the background pattern element ID.
        /// </summary>
        public ElementIdModel? BackgroundPatternId { get; set; }

        /// <summary>
        /// Gets or sets the foreground pattern color.
        /// </summary>
        public ColorModel? ForegroundPatternColor { get; set; }

        /// <summary>
        /// Gets or sets the foreground pattern element ID.
        /// </summary>
        public ElementIdModel? ForegroundPatternId { get; set; }

        #endregion

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the FilledRegionTypeModel class.
        /// </summary>
        public FilledRegionTypeModel () : base () { }

        #endregion
    }
}
