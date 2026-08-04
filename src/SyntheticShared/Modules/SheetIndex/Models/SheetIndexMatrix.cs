using System;
using System.Collections.Generic;

namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// Model representing a header column in the sheet index issuance matrix.
    /// </summary>
    public class SheetIndexRevisionHeader
    {
        public string UniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public int Sequence { get; set; }

        public SheetIndexRevisionHeader() { }

        public SheetIndexRevisionHeader(string uniqueId, string name, string date, int sequence)
        {
            UniqueId = uniqueId;
            Name = name;
            Date = date;
            Sequence = sequence;
        }
    }

    /// <summary>
    /// Model representing a row in the sheet index issuance matrix.
    /// </summary>
    public class SheetIndexRow
    {
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether this row represents a section grouping header (e.g. "STRUCTURAL", "ARCHITECTURAL").
        /// </summary>
        public bool IsSectionHeader { get; set; }

        /// <summary>
        /// Cell values corresponding to each Issuance Column. Solid bullet "●" when present, empty string "" when excluded.
        /// </summary>
        public List<string> Cells { get; set; } = new List<string>();

        public SheetIndexRow() { }

        public SheetIndexRow(string sheetNumber, string sheetName, List<string> cells, bool isSectionHeader = false)
        {
            SheetNumber = sheetNumber;
            SheetName = sheetName;
            Cells = cells ?? new List<string>();
            IsSectionHeader = isSectionHeader;
        }
    }

    /// <summary>
    /// Model representing the complete Sheet Index 2D matrix ready for export or display.
    /// </summary>
    public class SheetIndexMatrix
    {
        /// <summary>
        /// Issuance Indicator bullet symbol standard.
        /// </summary>
        public const string IssuanceIndicatorSymbol = "●";

        /// <summary>
        /// Header labels for the left metadata columns (e.g. "Sheet Number", "Sheet Name").
        /// </summary>
        public List<string> LeftColumns { get; set; } = new List<string> { "Sheet Number", "Sheet Name" };

        /// <summary>
        /// Issuance Columns ordered chronologically.
        /// </summary>
        public List<SheetIndexRevisionHeader> RevisionHeaders { get; set; } = new List<SheetIndexRevisionHeader>();

        /// <summary>
        /// Data rows ordered according to the active ordering strategy (alphanumeric by fallback).
        /// </summary>
        public List<SheetIndexRow> Rows { get; set; } = new List<SheetIndexRow>();

        public SheetIndexMatrix() { }
    }
}
