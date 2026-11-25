namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable snapshot metadata for a view or sheet excluded from native PDF export.
    /// </summary>
    public sealed class MultiModelPdfSkippedViewInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfSkippedViewInfo"/> class.
        /// </summary>
        public MultiModelPdfSkippedViewInfo(
            string? displayCode,
            string? viewName,
            string? skipReason)
        {
            DisplayCode = displayCode ?? string.Empty;
            ViewName = viewName ?? string.Empty;
            SkipReason = skipReason ?? string.Empty;
        }

        /// <summary>Gets the sheet number or view type display code.</summary>
        public string DisplayCode { get; }

        /// <summary>Gets the view or sheet name.</summary>
        public string ViewName { get; }

        /// <summary>Gets the human-readable exclusion reason.</summary>
        public string SkipReason { get; }
    }
}
