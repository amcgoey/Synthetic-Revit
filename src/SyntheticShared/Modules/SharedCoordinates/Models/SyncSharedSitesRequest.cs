using System.Collections.Generic;

namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Encapsulates the parameters required to synchronize Level-Based Shared Sites from a linked host model.
    /// </summary>
    public class SyncSharedSitesRequest
    {
        /// <summary>
        /// Gets or sets the unique identifier of the selected linked host model.
        /// </summary>
        public string LinkedModelId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the selected Reference Datum Level in the active unit model.
        /// </summary>
        public LevelItem ReferenceDatumLevel { get; set; } = null!;

        /// <summary>
        /// Gets or sets the list of target level sites to synchronize.
        /// </summary>
        public IReadOnlyList<SharedSiteSyncTarget> TargetSites { get; set; } = new List<SharedSiteSyncTarget>();
    }
}
