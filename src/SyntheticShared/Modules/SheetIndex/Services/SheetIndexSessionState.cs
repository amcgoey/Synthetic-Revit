using System.Collections.Generic;
using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.Services
{
    /// <summary>
    /// Stores user selection choices across command runs within the active Revit session.
    /// </summary>
    public static class SheetIndexSessionState
    {
        private static readonly object _lock = new object();

        public static SheetSelectionSourceMode SelectedSourceMode { get; set; } = SheetSelectionSourceMode.AllSheets;
        public static string? SelectedPrintSetName { get; set; }
        public static string? SelectedScheduleName { get; set; }
        public static HashSet<string>? SelectedRevisionIds { get; set; }
        public static HashSet<string>? SelectedSheetIds { get; set; }
        public static bool HasSavedState { get; private set; }

        public static void SaveState(
            SheetSelectionSourceMode sourceMode,
            string? printSetName,
            string? scheduleName,
            IEnumerable<string>? selectedRevisionIds,
            IEnumerable<string>? selectedSheetIds)
        {
            lock (_lock)
            {
                SelectedSourceMode = sourceMode;
                SelectedPrintSetName = printSetName;
                SelectedScheduleName = scheduleName;

                SelectedRevisionIds = selectedRevisionIds != null ? new HashSet<string>(selectedRevisionIds) : null;
                SelectedSheetIds = selectedSheetIds != null ? new HashSet<string>(selectedSheetIds) : null;
                HasSavedState = true;
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                SelectedSourceMode = SheetSelectionSourceMode.AllSheets;
                SelectedPrintSetName = null;
                SelectedScheduleName = null;
                SelectedRevisionIds = null;
                SelectedSheetIds = null;
                HasSavedState = false;
            }
        }
    }
}
