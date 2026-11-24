using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

using Synthetic.Features.SheetIndex.Models;
using Synthetic.Features.SheetIndex.Services;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SheetIndex.ViewModels
{
    public class ExportSheetIndexViewModel : INotifyPropertyChanged
    {
        private readonly SheetIndexSessionState _sessionState;
        private string _searchText = string.Empty;
        private SheetSelectionSourceMode _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
        private string? _selectedPrintSetName;
        private string? _selectedScheduleName;
        private bool _isApplyingSourceMode;
        private List<SheetIndexSheetModel> _allProjectSheets = new List<SheetIndexSheetModel>();
        private SheetIndexScheduleModel? _allSheetsOption;
        private SheetIndexScheduleModel? _selectedSchedule;
        private SheetIndexPrintSetModel? _selectedPrintSet;
        private bool _preserveSheetOrder;

        public ObservableCollection<SheetItemViewModel> Sheets { get; } = new ObservableCollection<SheetItemViewModel>();
        public ObservableCollection<RevisionItemViewModel> Revisions { get; } = new ObservableCollection<RevisionItemViewModel>();
        public ObservableCollection<SheetIndexScheduleModel> Schedules { get; } = new ObservableCollection<SheetIndexScheduleModel>();
        public ObservableCollection<SheetIndexPrintSetModel> PrintSets { get; } = new ObservableCollection<SheetIndexPrintSetModel>();

        public SheetIndexScheduleModel? SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                if (!Equals(_selectedSchedule, value))
                {
                    _selectedSchedule = value;
                    OnPropertyChanged();

                    string? resolvedName = (value != null && !ReferenceEquals(value, _allSheetsOption)) ? value.Name : null;
                    if (!string.Equals(_selectedScheduleName, resolvedName, StringComparison.Ordinal))
                    {
                        _selectedScheduleName = resolvedName;
                        OnPropertyChanged(nameof(SelectedScheduleName));
                    }

                    OnScheduleSelectionChanged();
                }
            }
        }

        public SheetIndexPrintSetModel? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                if (!Equals(_selectedPrintSet, value))
                {
                    _selectedPrintSet = value;
                    OnPropertyChanged();

                    string? resolvedName = value?.Name;
                    if (!string.Equals(_selectedPrintSetName, resolvedName, StringComparison.Ordinal))
                    {
                        _selectedPrintSetName = resolvedName;
                        OnPropertyChanged(nameof(SelectedPrintSetName));
                    }

                    OnPrintSetSelectionChanged();
                }
            }
        }

        public bool PreserveSheetOrder
        {
            get => _preserveSheetOrder;
            set
            {
                if (_preserveSheetOrder != value)
                {
                    _preserveSheetOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICollectionView VisibleSheets { get; }

        public List<SheetSelectionSourceMode> AvailableSourceModes { get; } = new List<SheetSelectionSourceMode>
        {
            SheetSelectionSourceMode.AllSheets,
            SheetSelectionSourceMode.ViewSheetSet,
            SheetSelectionSourceMode.ViewSchedule
        };

        public ObservableCollection<string> AvailablePrintSets { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableSchedules { get; } = new ObservableCollection<string>();

        public Dictionary<string, List<string>> PrintSetSheetMap { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ScheduleSheetMap { get; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    VisibleSheets.Refresh();
                }
            }
        }

        public SheetSelectionSourceMode SelectedSourceMode
        {
            get => _selectedSourceMode;
            set
            {
                if (_selectedSourceMode != value)
                {
                    _selectedSourceMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedSourceType));
                    OnPropertyChanged(nameof(IsPrintSetSource));
                    OnPropertyChanged(nameof(IsScheduleSource));
                    OnPropertyChanged(nameof(IsPrintSetSourceSelected));
                    ApplySourceModeSelection(autoDefaultSelection: true);
                }
            }
        }

        public SheetSelectionSourceType SelectedSourceType
        {
            get => SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet ? SheetSelectionSourceType.PrintSet : SheetSelectionSourceType.AllSheets;
            set
            {
                if (value == SheetSelectionSourceType.PrintSet)
                {
                    SelectedSourceMode = SheetSelectionSourceMode.ViewSheetSet;
                }
                else
                {
                    SelectedSourceMode = SheetSelectionSourceMode.AllSheets;
                }
            }
        }

        public string? SelectedPrintSetName
        {
            get => _selectedPrintSetName;
            set
            {
                if (!string.Equals(_selectedPrintSetName, value, StringComparison.Ordinal))
                {
                    _selectedPrintSetName = value;
                    OnPropertyChanged();
                    SyncSelectedPrintSetFromName(notifyPropertyChanged: true);
                    if (SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet)
                    {
                        ApplySourceModeSelection();
                    }
                }
            }
        }

        public string? SelectedScheduleName
        {
            get => _selectedScheduleName;
            set
            {
                if (!string.Equals(_selectedScheduleName, value, StringComparison.Ordinal))
                {
                    _selectedScheduleName = value;
                    OnPropertyChanged();
                    SyncSelectedScheduleFromName(notifyPropertyChanged: true);
                    if (SelectedSourceMode == SheetSelectionSourceMode.ViewSchedule)
                    {
                        ApplySourceModeSelection();
                    }
                }
            }
        }

        public bool IsPrintSetSource => SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet;
        public bool IsScheduleSource => SelectedSourceMode == SheetSelectionSourceMode.ViewSchedule;
        public bool IsPrintSetSourceSelected => SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet;

        public ICommand SelectAllSheetsCommand { get; }
        public ICommand DeselectAllSheetsCommand { get; }
        public ICommand SelectAllRevisionsCommand { get; }
        public ICommand DeselectAllRevisionsCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CancelCommand { get; }

        public bool? DialogResult { get; private set; }

        public event Action? RequestClose;

        /// <summary>
        /// Initializes a new instance of <see cref="ExportSheetIndexViewModel"/> from an immutable <see cref="SheetIndexSnapshot"/>.
        /// </summary>
        /// <param name="snapshot">The harvested project snapshot.</param>
        /// <param name="sessionState">Optional session state for restoring previous selections.</param>
        public ExportSheetIndexViewModel(SheetIndexSnapshot snapshot, SheetIndexSessionState? sessionState = null)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _sessionState = sessionState ?? new SheetIndexSessionState();

            VisibleSheets = CollectionViewSource.GetDefaultView(Sheets);
            VisibleSheets.Filter = FilterSheetItem;

            SelectAllSheetsCommand = new RelayCommand(_ => SetVisibleSheetsSelected(true));
            DeselectAllSheetsCommand = new RelayCommand(_ => SetVisibleSheetsSelected(false));
            SelectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(true));
            DeselectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(false));
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExecuteExport());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());

            LoadData(snapshot);
        }

        public void LoadData(SheetIndexSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            foreach (var s in Sheets) s.PropertyChanged -= OnItemSelectionChanged;
            foreach (var r in Revisions) r.PropertyChanged -= OnItemSelectionChanged;

            AvailablePrintSets.Clear();
            AvailableSchedules.Clear();
            PrintSetSheetMap.Clear();
            ScheduleSheetMap.Clear();
            PrintSets.Clear();
            Schedules.Clear();

            _selectedPrintSet = null;
            _selectedSchedule = null;
            _preserveSheetOrder = false;

            foreach (SheetIndexPrintSetModel printSet in snapshot.PrintSets)
            {
                if (printSet == null || string.IsNullOrWhiteSpace(printSet.Name))
                {
                    continue;
                }

                if (PrintSetSheetMap.ContainsKey(printSet.Name))
                {
                    continue;
                }

                List<string> sheetIds = snapshot.PrintSetSheetMap.TryGetValue(printSet.Name, out var mappedIds)
                    ? mappedIds.ToList()
                    : (printSet.SheetIds != null ? printSet.SheetIds.ToList() : new List<string>());

                PrintSets.Add(printSet);
                AvailablePrintSets.Add(printSet.Name);
                PrintSetSheetMap[printSet.Name] = sheetIds;
            }

            _allProjectSheets = snapshot.Sheets != null
                ? snapshot.Sheets.ToList()
                : new List<SheetIndexSheetModel>();

            _allSheetsOption = new SheetIndexScheduleModel(string.Empty, "<All Project Sheets>", _allProjectSheets);
            Schedules.Add(_allSheetsOption);

            foreach (SheetIndexScheduleModel schedule in snapshot.Schedules)
            {
                if (schedule == null ||
                    string.IsNullOrWhiteSpace(schedule.Name) ||
                    schedule.Sheets == null ||
                    schedule.Sheets.Count == 0)
                {
                    continue;
                }

                if (ScheduleSheetMap.ContainsKey(schedule.Name))
                {
                    continue;
                }

                List<string> sheetIds = snapshot.ScheduleSheetMap.TryGetValue(schedule.Name, out var mappedIds)
                    ? mappedIds.ToList()
                    : schedule.Sheets.Where(s => s != null && !string.IsNullOrWhiteSpace(s.UniqueId)).Select(s => s.UniqueId).ToList();

                Schedules.Add(schedule);
                AvailableSchedules.Add(schedule.Name);
                ScheduleSheetMap[schedule.Name] = sheetIds;
            }

            var savedSheetIds = _sessionState.SelectedSheetIds;
            var knownSheetIds = _sessionState.KnownSheetIds;
            var savedRevisionIds = _sessionState.SelectedRevisionIds;
            var knownRevisionIds = _sessionState.KnownRevisionIds;

            Sheets.Clear();
            foreach (var sheet in _allProjectSheets)
            {
                bool isSelected = ResolveInitialSelection(sheet.UniqueId, savedSheetIds, knownSheetIds);
                var item = new SheetItemViewModel(sheet, isSelected);
                item.PropertyChanged += OnItemSelectionChanged;
                Sheets.Add(item);
            }

            Revisions.Clear();
            if (snapshot.Revisions != null)
            {
                foreach (var rev in snapshot.Revisions)
                {
                    bool isSelected = ResolveInitialSelection(rev.UniqueId, savedRevisionIds, knownRevisionIds);
                    var item = new RevisionItemViewModel(rev, isSelected);
                    item.PropertyChanged += OnItemSelectionChanged;
                    Revisions.Add(item);
                }
            }

            if (_sessionState.HasSavedState)
            {
                _selectedSourceMode = _sessionState.SelectedSourceMode;
                _selectedPrintSetName = _sessionState.SelectedPrintSetName;
                _selectedScheduleName = _sessionState.SelectedScheduleName;
            }
            else
            {
                _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
                _selectedPrintSetName = AvailablePrintSets.Count > 0 ? AvailablePrintSets[0] : null;
                _selectedScheduleName = AvailableSchedules.Count > 0 ? AvailableSchedules[0] : null;
            }

            SyncSelectedPrintSetFromName(notifyPropertyChanged: false);
            SyncSelectedScheduleFromName(notifyPropertyChanged: false);

            OnPropertyChanged(nameof(SelectedSourceMode));
            OnPropertyChanged(nameof(SelectedSourceType));
            OnPropertyChanged(nameof(SelectedPrintSetName));
            OnPropertyChanged(nameof(SelectedScheduleName));
            OnPropertyChanged(nameof(SelectedPrintSet));
            OnPropertyChanged(nameof(SelectedSchedule));
            OnPropertyChanged(nameof(IsPrintSetSource));
            OnPropertyChanged(nameof(IsScheduleSource));
            OnPropertyChanged(nameof(IsPrintSetSourceSelected));

            ApplySourceModeSelection(isInitialRestore: true);

            VisibleSheets.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private bool ResolveInitialSelection(string id, HashSet<string>? savedIds, HashSet<string>? knownIds)
        {
            if (!_sessionState.HasSavedState || savedIds == null)
            {
                return true;
            }
            if (knownIds != null && !knownIds.Contains(id))
            {
                return true;
            }
            return savedIds.Contains(id);
        }

        private void OnItemSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SheetItemViewModel.IsSelected))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void OnScheduleSelectionChanged(bool applySavedSelection = false)
        {
            if (SelectedSchedule != null && !ReferenceEquals(SelectedSchedule, _allSheetsOption) && SelectedSchedule.Sheets.Count > 0)
            {
                PopulateSheets(SelectedSchedule.Sheets, preserveOrder: true, applySavedSelection: applySavedSelection);
            }
            else
            {
                PopulateSheets(_allProjectSheets, preserveOrder: false, applySavedSelection: applySavedSelection);
            }
        }

        private void OnPrintSetSelectionChanged(bool applySavedSelection = false)
        {
            if (SelectedPrintSet != null && SelectedPrintSet.SheetIds.Count > 0)
            {
                var sheetMap = new Dictionary<string, SheetIndexSheetModel>(StringComparer.OrdinalIgnoreCase);
                foreach (SheetIndexSheetModel sheet in _allProjectSheets)
                {
                    if (!string.IsNullOrEmpty(sheet.UniqueId) && !sheetMap.ContainsKey(sheet.UniqueId))
                    {
                        sheetMap[sheet.UniqueId] = sheet;
                    }
                }

                var orderedSheets = new List<SheetIndexSheetModel>();
                for (int i = 0; i < SelectedPrintSet.SheetIds.Count; i++)
                {
                    string id = SelectedPrintSet.SheetIds[i];
                    if (sheetMap.TryGetValue(id, out var sheet))
                    {
                        orderedSheets.Add(new SheetIndexSheetModel(sheet.UniqueId, sheet.SheetNumber, sheet.SheetName, sheet.RevisionIds, printOrderIndex: i));
                    }
                }

                PopulateSheets(orderedSheets, preserveOrder: true, applySavedSelection: applySavedSelection);
            }
            else
            {
                PopulateSheets(_allProjectSheets, preserveOrder: false, applySavedSelection: applySavedSelection);
            }
        }

        private void PopulateSheets(IEnumerable<SheetIndexSheetModel> sheets, bool preserveOrder, bool applySavedSelection = false)
        {
            foreach (var s in Sheets) s.PropertyChanged -= OnItemSelectionChanged;
            Sheets.Clear();
            if (sheets != null)
            {
                var savedSheetIds = _sessionState.SelectedSheetIds;
                var knownSheetIds = _sessionState.KnownSheetIds;
                foreach (var sheet in sheets)
                {
                    bool isSelected = applySavedSelection
                        ? ResolveInitialSelection(sheet.UniqueId, savedSheetIds, knownSheetIds)
                        : true;
                    var item = new SheetItemViewModel(sheet, isSelected);
                    item.PropertyChanged += OnItemSelectionChanged;
                    Sheets.Add(item);
                }
            }
            PreserveSheetOrder = preserveOrder;
            VisibleSheets.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ApplySourceModeSelection(bool isInitialRestore = false, bool autoDefaultSelection = false)
        {
            if (_isApplyingSourceMode) return;
            _isApplyingSourceMode = true;
            try
            {
                bool restoreSelection = isInitialRestore && _sessionState.HasSavedState;

                if (SelectedSourceMode == SheetSelectionSourceMode.AllSheets)
                {
                    if (restoreSelection)
                    {
                        PreserveSheetOrder = false;
                        VisibleSheets.Refresh();
                    }
                    else
                    {
                        PopulateSheets(_allProjectSheets, preserveOrder: false, applySavedSelection: false);
                    }
                }
                else if (SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet)
                {
                    if (autoDefaultSelection && SelectedPrintSet == null && string.IsNullOrEmpty(SelectedPrintSetName) && PrintSets.Count > 0)
                    {
                        _selectedPrintSet = PrintSets[0];
                        _selectedPrintSetName = PrintSets[0].Name;
                        OnPropertyChanged(nameof(SelectedPrintSet));
                        OnPropertyChanged(nameof(SelectedPrintSetName));
                    }

                    if (SelectedPrintSet != null)
                    {
                        OnPrintSetSelectionChanged(applySavedSelection: restoreSelection);
                    }
                    else if (!string.IsNullOrEmpty(SelectedPrintSetName) && PrintSetSheetMap.TryGetValue(SelectedPrintSetName!, out var validIds))
                    {
                        ApplySelectionFromSheetIds(validIds);
                    }
                    else if (!isInitialRestore)
                    {
                        PopulateSheets(_allProjectSheets, preserveOrder: false, applySavedSelection: false);
                    }
                }
                else if (SelectedSourceMode == SheetSelectionSourceMode.ViewSchedule)
                {
                    if (autoDefaultSelection && SelectedSchedule == null && string.IsNullOrEmpty(SelectedScheduleName))
                    {
                        SheetIndexScheduleModel? firstSchedule = Schedules.FirstOrDefault(s => !ReferenceEquals(s, _allSheetsOption) && s.Sheets.Count > 0);
                        if (firstSchedule != null)
                        {
                            _selectedSchedule = firstSchedule;
                            _selectedScheduleName = firstSchedule.Name;
                            OnPropertyChanged(nameof(SelectedSchedule));
                            OnPropertyChanged(nameof(SelectedScheduleName));
                        }
                    }

                    if (SelectedSchedule != null && !ReferenceEquals(SelectedSchedule, _allSheetsOption) && SelectedSchedule.Sheets.Count > 0)
                    {
                        OnScheduleSelectionChanged(applySavedSelection: restoreSelection);
                    }
                    else if (!string.IsNullOrEmpty(SelectedScheduleName) && ScheduleSheetMap.TryGetValue(SelectedScheduleName!, out var validIds))
                    {
                        ApplySelectionFromSheetIds(validIds);
                    }
                    else if (!isInitialRestore)
                    {
                        PopulateSheets(_allProjectSheets, preserveOrder: false, applySavedSelection: false);
                    }
                }
            }
            finally
            {
                _isApplyingSourceMode = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void ApplySelectionFromSheetIds(IEnumerable<string> validIds)
        {
            var set = new HashSet<string>(validIds, StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in Sheets)
            {
                sheet.IsSelected = set.Contains(sheet.Model.UniqueId);
            }
        }

        private void SyncSelectedPrintSetFromName(bool notifyPropertyChanged)
        {
            SheetIndexPrintSetModel? matched = FindPrintSetByName(_selectedPrintSetName);
            if (!Equals(_selectedPrintSet, matched))
            {
                _selectedPrintSet = matched;
                if (notifyPropertyChanged)
                {
                    OnPropertyChanged(nameof(SelectedPrintSet));
                }
            }
        }

        private void SyncSelectedScheduleFromName(bool notifyPropertyChanged)
        {
            SheetIndexScheduleModel? matched = FindScheduleByName(_selectedScheduleName);
            if (!Equals(_selectedSchedule, matched))
            {
                _selectedSchedule = matched;
                if (notifyPropertyChanged)
                {
                    OnPropertyChanged(nameof(SelectedSchedule));
                }
            }
        }

        private static T? FindByNameExactOrIgnoreCase<T>(
            IEnumerable<T> items,
            string? name,
            Func<T, string> nameSelector) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            T? caseInsensitiveMatch = null;
            foreach (T item in items)
            {
                string itemName = nameSelector(item);
                if (string.Equals(itemName, name, StringComparison.Ordinal))
                {
                    return item;
                }

                if (caseInsensitiveMatch == null && string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = item;
                }
            }

            return caseInsensitiveMatch;
        }

        private SheetIndexPrintSetModel? FindPrintSetByName(string? name) =>
            FindByNameExactOrIgnoreCase(PrintSets, name, p => p.Name);

        private SheetIndexScheduleModel? FindScheduleByName(string? name) =>
            FindByNameExactOrIgnoreCase(
                Schedules.Where(s => !ReferenceEquals(s, _allSheetsOption)),
                name,
                s => s.Name);

        public List<SheetIndexSheetModel> GetSelectedSheets()
        {
            return Sheets.Where(s => s.IsSelected).Select(s => s.Model).ToList();
        }

        public List<SheetIndexRevisionModel> GetSelectedRevisions()
        {
            return Revisions.Where(r => r.IsSelected).Select(r => r.Model).ToList();
        }

        private bool FilterSheetItem(object item)
        {
            if (!(item is SheetItemViewModel sheetVM))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string term = SearchText.Trim();
            return sheetVM.SheetNumber.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   sheetVM.SheetName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetVisibleSheetsSelected(bool isSelected)
        {
            foreach (var item in VisibleSheets.Cast<SheetItemViewModel>())
            {
                item.IsSelected = isSelected;
            }
        }

        private void SetRevisionsSelected(bool isSelected)
        {
            foreach (var item in Revisions)
            {
                item.IsSelected = isSelected;
            }
        }

        private bool CanExecuteExport()
        {
            return Sheets.Any(s => s.IsSelected) && Revisions.Any(r => r.IsSelected);
        }

        private void ExecuteExport()
        {
            var selectedSheetIds = Sheets.Where(s => s.IsSelected).Select(s => s.Model.UniqueId);
            var selectedRevisionIds = Revisions.Where(r => r.IsSelected).Select(r => r.Model.UniqueId);
            var knownSheetIds = _allProjectSheets.Select(s => s.UniqueId);
            var knownRevisionIds = Revisions.Select(r => r.Model.UniqueId);

            _sessionState.SaveState(
                SelectedSourceMode,
                SelectedPrintSetName,
                SelectedScheduleName,
                selectedRevisionIds,
                selectedSheetIds,
                knownRevisionIds,
                knownSheetIds);

            DialogResult = true;
            RequestClose?.Invoke();
        }

        private void ExecuteCancel()
        {
            DialogResult = false;
            RequestClose?.Invoke();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
