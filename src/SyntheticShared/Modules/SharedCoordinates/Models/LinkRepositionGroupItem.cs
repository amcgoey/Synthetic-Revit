namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Represents a linked model type and its instance counts for Shared Site Link Repositioning.
    /// </summary>
    public class LinkRepositionGroupItem
    {
        /// <summary>
        /// Gets or sets the unique identifier of the RevitLinkType.
        /// </summary>
        public string TypeId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the linked model type.
        /// </summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the total number of loaded instances of this link type.
        /// </summary>
        public int TotalInstanceCount { get; set; }

        /// <summary>
        /// Gets or sets the number of instances whose placement transform is out of sync with their assigned Shared Site.
        /// </summary>
        public int OutOfSyncCount { get; set; }

        /// <summary>
        /// Gets or sets the number of instances with unassigned or not-shared sites.
        /// </summary>
        public int UnassignedCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this link group is selected for repositioning.
        /// </summary>
        public bool IsChecked { get; set; } = true;
    }
}
