namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Represents the results of processing a single model element to a 2D Detail Item family.
    /// </summary>
    public class DetailItemResultItem
    {
        /// <summary>
        /// Gets or sets the name of the processed model element.
        /// </summary>
        public string ElementName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Revit Element ID of the processed element.
        /// </summary>
        public string ElementId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the execution status (e.g., Success, Skipped, Failed).
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a description or details (such as exception messages or skip reasons).
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
