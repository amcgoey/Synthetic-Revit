using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Typed outcome object returned by <see cref="Adapters.IDetailItemFactoryAdapter.ExecuteBatch"/>.
    /// </summary>
    public sealed class DetailItemBatchOutcome
    {
        public bool Succeeded { get; }
        public bool WasCancelled { get; }
        public string OutputFolder { get; }
        public int TotalCount { get; }
        public int SucceededCount { get; }
        public int PartialSuccessCount { get; }
        public int SkippedCount { get; }
        public int FailedCount { get; }
        public int CommittedCount { get; }
        public IReadOnlyList<DetailItemResultItem> Results { get; }
        public IReadOnlyList<string> Warnings { get; }
        public string? ErrorMessage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemBatchOutcome"/> class.
        /// </summary>
        public DetailItemBatchOutcome(
            bool succeeded,
            bool wasCancelled,
            string? outputFolder,
            IEnumerable<DetailItemResultItem>? results = null,
            IEnumerable<string>? warnings = null,
            string? errorMessage = null)
        {
            Succeeded = succeeded;
            WasCancelled = wasCancelled;
            OutputFolder = outputFolder ?? string.Empty;

            var resultList = results != null
                ? results.Where(r => r != null).ToList()
                : new List<DetailItemResultItem>();
            Results = new ReadOnlyCollection<DetailItemResultItem>(resultList);

            TotalCount = resultList.Count;
            SucceededCount = resultList.Count(r => string.Equals(r.Status, DetailItemResultStatus.Success, StringComparison.Ordinal));
            PartialSuccessCount = resultList.Count(r => string.Equals(r.Status, DetailItemResultStatus.PartialSuccess, StringComparison.Ordinal));
            SkippedCount = resultList.Count(r => string.Equals(r.Status, DetailItemResultStatus.Skipped, StringComparison.Ordinal));
            FailedCount = resultList.Count(r => string.Equals(r.Status, DetailItemResultStatus.Failed, StringComparison.Ordinal));
            CommittedCount = SucceededCount + PartialSuccessCount;

            var warningList = warnings != null
                ? warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToList()
                : new List<string>();
            Warnings = warningList.Count > 0
                ? new ReadOnlyCollection<string>(warningList)
                : Array.Empty<string>();

            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Creates a completed batch outcome (with optional cancellation flag).
        /// </summary>
        public static DetailItemBatchOutcome Completed(
            string? outputFolder,
            IEnumerable<DetailItemResultItem>? results,
            bool wasCancelled = false,
            IEnumerable<string>? warnings = null)
        {
            return new DetailItemBatchOutcome(
                succeeded: true,
                wasCancelled: wasCancelled,
                outputFolder: outputFolder,
                results: results,
                warnings: warnings,
                errorMessage: null);
        }

        /// <summary>
        /// Creates a batch-level failure outcome (such as missing template or invalid output directory).
        /// </summary>
        public static DetailItemBatchOutcome Failure(
            string? outputFolder,
            string errorMessage,
            IEnumerable<DetailItemResultItem>? results = null,
            IEnumerable<string>? warnings = null)
        {
            string resolvedError = string.IsNullOrWhiteSpace(errorMessage)
                ? "Detail Item Factory batch execution failed."
                : errorMessage;

            return new DetailItemBatchOutcome(
                succeeded: false,
                wasCancelled: false,
                outputFolder: outputFolder,
                results: results,
                warnings: warnings,
                errorMessage: resolvedError);
        }
    }
}
