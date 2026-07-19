using System;
using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Represents a lightweight data structure mapping PlanViewRange offsets and level references.
    /// </summary>
    public class PlanViewRangeModel : ObjectModel
    {
        /// <summary>
        /// Gets or sets the top offset value of the view range.
        /// </summary>
        public double TopOffset { get; set; }

        /// <summary>
        /// Gets or sets the top level ID reference.
        /// </summary>
        public ElementIdModel? TopLevelId { get; set; }

        /// <summary>
        /// Gets or sets the cut plane offset value.
        /// </summary>
        public double CutPlaneOffset { get; set; }

        /// <summary>
        /// Gets or sets the cut plane level ID reference.
        /// </summary>
        public ElementIdModel? CutPlaneLevelId { get; set; }

        /// <summary>
        /// Gets or sets the bottom offset value.
        /// </summary>
        public double BottomOffset { get; set; }

        /// <summary>
        /// Gets or sets the bottom level ID reference.
        /// </summary>
        public ElementIdModel? BottomLevelId { get; set; }

        /// <summary>
        /// Gets or sets the view depth offset value.
        /// </summary>
        public double ViewDepthOffset { get; set; }

        /// <summary>
        /// Gets or sets the view depth level ID reference.
        /// </summary>
        public ElementIdModel? ViewDepthLevelId { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlanViewRangeModel"/> class.
        /// </summary>
        public PlanViewRangeModel()
        {
        }
    }
}

