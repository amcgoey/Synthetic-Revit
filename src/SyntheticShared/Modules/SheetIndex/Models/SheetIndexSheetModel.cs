using System;
using System.Collections.Generic;

namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// POCO model representing a Revit sheet and its associated revisions for sheet index matrix generation.
    /// </summary>
    public class SheetIndexSheetModel
    {
        /// <summary>
        /// Gets or sets the unique identifier or ElementId string of the sheet.
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sheet number (e.g. "A101").
        /// </summary>
        public string SheetNumber { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sheet name (e.g. "FIRST FLOOR PLAN").
        /// </summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the set of revision unique IDs or identifiers associated with this sheet.
        /// </summary>
        public HashSet<string> RevisionIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or sets optional section grouping name derived from ViewSchedule sort/group fields (e.g. "STRUCTURAL", "ARCHITECTURAL").
        /// </summary>
        public string? SectionGroup { get; set; }

        /// <summary>
        /// Gets or sets the optional 0-based print order sequence index when sourced from a print set.
        /// </summary>
        public int? PrintOrderIndex { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SheetIndexSheetModel()
        {
        }

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        public SheetIndexSheetModel(string uniqueId, string sheetNumber, string sheetName, IEnumerable<string>? revisionIds = null, int? printOrderIndex = null)
        {
            UniqueId = uniqueId;
            SheetNumber = sheetNumber;
            SheetName = sheetName;
            PrintOrderIndex = printOrderIndex;
            if (revisionIds != null)
            {
                foreach (var revId in revisionIds)
                {
                    RevisionIds.Add(revId);
                }
            }
        }
    }
}
