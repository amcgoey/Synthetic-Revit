namespace Synthetic.Modules.SheetIndex.Models
{
    /// <summary>
    /// Represents the source mechanism used to harvest sheets for the sheet index.
    /// </summary>
    public enum SheetSelectionSourceType
    {
        /// <summary>
        /// All valid non-placeholder sheets in the Revit document.
        /// </summary>
        AllSheets,

        /// <summary>
        /// Sheets filtered by a selected ViewSheetSet (Print Set).
        /// </summary>
        PrintSet
    }
}
