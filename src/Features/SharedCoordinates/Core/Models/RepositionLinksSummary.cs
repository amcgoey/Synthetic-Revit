using System.Collections.Generic;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Reports the outcome of a Shared Site Link Repositioning execution.
    /// </summary>
    public class RepositionLinksSummary
    {
        /// <summary>
        /// Gets or sets the count of link instances successfully repositioned.
        /// </summary>
        public int RepositionedCount { get; set; }

        /// <summary>
        /// Gets or sets the count of link instances that were already aligned.
        /// </summary>
        public int AlreadyAlignedCount { get; set; }

        /// <summary>
        /// Gets or sets the count of link instances that were skipped (e.g. unassigned or not shared).
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// Gets or sets the names of repositioned link instances.
        /// </summary>
        public IReadOnlyList<string> RepositionedInstanceNames { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the names of skipped link instances.
        /// </summary>
        public IReadOnlyList<string> SkippedInstanceNames { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets any error messages encountered during execution.
        /// </summary>
        public IReadOnlyList<string> ErrorMessages { get; set; } = new List<string>();
    }
}
