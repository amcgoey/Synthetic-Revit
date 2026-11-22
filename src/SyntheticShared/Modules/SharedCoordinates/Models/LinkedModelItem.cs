namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Represents a loaded linked model instance available for Shared Site synchronization.
    /// </summary>
    public class LinkedModelItem
    {
        /// <summary>
        /// Gets or sets the unique identifier of the linked model instance.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the linked model.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether this link instance was pre-selected in the viewport.
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// Returns the display name of the linked model.
        /// </summary>
        public override string ToString() => Name;
    }
}
