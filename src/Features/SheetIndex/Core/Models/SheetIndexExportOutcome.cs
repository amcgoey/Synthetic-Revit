using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Synthetic.Features.SheetIndex.Models
{
    /// <summary>
    /// Typed outcome returned by <see cref="Services.SheetIndexExporter"/> describing export success,
    /// output destination, exported matrix counts, non-fatal warnings, and any save failure message.
    /// </summary>
    public sealed class SheetIndexExportOutcome
    {
        /// <summary>
        /// Gets a value indicating whether the .xlsx export succeeded.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the destination file path when exported to disk, or an empty string when exported to a stream.
        /// </summary>
        public string OutputFilePath { get; }

        /// <summary>
        /// Gets the number of sheet data rows written (excluding section header rows).
        /// </summary>
        public int ExportedSheetCount { get; }

        /// <summary>
        /// Gets the number of chronological Issuance Columns written.
        /// </summary>
        public int ExportedRevisionColumnCount { get; }

        /// <summary>
        /// Gets the number of section grouping header rows injected into the matrix.
        /// </summary>
        public int SectionHeaderCount { get; }

        /// <summary>
        /// Gets any non-fatal warning notices produced during export.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the failure diagnostic message when <see cref="Succeeded"/> is <c>false</c>; otherwise <c>null</c>.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SheetIndexExportOutcome"/> class.
        /// </summary>
        public SheetIndexExportOutcome(
            bool succeeded,
            string? outputFilePath,
            int exportedSheetCount,
            int exportedRevisionColumnCount,
            int sectionHeaderCount,
            IReadOnlyList<string>? warnings = null,
            string? errorMessage = null)
        {
            Succeeded = succeeded;
            OutputFilePath = outputFilePath ?? string.Empty;
            ExportedSheetCount = exportedSheetCount;
            ExportedRevisionColumnCount = exportedRevisionColumnCount;
            SectionHeaderCount = sectionHeaderCount;
            Warnings = warnings != null && warnings.Count > 0
                ? new ReadOnlyCollection<string>(warnings.ToList())
                : Array.Empty<string>();
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Creates a successful <see cref="SheetIndexExportOutcome"/>.
        /// </summary>
        public static SheetIndexExportOutcome Success(
            string? outputFilePath,
            int exportedSheetCount,
            int exportedRevisionColumnCount,
            int sectionHeaderCount,
            IReadOnlyList<string>? warnings = null)
        {
            return new SheetIndexExportOutcome(
                succeeded: true,
                outputFilePath: outputFilePath,
                exportedSheetCount: exportedSheetCount,
                exportedRevisionColumnCount: exportedRevisionColumnCount,
                sectionHeaderCount: sectionHeaderCount,
                warnings: warnings,
                errorMessage: null);
        }

        /// <summary>
        /// Creates a failed <see cref="SheetIndexExportOutcome"/>.
        /// </summary>
        public static SheetIndexExportOutcome Failure(
            string? outputFilePath,
            string errorMessage,
            IReadOnlyList<string>? warnings = null,
            int exportedSheetCount = 0,
            int exportedRevisionColumnCount = 0,
            int sectionHeaderCount = 0)
        {
            string resolvedError = string.IsNullOrWhiteSpace(errorMessage)
                ? "Sheet index export failed."
                : errorMessage;

            return new SheetIndexExportOutcome(
                succeeded: false,
                outputFilePath: outputFilePath,
                exportedSheetCount: exportedSheetCount,
                exportedRevisionColumnCount: exportedRevisionColumnCount,
                sectionHeaderCount: sectionHeaderCount,
                warnings: warnings,
                errorMessage: resolvedError);
        }
    }
}
