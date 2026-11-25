using System;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Immutable input descriptor for deterministic title-collision token resolution.
    /// </summary>
    public sealed class MultiModelPdfTokenCandidate
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfTokenCandidate"/> class.
        /// </summary>
        public MultiModelPdfTokenCandidate(
            string documentKey,
            string? documentTitle,
            MultiModelPdfDocumentOrigin origin)
        {
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                throw new ArgumentException("Document key cannot be blank.", nameof(documentKey));
            }

            DocumentKey = documentKey;
            DocumentTitle = documentTitle ?? string.Empty;
            Origin = origin;
        }

        /// <summary>Gets the deterministic document identity key.</summary>
        public string DocumentKey { get; }

        /// <summary>Gets the raw Revit document title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Gets the document session origin governing collision priority.</summary>
        public MultiModelPdfDocumentOrigin Origin { get; }
    }
}
