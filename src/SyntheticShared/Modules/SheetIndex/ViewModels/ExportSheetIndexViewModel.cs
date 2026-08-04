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

        public ObservableCollection<SheetItemViewModel> Sheets { get; } = new ObservableCollection<SheetItemViewModel>();
        public ObservableCollection<RevisionItemViewModel> Revisions { get; } = new ObservableCollection<RevisionItemViewModel>();

        public ICollectionView VisibleSheets { get; }

        public List<SheetSelectionSourceMode> AvailableSourceModes { get; } = new List<SheetSelectionSourceMode>
        {
            SheetSelectionSourceMode.AllSheets,
            SheetSelectionSourceMode.ViewSheetSet,
            SheetSelectionSourceMode.ViewSchedule
        };

        public ObservableCollection<string> AvailablePrintSets { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableSchedules { get; } = new ObservableCollection<string>();

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
                }
            }
        }

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
            IEnumerable<string>? printSets = null,
            IEnumerable<string>? schedules = null)
            : this()
        {
            if (printSets != null)
            {
                foreach (var ps in printSets)
                {
                    AvailablePrintSets.Add(ps);
                }
            }

            if (schedules != null)
            {
                foreach (var sch in schedules)
                {
                    AvailableSchedules.Add(sch);
                }
            }

            LoadData(sheets, revisions);
        }

        public void LoadData(IEnumerable<SheetIndexSheetModel> sheets, IEnumerable<SheetIndexRevisionModel> revisions)
        {
            Sheets.Clear();
            if (sheets != null)
            {
                foreach (var sheet in sheets)
                {
                    bool isSelected = true;
                    if (SheetIndexSessionState.HasSavedState && SheetIndexSessionState.SelectedSheetIds != null)
                    {
                        isSelected = SheetIndexSessionState.SelectedSheetIds.Contains(sheet.UniqueId);
                    }
                    Sheets.Add(new SheetItemViewModel(sheet, isSelected));
                }
            }

            Revisions.Clear();
            if (revisions != null)
            {
                foreach (var rev in revisions)
                {
                    bool isSelected = true;
                    if (SheetIndexSessionState.HasSavedState && SheetIndexSessionState.SelectedRevisionIds != null)
                    {
                        isSelected = SheetIndexSessionState.SelectedRevisionIds.Contains(rev.UniqueId);
                    }
                    Revisions.Add(new RevisionItemViewModel(rev, isSelected));
                }
            }

            if (SheetIndexSessionState.HasSavedState)
            {
                SelectedSourceMode = SheetIndexSessionState.SelectedSourceMode;
                SelectedPrintSetName = SheetIndexSessionState.SelectedPrintSetName;
                SelectedScheduleName = SheetIndexSessionState.SelectedScheduleName;
            }
            else
            {
                if (AvailablePrintSets.Count > 0 && string.IsNullOrEmpty(SelectedPrintSetName))
                {
                    SelectedPrintSetName = AvailablePrintSets[0];
                }
                if (AvailableSchedules.Count > 0 && string.IsNullOrEmpty(SelectedScheduleName))
                {
                    SelectedScheduleName = AvailableSchedules[0];
                }
            }

            VisibleSheets.Refresh();
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

