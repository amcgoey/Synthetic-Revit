using System.Collections.Generic;
using Synthetic.Modules.SheetIndex.Models;

namespace Synthetic.Modules.SheetIndex.Services
{
    /// <summary>
    /// Thread-safe store persisting user sheet index selection choices across command executions in the active Revit session.
    /// </summary>
    public static class SheetIndexSessionState
    {
        private static readonly object _lock = new object();

        private static SheetSelectionSourceMode _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
        private static string? _selectedPrintSetName;
        private static string? _selectedScheduleName;
        private static HashSet<string>? _selectedRevisionIds;
        private static HashSet<string>? _selectedSheetIds;
        private static bool _hasSavedState;

        public static SheetSelectionSourceMode SelectedSourceMode
        {
            get { lock (_lock) return _selectedSourceMode; }
            set { lock (_lock) _selectedSourceMode = value; }
        }

        public static string? SelectedPrintSetName
        {
            get { lock (_lock) return _selectedPrintSetName; }
            set { lock (_lock) _selectedPrintSetName = value; }
        }

        public static string? SelectedScheduleName
        {
            get { lock (_lock) return _selectedScheduleName; }
            set { lock (_lock) _selectedScheduleName = value; }
        }

        public static HashSet<string>? SelectedRevisionIds
        {
            get { lock (_lock) return _selectedRevisionIds != null ? new HashSet<string>(_selectedRevisionIds) : null; }
        }

        public static HashSet<string>? SelectedSheetIds
        {
            get { lock (_lock) return _selectedSheetIds != null ? new HashSet<string>(_selectedSheetIds) : null; }
        }

        public static bool HasSavedState
        {
            get { lock (_lock) return _hasSavedState; }
        }

        public static void SaveState(
            SheetSelectionSourceMode sourceMode,
            string? printSetName,
            string? scheduleName,
            IEnumerable<string>? selectedRevisionIds,
            IEnumerable<string>? selectedSheetIds)
        {
            lock (_lock)
            {
                _selectedSourceMode = sourceMode;
                _selectedPrintSetName = printSetName;
                _selectedScheduleName = scheduleName;

                _selectedRevisionIds = selectedRevisionIds != null ? new HashSet<string>(selectedRevisionIds) : null;
                _selectedSheetIds = selectedSheetIds != null ? new HashSet<string>(selectedSheetIds) : null;
                _hasSavedState = true;
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
                _selectedPrintSetName = null;
                _selectedScheduleName = null;
                _selectedRevisionIds = null;
                _selectedSheetIds = null;
                _hasSavedState = false;
            }
        }
    }
}

