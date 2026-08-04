using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Synthetic.Modules.SheetIndex.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SheetIndex.ViewModels
{
    public class SheetSelectionSourceOption
    {
        public SheetSelectionSourceType SourceType { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        public SheetSelectionSourceOption(SheetSelectionSourceType sourceType, string displayName)
        {
            SourceType = sourceType;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    public class ExportSheetIndexViewModel : INotifyPropertyChanged
    {
        private readonly List<SheetIndexSheetModel> _allSheets = new List<SheetIndexSheetModel>();
        private readonly List<SheetIndexPrintSetModel> _allPrintSets = new List<SheetIndexPrintSetModel>();

        public ObservableCollection<SheetSelectionSourceOption> SourceOptions { get; } = new ObservableCollection<SheetSelectionSourceOption>
        {
            new SheetSelectionSourceOption(SheetSelectionSourceType.AllSheets, "All Sheets"),
            new SheetSelectionSourceOption(SheetSelectionSourceType.PrintSet, "Print Set")
        };

        public ObservableCollection<SheetIndexPrintSetModel> PrintSets { get; } = new ObservableCollection<SheetIndexPrintSetModel>();

        private SheetSelectionSourceOption _selectedSourceOption;
        public SheetSelectionSourceOption SelectedSourceOption
        {
            get => _selectedSourceOption;
            set
            {
                if (_selectedSourceOption != value && value != null)
                {
                    _selectedSourceOption = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPrintSetSourceSelected));
                    OnPropertyChanged(nameof(SelectedSourceType));
                    ApplySheetSelectionSource();
                }
            }
        }

        public SheetSelectionSourceType SelectedSourceType
        {
            get => SelectedSourceOption?.SourceType ?? SheetSelectionSourceType.AllSheets;
            set
            {
                var option = SourceOptions.FirstOrDefault(o => o.SourceType == value);
                if (option != null)
                {
                    SelectedSourceOption = option;
                }
            }
        }

        private SheetIndexPrintSetModel? _selectedPrintSet;
        public SheetIndexPrintSetModel? SelectedPrintSet
        {
            get => _selectedPrintSet;
            set
            {
                if (_selectedPrintSet != value)
                {
                    _selectedPrintSet = value;
                    OnPropertyChanged();
                    if (IsPrintSetSourceSelected)
                    {
                        ApplySheetSelectionSource();
                    }
                }
            }
        }

        public bool IsPrintSetSourceSelected => SelectedSourceType == SheetSelectionSourceType.PrintSet;

        public ObservableCollection<SheetItemViewModel> Sheets { get; } = new ObservableCollection<SheetItemViewModel>();
        public ObservableCollection<RevisionItemViewModel> Revisions { get; } = new ObservableCollection<RevisionItemViewModel>();

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
            _selectedSourceOption = SourceOptions[0];
            SelectAllSheetsCommand = new RelayCommand(_ => SetSheetsSelected(true));
            DeselectAllSheetsCommand = new RelayCommand(_ => SetSheetsSelected(false));
            SelectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(true));
            DeselectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(false));
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExecuteExport());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());
        }

        public ExportSheetIndexViewModel(IEnumerable<SheetIndexSheetModel> sheets, IEnumerable<SheetIndexRevisionModel> revisions, IEnumerable<SheetIndexPrintSetModel>? printSets = null)
            : this()
        {
            LoadData(sheets, revisions, printSets);
        }

        public void LoadData(IEnumerable<SheetIndexSheetModel> sheets, IEnumerable<SheetIndexRevisionModel> revisions, IEnumerable<SheetIndexPrintSetModel>? printSets = null)
        {
            _allSheets.Clear();
            if (sheets != null)
            {
                _allSheets.AddRange(sheets);
            }

            _allPrintSets.Clear();
            PrintSets.Clear();
            if (printSets != null)
            {
                foreach (var ps in printSets)
                {
                    _allPrintSets.Add(ps);
                    PrintSets.Add(ps);
                }
            }

            Revisions.Clear();
            if (revisions != null)
            {
                foreach (var rev in revisions)
                {
                    Revisions.Add(new RevisionItemViewModel(rev));
                }
            }

            if (_allPrintSets.Count > 0)
            {
                _selectedPrintSet = _allPrintSets[0];
            }

            ApplySheetSelectionSource();
        }

        public void ApplySheetSelectionSource()
        {
            Sheets.Clear();

            if (IsPrintSetSourceSelected && SelectedPrintSet != null)
            {
                var printSetSheetIds = SelectedPrintSet.SheetIds ?? new List<string>();
                var sheetIdOrderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < printSetSheetIds.Count; i++)
                {
                    if (!sheetIdOrderMap.ContainsKey(printSetSheetIds[i]))
                    {
                        sheetIdOrderMap[printSetSheetIds[i]] = i;
                    }
                }

                var matchingSheets = new List<(SheetIndexSheetModel Sheet, int OrderIndex)>();
                foreach (var sheet in _allSheets)
                {
                    if (sheetIdOrderMap.TryGetValue(sheet.UniqueId, out int orderIndex))
                    {
                        sheet.PrintOrderIndex = orderIndex;
                        matchingSheets.Add((sheet, orderIndex));
                    }
                }

                foreach (var item in matchingSheets.OrderBy(x => x.OrderIndex))
                {
                    Sheets.Add(new SheetItemViewModel(item.Sheet) { IsSelected = true });
                }
            }
            else
            {
                // All Sheets source
                foreach (var sheet in _allSheets)
                {
                    sheet.PrintOrderIndex = null;
                    Sheets.Add(new SheetItemViewModel(sheet) { IsSelected = true });
                }
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

        private void SetSheetsSelected(bool isSelected)
        {
            foreach (var item in Sheets)
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
