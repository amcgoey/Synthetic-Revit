using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.DetailItemFactory.Models
{
    /// <summary>
    /// Typed outcome summary returned by <see cref="Services.DetailItemBatchPlanner.CleanupTempExportFiles"/>.
    /// </summary>
    public sealed class DetailItemTempCleanupSummary
    {
        public string OutputFolder { get; }
        public string FileNamePrefix { get; }
        public bool DirectoryExists { get; }
        public int DeletedCount { get; }
        public int FailedCount { get; }
        public IReadOnlyList<string> DeletedFiles { get; }
        public IReadOnlyList<string> Errors { get; }
        public string? EnumerationError { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemTempCleanupSummary"/> class.
        /// </summary>
        public DetailItemTempCleanupSummary(
            string outputFolder,
            string fileNamePrefix,
            bool directoryExists,
            IEnumerable<string>? deletedFiles = null,
            int failedCount = 0,
            IEnumerable<string>? errors = null,
            string? enumerationError = null)
        {
            OutputFolder = outputFolder ?? string.Empty;
            FileNamePrefix = fileNamePrefix ?? string.Empty;
            DirectoryExists = directoryExists;

            var deletedList = deletedFiles != null
                ? deletedFiles.Where(p => !string.IsNullOrEmpty(p)).ToList()
                : new List<string>();
            DeletedFiles = new ReadOnlyCollection<string>(deletedList);
            DeletedCount = deletedList.Count;
            FailedCount = Math.Max(0, failedCount);

            var errorList = errors != null
                ? errors.Where(e => !string.IsNullOrEmpty(e)).ToList()
                : new List<string>();
            Errors = new ReadOnlyCollection<string>(errorList);
            EnumerationError = enumerationError;
        }
    }
}
