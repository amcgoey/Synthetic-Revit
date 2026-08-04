using System;
using System.Collections.Generic;

namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// POCO model representing a Revit ViewSchedule sheet schedule and its scheduled sheets.
    /// </summary>
    public class SheetIndexScheduleModel
    {
        /// <summary>
        /// Gets or sets the unique identifier of the ViewSchedule.
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the view schedule title/name (e.g. "DRAWING INDEX").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the collection of sheet models contained in this schedule in schedule sort order.
        /// </summary>
        public List<SheetIndexSheetModel> Sheets { get; set; } = new List<SheetIndexSheetModel>();

        public SheetIndexScheduleModel() { }

        public SheetIndexScheduleModel(string uniqueId, string name, IEnumerable<SheetIndexSheetModel>? sheets = null)
        {
            UniqueId = uniqueId;
            Name = name;
            if (sheets != null)
            {
                Sheets.AddRange(sheets);
            }
        }
    }
}
