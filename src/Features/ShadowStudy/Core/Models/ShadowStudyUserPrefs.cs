namespace Synthetic.Features.ShadowStudy.Models
{
    /// <summary>
    /// Machine-local user interface preferences for the Shadow Study dialog.
    /// Persisted outside the Revit document so Export never modifies the model or Undo stack.
    /// </summary>
    public class ShadowStudyUserPrefs
    {
        /// <summary>
        /// Gets or sets the last confirmed output folder path.
        /// </summary>
        public string LastOutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last confirmed horizontal PNG pixel width (<c>3000</c> default).
        /// </summary>
        public int PixelWidth { get; set; } = 3000;

        /// <summary>
        /// Gets or sets a value indicating whether individual Shadow Frame PNGs are exported (<c>true</c> default).
        /// </summary>
        public bool ExportIndividualFrames { get; set; } = true;
    }
}
