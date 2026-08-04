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
    public class ExportSheetIndexViewModel : INotifyPropertyChanged
    {
        private List<SheetIndexSheetModel> _allProjectSheets = new List<SheetIndexSheetModel>();
        private SheetIndexScheduleModel? _selectedSchedule;
        private bool _preserveSheetOrder;

        public ObservableCollection<SheetItemViewModel> Sheets { get; } = new ObservableCollection<SheetItemViewModel>();
        public ObservableCollection<RevisionItemViewModel> Revisions { get; } = new ObservableCollection<RevisionItemViewModel>();
        public ObservableCollection<SheetIndexScheduleModel> Schedules { get; } = new ObservableCollection<SheetIndexScheduleModel>();

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
            SelectAllSheetsCommand = new RelayCommand(_ => SetSheetsSelected(true));
            DeselectAllSheetsCommand = new RelayCommand(_ => SetSheetsSelected(false));
            SelectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(true));
            DeselectAllRevisionsCommand = new RelayCommand(_ => SetRevisionsSelected(false));
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => CanExecuteExport());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());
        }

        public ExportSheetIndexViewModel(
            IEnumerable<SheetIndexSheetModel> sheets,
            IEnumerable<SheetIndexRevisionModel> revisions,
            IEnumerable<SheetIndexScheduleModel>? schedules = null)
            : this()
        {
            LoadData(sheets, revisions, schedules);
        }

        public void LoadData(
            IEnumerable<SheetIndexSheetModel> sheets,
            IEnumerable<SheetIndexRevisionModel> revisions,
            IEnumerable<SheetIndexScheduleModel>? schedules = null)
        {
            _allProjectSheets = sheets != null ? sheets.ToList() : new List<SheetIndexSheetModel>();

            Schedules.Clear();
            if (schedules != null)
            {
                foreach (var schedule in schedules)
                {
                    Schedules.Add(schedule);
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

            // Default to all sheets
            PopulateSheets(_allProjectSheets, preserveOrder: false);
        }

        private void OnScheduleSelectionChanged()
        {
            if (SelectedSchedule != null && SelectedSchedule.Sheets.Count > 0)
            {
                PopulateSheets(SelectedSchedule.Sheets, preserveOrder: true);
            }
            else
            {
                PopulateSheets(_allProjectSheets, preserveOrder: false);
            }
        }

        private void PopulateSheets(IEnumerable<SheetIndexSheetModel> sheets, bool preserveOrder)
        {
            Sheets.Clear();
            if (sheets != null)
            {
                foreach (var sheet in sheets)
                {
                    Sheets.Add(new SheetItemViewModel(sheet));
                }
            }
            PreserveSheetOrder = preserveOrder;
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
