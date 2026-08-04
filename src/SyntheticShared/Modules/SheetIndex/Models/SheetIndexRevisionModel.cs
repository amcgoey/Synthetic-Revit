using System;

namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// POCO model representing a Revit revision for sheet index matrix generation.
    /// </summary>
    public class SheetIndexRevisionModel
    {
        /// <summary>
        /// Gets or sets the unique identifier or ElementId string of the revision.
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the revision name or description (e.g. "PERMIT SUBMITTAL").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the revision issuance date string (e.g. "2026-08-04").
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the sequence number for chronological ordering.
        /// </summary>
        public int Sequence { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public SheetIndexRevisionModel()
        {
        }

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        public SheetIndexRevisionModel(string uniqueId, string name, string date, int sequence)
        {
            UniqueId = uniqueId;
            Name = name;
            Date = date;
            Sequence = sequence;
        }
    }
}
