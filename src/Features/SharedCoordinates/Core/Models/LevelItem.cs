namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Represents a model level with its internal elevation and shared coordinate elevation.
    /// </summary>
    public class LevelItem
    {
        /// <summary>
        /// Gets or sets the unique identifier of the level element.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the level.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the internal elevation of the level in feet.
        /// </summary>
        public double Elevation { get; set; }

        /// <summary>
        /// Gets or sets the shared coordinate elevation of the level in feet.
        /// </summary>
        public double SharedElevation { get; set; }

        /// <summary>
        /// Returns a formatted representation of the level name and elevation.
        /// </summary>
        public override string ToString() => $"{Name} ({Elevation:F2}')";
    }
}
