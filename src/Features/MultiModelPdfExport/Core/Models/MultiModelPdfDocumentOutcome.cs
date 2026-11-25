using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Typed Outcome Object returned by <see cref="Adapters.IMultiModelPdfExportAdapter.ExportDocument"/>
    /// for a single Revit project document.
    /// </summary>
    public sealed class MultiModelPdfDocumentOutcome
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfDocumentOutcome"/> class.
        /// </summary>
        public MultiModelPdfDocumentOutcome(
            string documentKey,
            string documentTitle,
            bool succeeded,
            IEnumerable<string>? generatedFilePaths = null,
            IEnumerable<string>? warnings = null,
            string? errorMessage = null)
        {
            DocumentKey = documentKey ?? string.Empty;
            DocumentTitle = documentTitle ?? string.Empty;
            Succeeded = succeeded;
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage!.Trim();

            var files = generatedFilePaths != null
                ? generatedFilePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                : new List<string>();
            GeneratedFilePaths = files.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(files);

            var notices = warnings != null
                ? warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList()
                : new List<string>();
            Warnings = notices.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(notices);
        }

        /// <summary>Gets the exported document key.</summary>
        public string DocumentKey { get; }

        /// <summary>Gets the exported document title.</summary>
        public string DocumentTitle { get; }

        /// <summary>Gets a value indicating whether the document export succeeded.</summary>
        public bool Succeeded { get; }

        /// <summary>Gets the full file paths of PDF files written for this document.</summary>
        public IReadOnlyList<string> GeneratedFilePaths { get; }

        /// <summary>Gets any non-blocking warnings emitted during export.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Gets the failure reason when <see cref="Succeeded"/> is <c>false</c>.</summary>
        public string? ErrorMessage { get; }

        /// <summary>Gets the number of PDF files generated for this document.</summary>
        public int GeneratedCount => GeneratedFilePaths.Count;

        /// <summary>Gets the primary generated PDF path, or <c>null</c> when none succeeded.</summary>
        public string? PrimaryFilePath => GeneratedFilePaths.Count > 0 ? GeneratedFilePaths[0] : null;

        /// <summary>Creates a successful document export outcome.</summary>
        public static MultiModelPdfDocumentOutcome Success(
            string documentKey,
            string documentTitle,
            IEnumerable<string> generatedFilePaths,
            IEnumerable<string>? warnings = null) =>
            new MultiModelPdfDocumentOutcome(documentKey, documentTitle, true, generatedFilePaths, warnings, null);

        /// <summary>Creates a failed document export outcome.</summary>
        public static MultiModelPdfDocumentOutcome Failure(
            string documentKey,
            string documentTitle,
            string errorMessage,
            IEnumerable<string>? warnings = null) =>
            new MultiModelPdfDocumentOutcome(documentKey, documentTitle, false, null, warnings, errorMessage);
    }
}
