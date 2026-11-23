using System.Collections.Generic;

namespace Synthetic.RevitDOM.Models
{
    /// <summary>
    /// Captures output telemetry and metrics for element reference redirection operations performed by <see cref="Operations.AliasSwapEngine"/>.
    /// </summary>
    public class RedirectionResultModel
    {
        /// <summary>
        /// Gets or sets the count of unique element instances modified during reference redirection.
        /// </summary>
        public int InstancesCount { get; set; }

        /// <summary>
        /// Gets or sets the count of writeable ElementId parameters swapped.
        /// </summary>
        public int ParametersCount { get; set; }

        /// <summary>
        /// Gets or sets the count of category default style references (Material, Line patterns) swapped.
        /// </summary>
        public int CategoryStylesCount { get; set; }

        /// <summary>
        /// Gets or sets the count of compound structure layer references (MaterialId, DeckProfileId) swapped.
        /// </summary>
        public int CompoundStructureLayersCount { get; set; }

        /// <summary>
        /// Gets or sets the count of view category graphic override pattern references swapped.
        /// </summary>
        public int ViewGraphicOverridesCount { get; set; }

        /// <summary>
        /// Gets or sets the list of warning messages logged during redirection.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the list of error messages logged during redirection.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Gets the total count of element references swapped across all categories.
        /// </summary>
        public int TotalSwappedCount => ParametersCount + CategoryStylesCount + CompoundStructureLayersCount + ViewGraphicOverridesCount;
    }
}
