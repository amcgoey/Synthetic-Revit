namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Machine-local user interface preferences for the Multi-Model PDF Export dialog.
    /// Persisted outside the Revit document so Export never modifies the model or Undo stack.
    /// </summary>
    public sealed class MultiModelPdfExportUserPrefs
    {
        /// <summary>
        /// Gets or sets the last confirmed output folder path.
        /// </summary>
        public string LastOutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last confirmed PDF file output structure mode
        /// (<see cref="MultiModelPdfFileMode.CombinedPerDocument"/> default).
        /// </summary>
        public MultiModelPdfFileMode FileMode { get; set; } = MultiModelPdfFileMode.CombinedPerDocument;

        /// <summary>
        /// Gets or sets the last confirmed PDF Export Setup name
        /// (empty when <c>&lt;Default — Use Sheet Size&gt;</c> is active).
        /// </summary>
        public string PdfSetupName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the last confirmed bulk Print Set preference name (empty when none).
        /// </summary>
        public string LastBulkPrintSetName { get; set; } = string.Empty;
    }
}
