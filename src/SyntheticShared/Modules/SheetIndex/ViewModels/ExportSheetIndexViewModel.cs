using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Modules.SheetIndex.Services;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SheetIndex.ViewModels
{
    public class ExportSheetIndexViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private SheetSelectionSourceMode _selectedSourceMode = SheetSelectionSourceMode.AllSheets;
        private string? _selectedPrintSetName;
        private string? _selectedScheduleName;
        private bool _isApplyingSourceMode;
        private List<SheetIndexSheetModel> _allProjectSheets = new List<SheetIndexSheetModel>();
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
                if (_selectedSchedule != value)
                {
                    _selectedSchedule = value;
                    OnPropertyChanged();
                    OnScheduleSelectionChanged();
                }
            }
        }

        public SheetIndexPrintSetModel? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                if (_selectedPrintSet != value)
                {
                    _selectedPrintSet = value;
                    OnPropertyChanged();
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

        public Dictionary<string, List<string>> PrintSetSheetMap { get; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> ScheduleSheetMap { get; } = new Dictionary<string, List<string>>();

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
                    OnPropertyChanged(nameof(IsPrintSetSource));
                    OnPropertyChanged(nameof(IsScheduleSource));
                    OnPropertyChanged(nameof(IsPrintSetSourceSelected));
                    ApplySourceModeSelection();
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
                if (_selectedPrintSetName != value)
                {
                    _selectedPrintSetName = value;
                    OnPropertyChanged();
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
                if (_selectedScheduleName != value)
                {
                    _selectedScheduleName = value;
                    OnPropertyChanged();
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

        public ExportSheetIndexViewModel()
        {
            VisibleSheets = CollectionViewSource.GetDefaultView(Sheets);
            VisibleSheets.Filter = FilterSheetItem;

            SelectAllSheetsCommand = new RelayCommand(_ => SetVisibleSheetsSelected(true));
            DeselectAllSheetsCommand = new RelayCommand(_ => SetVisibleSheetsSelected(false));
            SelectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(true));
            DeselectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(false));
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExecuteExport());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());
        }

        public ExportSheetIndexViewModel(
            IEnumerable<SheetIndexSheetModel> sheets,
            IEnumerable<SheetIndexRevisionModel> revisions,
            IEnumerable<SheetIndexPrintSetModel> printSetModels)
            : this(sheets, revisions, null, null, null, null, null, printSetModels)
        {
        }

        public ExportSheetIndexViewModel(
            IEnumerable<SheetIndexSheetModel> sheets,
            IEnumerable<SheetIndexRevisionModel> revisions,
            IEnumerable<string>? printSets = null,
            IEnumerable<string>? schedules = null,
            Dictionary<string, List<string>>? printSetSheetMap = null,
            Dictionary<string, List<string>>? scheduleSheetMap = null,
            IEnumerable<SheetIndexScheduleModel>? scheduleModels = null,
            IEnumerable<SheetIndexPrintSetModel>? printSetModels = null)
            : this()
        {
            if (printSets != null)
            {
                foreach (var ps in printSets) AvailablePrintSets.Add(ps);
            }

            if (schedules != null)
            {
                foreach (var sch in schedules) AvailableSchedules.Add(sch);
            }

            if (printSetSheetMap != null)
            {
                foreach (var kvp in printSetSheetMap) PrintSetSheetMap[kvp.Key] = kvp.Value;
            }

            if (scheduleSheetMap != null)
            {
                foreach (var kvp in scheduleSheetMap) ScheduleSheetMap[kvp.Key] = kvp.Value;
            }

            if (printSetModels != null)
            {
                foreach (var ps in printSetModels) PrintSets.Add(ps);
            }

            LoadData(sheets, revisions, scheduleModels);
        }

        public void LoadData(
            IEnumerable<SheetIndexSheetModel> sheets,
            IEnumerable<SheetIndexRevisionModel> revisions,
            IEnumerable<SheetIndexScheduleModel>? schedules = null)
        {
            foreach (var s in Sheets) s.PropertyChanged -= OnItemSelectionChanged;
            foreach (var r in Revisions) r.PropertyChanged -= OnItemSelectionChanged;

            _allProjectSheets = sheets != null ? sheets.ToList() : new List<SheetIndexSheetModel>();

            Schedules.Clear();
            var allSheetsOption = new SheetIndexScheduleModel(string.Empty, "<All Project Sheets>", _allProjectSheets);
            Schedules.Add(allSheetsOption);

            if (schedules != null)
            {
                foreach (var schedule in schedules)
                {
                    Schedules.Add(schedule);
                }
            }

            Sheets.Clear();
            foreach (var sheet in _allProjectSheets)
            {
                bool isSelected = ResolveInitialSelection(sheet.UniqueId, SheetIndexSessionState.SelectedSheetIds);
                var item = new SheetItemViewModel(sheet, isSelected);
                item.PropertyChanged += OnItemSelectionChanged;
                Sheets.Add(item);
            }

            Revisions.Clear();
            if (revisions != null)
            {
                foreach (var rev in revisions)
                {
                    bool isSelected = ResolveInitialSelection(rev.UniqueId, SheetIndexSessionState.SelectedRevisionIds);
                    var item = new RevisionItemViewModel(rev, isSelected);
                    item.PropertyChanged += OnItemSelectionChanged;
                    Revisions.Add(item);
                }
            }

            if (SheetIndexSessionState.HasSavedState)
            {
                _selectedSourceMode = SheetIndexSessionState.SelectedSourceMode;
                _selectedPrintSetName = SheetIndexSessionState.SelectedPrintSetName;
                _selectedScheduleName = SheetIndexSessionState.SelectedScheduleName;
            }
            else
            {
                if (AvailablePrintSets.Count > 0 && string.IsNullOrEmpty(_selectedPrintSetName))
                {
                    _selectedPrintSetName = AvailablePrintSets[0];
                }
                if (AvailableSchedules.Count > 0 && string.IsNullOrEmpty(_selectedScheduleName))
                {
                    _selectedScheduleName = AvailableSchedules[0];
                }
            }

            OnPropertyChanged(nameof(SelectedSourceMode));
            OnPropertyChanged(nameof(SelectedPrintSetName));
            OnPropertyChanged(nameof(SelectedScheduleName));
            OnPropertyChanged(nameof(IsPrintSetSource));
            OnPropertyChanged(nameof(IsScheduleSource));
            OnPropertyChanged(nameof(IsPrintSetSourceSelected));

            VisibleSheets.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private bool ResolveInitialSelection(string id, HashSet<string>? savedIds)
        {
            if (!SheetIndexSessionState.HasSavedState || savedIds == null)
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

        private void OnScheduleSelectionChanged()
        {
            if (SelectedSchedule != null && !string.IsNullOrEmpty(SelectedSchedule.UniqueId) && SelectedSchedule.Sheets.Count > 0)
            {
                PopulateSheets(SelectedSchedule.Sheets, preserveOrder: true);
            }
            else
            {
                PopulateSheets(_allProjectSheets, preserveOrder: false);
            }
        }

        private void OnPrintSetSelectionChanged()
        {
            if (SelectedPrintSet != null && SelectedPrintSet.SheetIds.Count > 0)
            {
                var sheetMap = _allProjectSheets.ToDictionary(s => s.UniqueId, StringComparer.OrdinalIgnoreCase);
                var orderedSheets = new List<SheetIndexSheetModel>();

                for (int i = 0; i < SelectedPrintSet.SheetIds.Count; i++)
                {
                    string id = SelectedPrintSet.SheetIds[i];
                    if (sheetMap.TryGetValue(id, out var sheet))
                    {
                        orderedSheets.Add(new SheetIndexSheetModel(sheet.UniqueId, sheet.SheetNumber, sheet.SheetName, sheet.RevisionIds, printOrderIndex: i));
                    }
                }

                PopulateSheets(orderedSheets, preserveOrder: false);
            }
            else
            {
                PopulateSheets(_allProjectSheets, preserveOrder: false);
            }
        }

        private void PopulateSheets(IEnumerable<SheetIndexSheetModel> sheets, bool preserveOrder)
        {
            foreach (var s in Sheets) s.PropertyChanged -= OnItemSelectionChanged;
            Sheets.Clear();
            if (sheets != null)
            {
                foreach (var sheet in sheets)
                {
                    bool isSelected = ResolveInitialSelection(sheet.UniqueId, SheetIndexSessionState.SelectedSheetIds);
                    var item = new SheetItemViewModel(sheet, isSelected);
                    item.PropertyChanged += OnItemSelectionChanged;
                    Sheets.Add(item);
                }
            }
            PreserveSheetOrder = preserveOrder;
            VisibleSheets.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ApplySourceModeSelection()
        {
            if (_isApplyingSourceMode) return;
            _isApplyingSourceMode = true;
            try
            {
                if (SelectedSourceMode == SheetSelectionSourceMode.AllSheets)
                {
                    PopulateSheets(_allProjectSheets, preserveOrder: false);
                    foreach (var sheet in Sheets)
                    {
                        sheet.IsSelected = true;
                    }
                }
                else if (SelectedSourceMode == SheetSelectionSourceMode.ViewSheetSet)
                {
                    if (SelectedPrintSet == null && PrintSets.Count > 0)
                    {
                        SelectedPrintSet = PrintSets[0];
                    }
                    else if (SelectedPrintSet != null)
                    {
                        OnPrintSetSelectionChanged();
                    }
                    else if (!string.IsNullOrEmpty(SelectedPrintSetName) && PrintSetSheetMap.TryGetValue(SelectedPrintSetName, out var validIds))
                    {
                        var set = new HashSet<string>(validIds);
                        foreach (var sheet in Sheets)
                        {
                            sheet.IsSelected = set.Contains(sheet.Model.UniqueId);
                        }
                    }
                }
                else if (SelectedSourceMode == SheetSelectionSourceMode.ViewSchedule)
                {
                    if (!string.IsNullOrEmpty(SelectedScheduleName) && Schedules.Count > 0)
                    {
                        var matchingSchedule = Schedules.FirstOrDefault(s => s.Name == SelectedScheduleName);
                        if (matchingSchedule != null && matchingSchedule.Sheets.Count > 0)
                        {
                            SelectedSchedule = matchingSchedule;
                        }
                        else if (ScheduleSheetMap.TryGetValue(SelectedScheduleName, out var validIds))
                        {
                            var set = new HashSet<string>(validIds);
                            foreach (var sheet in Sheets)
                            {
                                sheet.IsSelected = set.Contains(sheet.Model.UniqueId);
                            }
                        }
                    }
                }
            }
            finally
            {
                _isApplyingSourceMode = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

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

            SheetIndexSessionState.SaveState(
                SelectedSourceMode,
                SelectedPrintSetName,
                SelectedScheduleName,
                selectedRevisionIds,
                selectedSheetIds);

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
