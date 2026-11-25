using System;

namespace Synthetic.Features.MultiModelPdfExport.ViewModels
{
    /// <summary>
    /// Semantic tone for the inline dismissible bulk Print Set feedback banner.
    /// </summary>
    public enum MultiModelPdfBulkFeedbackKind
    {
        None = 0,
        Success = 1,
        Warning = 2
    }

    /// <summary>
    /// Immutable option shown in the bulk <c>Apply Print Set</c> dropdown, ordered by active-document
    /// presence, matching eligible project count, and name.
    /// </summary>
    public sealed class MultiModelPdfBulkPrintSetOption
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfBulkPrintSetOption"/> class.
        /// </summary>
        public MultiModelPdfBulkPrintSetOption(
            string name,
            int matchCount,
            int totalCount,
            bool isInActiveDocument)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Print Set name cannot be blank.", nameof(name));
            }

            Name = name;
            MatchCount = Math.Max(0, matchCount);
            TotalCount = Math.Max(0, totalCount);
            IsInActiveDocument = isInActiveDocument;
            DisplayName = $"{Name} ({MatchCount} of {TotalCount} models)";
        }

        /// <summary>Gets the canonical Print Set name.</summary>
        public string Name { get; }

        /// <summary>Gets the count of eligible project rows containing this Print Set.</summary>
        public int MatchCount { get; }

        /// <summary>Gets the total discovered project row count in the session.</summary>
        public int TotalCount { get; }

        /// <summary>Gets whether the active project document contains this Print Set.</summary>
        public bool IsInActiveDocument { get; }

        /// <summary>Gets the formatted Variant A dropdown label.</summary>
        public string DisplayName { get; }
    }
}
