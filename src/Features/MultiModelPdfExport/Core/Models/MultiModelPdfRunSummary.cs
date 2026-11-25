using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.MultiModelPdfExport.Models
{
    /// <summary>
    /// Typed Outcome Object summarizing a completed or cancelled Multi-Model PDF Export batch run.
    /// </summary>
    public sealed class MultiModelPdfRunSummary
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfRunSummary"/> class.
        /// </summary>
        public MultiModelPdfRunSummary(
            string outputFolder,
            IEnumerable<string>? generatedFilePaths,
            IEnumerable<MultiModelPdfDocumentOutcome>? documentOutcomes,
            IEnumerable<string>? warnings,
            IEnumerable<string>? errors,
            int completedDocumentCount,
            int failedDocumentCount,
            bool wasCancelled)
        {
            OutputFolder = outputFolder ?? string.Empty;

            var files = generatedFilePaths != null
                ? generatedFilePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                : new List<string>();
            GeneratedFilePaths = files.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(files);

            var outcomes = documentOutcomes != null
                ? documentOutcomes.Where(o => o != null).ToList()
                : new List<MultiModelPdfDocumentOutcome>();
            DocumentOutcomes = outcomes.Count == 0
                ? Array.Empty<MultiModelPdfDocumentOutcome>()
                : (IReadOnlyList<MultiModelPdfDocumentOutcome>)new ReadOnlyCollection<MultiModelPdfDocumentOutcome>(outcomes);

            var warnList = warnings != null
                ? warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList()
                : new List<string>();
            Warnings = warnList.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(warnList);

            var errorList = errors != null
                ? errors.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList()
                : new List<string>();
            Errors = errorList.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(errorList);

            CompletedDocumentCount = Math.Max(0, completedDocumentCount);
            FailedDocumentCount = Math.Max(0, failedDocumentCount);
            WasCancelled = wasCancelled;

            var combinedNotices = new List<string>(warnList.Count + errorList.Count);
            combinedNotices.AddRange(warnList);
            foreach (string err in errorList)
            {
                if (!combinedNotices.Contains(err))
                {
                    combinedNotices.Add(err);
                }
            }

            CompletionNotices = combinedNotices.Count == 0
                ? Array.Empty<string>()
                : (IReadOnlyList<string>)new ReadOnlyCollection<string>(combinedNotices);
        }

        /// <summary>Gets the target Output Folder path.</summary>
        public string OutputFolder { get; }

        /// <summary>Gets the full paths of all PDF files successfully generated during the run.</summary>
        public IReadOnlyList<string> GeneratedFilePaths { get; }

        /// <summary>Gets per-document export outcomes.</summary>
        public IReadOnlyList<MultiModelPdfDocumentOutcome> DocumentOutcomes { get; }

        /// <summary>Gets non-blocking warning notices (skipped views, setup fallbacks, partial cancellation).</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Gets per-document error notices for failed documents.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Gets combined warnings and errors for display in the Export Completion Dialog.</summary>
        public IReadOnlyList<string> CompletionNotices { get; }

        /// <summary>Gets the number of documents that succeeded.</summary>
        public int CompletedDocumentCount { get; }

        /// <summary>Gets the number of documents that failed.</summary>
        public int FailedDocumentCount { get; }

        /// <summary>Gets a value indicating whether the batch was cancelled before finishing all jobs.</summary>
        public bool WasCancelled { get; }

        /// <summary>Gets the total number of PDF files generated.</summary>
        public int GeneratedCount => GeneratedFilePaths.Count;

        /// <summary>Gets the first generated PDF path, or <c>null</c> when none succeeded.</summary>
        public string? PrimaryFilePath => GeneratedFilePaths.Count > 0 ? GeneratedFilePaths[0] : null;
    }
}
