using System.Collections.Generic;
using Synthetic.Features.SheetIndex.Models;

namespace Synthetic.Features.SheetIndex.Services
{
    /// <summary>
    /// Thread-safe store persisting user sheet index selection choices across command executions in the active Revit session.
    /// </summary>
    public class SheetIndexSessionState
    {
        private readonly object _lock = new object();

        private SheetSelectionSourceMode _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
        private string? _selectedPrintSetName;
        private string? _selectedScheduleName;
        private HashSet<string>? _selectedRevisionIds;
        private HashSet<string>? _selectedSheetIds;
        private HashSet<string>? _knownRevisionIds;
        private HashSet<string>? _knownSheetIds;
        private bool _hasSavedState;

        public SheetSelectionSourceMode SelectedSourceMode
        {
            get { lock (_lock) return _selectedSourceMode; }
            set { lock (_lock) _selectedSourceMode = value; }
        }

        public string? SelectedPrintSetName
        {
            get { lock (_lock) return _selectedPrintSetName; }
            set { lock (_lock) _selectedPrintSetName = value; }
        }

        public string? SelectedScheduleName
        {
            get { lock (_lock) return _selectedScheduleName; }
            set { lock (_lock) _selectedScheduleName = value; }
        }

        public HashSet<string>? SelectedRevisionIds
        {
            get { lock (_lock) return _selectedRevisionIds != null ? new HashSet<string>(_selectedRevisionIds) : null; }
        }

        public HashSet<string>? SelectedSheetIds
        {
            get { lock (_lock) return _selectedSheetIds != null ? new HashSet<string>(_selectedSheetIds) : null; }
        }

        public HashSet<string>? KnownRevisionIds
        {
            get { lock (_lock) return _knownRevisionIds != null ? new HashSet<string>(_knownRevisionIds) : null; }
        }

        public HashSet<string>? KnownSheetIds
        {
            get { lock (_lock) return _knownSheetIds != null ? new HashSet<string>(_knownSheetIds) : null; }
        }

        public bool HasSavedState
        {
            get { lock (_lock) return _hasSavedState; }
        }

        public void SaveState(
            SheetSelectionSourceMode sourceMode,
            string? printSetName,
            string? scheduleName,
            IEnumerable<string>? selectedRevisionIds,
            IEnumerable<string>? selectedSheetIds,
            IEnumerable<string>? knownRevisionIds = null,
            IEnumerable<string>? knownSheetIds = null)
        {
            lock (_lock)
            {
                _selectedSourceMode = sourceMode;
                _selectedPrintSetName = printSetName;
                _selectedScheduleName = scheduleName;

                _selectedRevisionIds = selectedRevisionIds != null ? new HashSet<string>(selectedRevisionIds) : null;
                _selectedSheetIds = selectedSheetIds != null ? new HashSet<string>(selectedSheetIds) : null;
                _knownRevisionIds = knownRevisionIds != null ? new HashSet<string>(knownRevisionIds) : null;
                _knownSheetIds = knownSheetIds != null ? new HashSet<string>(knownSheetIds) : null;
                _hasSavedState = true;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
                _selectedPrintSetName = null;
                _selectedScheduleName = null;
                _selectedRevisionIds = null;
                _selectedSheetIds = null;
                _knownRevisionIds = null;
                _knownSheetIds = null;
                _hasSavedState = false;
            }
        }
    }
}

