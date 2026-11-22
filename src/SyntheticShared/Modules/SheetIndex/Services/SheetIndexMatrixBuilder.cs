using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.Services
{
    /// <summary>
    /// Natural alphanumeric string comparer for sorting sheet numbers (e.g. A1, A2, A10, A101, B1).
    /// </summary>
    public class AlphanumericComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            string[] xRegex = Regex.Split(x.Replace(" ", ""), "([0-9]+)");
            string[] yRegex = Regex.Split(y.Replace(" ", ""), "([0-9]+)");

            for (int i = 0; i < Math.Min(xRegex.Length, yRegex.Length); i++)
            {
                if (xRegex[i] != yRegex[i])
                {
                    if (long.TryParse(xRegex[i], out long xNum) && long.TryParse(yRegex[i], out long yNum))
                    {
                        return xNum.CompareTo(yNum);
                    }
                    return string.Compare(xRegex[i], yRegex[i], StringComparison.OrdinalIgnoreCase);
                }
            }

            return xRegex.Length.CompareTo(yRegex.Length);
        }
    }

    /// <summary>
    /// Pure POCO engine for converting raw sheet and revision models into a 2D sheet index matrix.
    /// </summary>
    public class SheetIndexMatrixBuilder
    {
        private static readonly AlphanumericComparer SheetComparer = new AlphanumericComparer();

        /// <summary>
        /// Transforms raw sheet models and selected revision models into a formatted SheetIndexMatrix.
        /// </summary>
        /// <param name="sheets">Collection of project sheet models.</param>
        /// <param name="revisions">Collection of project revision models selected for export.</param>
        /// <param name="preserveSheetOrder">If true, preserves input sheet sequence (e.g. from ViewSchedule); otherwise sorts by PrintOrderIndex / alphanumeric.</param>
        /// <returns>Formatted 2D sheet index matrix with chronological revision columns and ordered sheet rows with section headers.</returns>
        public SheetIndexMatrix BuildMatrix(IEnumerable<SheetIndexSheetModel> sheets, IEnumerable<SheetIndexRevisionModel> revisions, bool preserveSheetOrder = false)
        {
            var matrix = new SheetIndexMatrix();

            if (sheets == null || revisions == null)
            {
                return matrix;
            }

            // 1. Order selected revisions chronologically by Sequence, then Date
            var sortedRevisions = revisions
                .OrderBy(r => r.Sequence)
                .ThenBy(r => r.Date, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var rev in sortedRevisions)
            {
                matrix.RevisionHeaders.Add(new SheetIndexRevisionHeader(rev.UniqueId, rev.Name, rev.Date, rev.Sequence));
            }

            // 2. Order sheets by strategy (preserve sequence vs PrintOrderIndex / fallback alphanumeric sorting)
            List<SheetIndexSheetModel> sortedSheets;
            if (preserveSheetOrder)
            {
                sortedSheets = sheets.ToList();
            }
            else
            {
                sortedSheets = sheets
                    .OrderBy(s => s.PrintOrderIndex.HasValue ? s.PrintOrderIndex.Value : int.MaxValue)
                    .ThenBy(s => s.SheetNumber ?? string.Empty, SheetComparer)
                    .ToList();
            }

            // 3. Build data rows and inject section headers when SectionGroup changes
            string? currentSectionGroup = null;

            foreach (var sheet in sortedSheets)
            {
                // Inject section header row when SectionGroup changes
                if (!string.IsNullOrWhiteSpace(sheet.SectionGroup) &&
                    !string.Equals(sheet.SectionGroup, currentSectionGroup, StringComparison.OrdinalIgnoreCase))
                {
                    var headerCells = matrix.RevisionHeaders.Select(_ => string.Empty).ToList();
                    matrix.Rows.Add(new SheetIndexRow(sheet.SectionGroup, string.Empty, headerCells, isSectionHeader: true));
                    currentSectionGroup = sheet.SectionGroup;
                }

                var rowCells = new List<string>();
                foreach (var revHeader in matrix.RevisionHeaders)
                {
                    bool isIncluded = sheet.RevisionIds != null && sheet.RevisionIds.Contains(revHeader.UniqueId);
                    rowCells.Add(isIncluded ? SheetIndexMatrix.IssuanceIndicatorSymbol : string.Empty);
                }

                matrix.Rows.Add(new SheetIndexRow(sheet.SheetNumber, sheet.SheetName, rowCells));
            }

            return matrix;
        }
    }
}
