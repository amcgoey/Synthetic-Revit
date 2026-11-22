using System.Collections.Generic;

namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Reports the outcome of a Shared Site Sync execution, detailing created and updated Level-Based Shared Sites.
    /// </summary>
    public class SyncSharedSitesSummary
    {
        /// <summary>
        /// Gets or sets the count of newly created Level-Based Shared Sites.
        /// </summary>
        public int CreatedCount { get; set; }

        /// <summary>
        /// Gets or sets the count of updated existing Level-Based Shared Sites.
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// Gets or sets the names of newly created Level-Based Shared Sites.
        /// </summary>
        public IReadOnlyList<string> CreatedSiteNames { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the names of updated existing Level-Based Shared Sites.
        /// </summary>
        public IReadOnlyList<string> UpdatedSiteNames { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets any error messages encountered during execution.
        /// </summary>
        public IReadOnlyList<string> ErrorMessages { get; set; } = new List<string>();
    }
}
