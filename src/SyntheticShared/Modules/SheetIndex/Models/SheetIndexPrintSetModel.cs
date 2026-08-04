using System;
using System.Collections.Generic;

namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// POCO model representing a Revit print set (ViewSheetSet) and its contained sheet unique IDs in print order sequence.
    /// </summary>
    public class SheetIndexPrintSetModel
    {
        /// <summary>
        /// Gets or sets the unique identifier or ElementId string of the print set.
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the print set (e.g. "Permit Submittal Set").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of sheet unique IDs included in this print set in native print order sequence.
        /// </summary>
        public List<string> SheetIds { get; set; } = new List<string>();

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SheetIndexPrintSetModel()
        {
        }

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        public SheetIndexPrintSetModel(string uniqueId, string name, IEnumerable<string>? sheetIds = null)
        {
            UniqueId = uniqueId;
            Name = name;
            if (sheetIds != null)
            {
                SheetIds.AddRange(sheetIds);
            }
        }
    }
}
