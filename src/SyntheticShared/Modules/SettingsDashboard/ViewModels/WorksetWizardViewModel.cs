using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Workset Configuration Wizard.
    /// </summary>
    public class WorksetWizardViewModel : ViewModelBase
    {
        private string? _worksetFile;
        private string? _worksetPath;
        private string? _selectedGroup;
        private bool _isLoading;
        private readonly IFileDialogService _fileDialogService;

        /// <summary>
        /// Gets or sets the workset configuration Excel filename.
        /// </summary>
        public string? WorksetFile
        {
            get => _worksetFile;
            set
            {
                if (SetProperty(ref _worksetFile, value))
                {
                    OnPropertyChanged(nameof(FullPath));
                    TriggerLoadAvailableGroups();
                }
            }
        }

        /// <summary>
        /// Gets or sets the path to the workset Excel file directory.
        /// </summary>
        public string? WorksetPath
        {
            get => _worksetPath;
            set
            {
                if (SetProperty(ref _worksetPath, value))
                {
                    OnPropertyChanged(nameof(FullPath));
                    TriggerLoadAvailableGroups();
                }
            }
        }

        /// <summary>
        /// Gets the full path to the workset configuration Excel file.
        /// </summary>
        public string FullPath
        {
            get
            {
                if (string.IsNullOrEmpty(WorksetFile)) return string.Empty;
                string dir = !string.IsNullOrEmpty(WorksetPath) ? WorksetPath! : Config.addinPath;
                return Path.Combine(dir, WorksetFile);
            }
        }

        /// <summary>
        /// Gets or sets the selected workset group.
        /// </summary>
        public string? SelectedGroup
        {
            get => _selectedGroup;
            set => SetProperty(ref _selectedGroup, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether background worksheet loading is in progress.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        /// <summary>
        /// Gets the collection of available workset groups (worksheet names).
        /// </summary>
        public ObservableCollection<string> AvailableGroups { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets the browse command.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The workset settings to edit.</param>
        public WorksetWizardViewModel(WorksetSettings settings, IFileDialogService? fileDialogService = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _worksetFile = settings.WorksetFile;
            _worksetPath = settings.WorksetPath;
            _selectedGroup = settings.WorksetGroup;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            BrowseCommand = new RelayCommand(OnBrowse);
            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);

            TriggerLoadAvailableGroups();
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("Excel Files (*.xlsx)|*.xlsx", "Select Workset Configuration Excel File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                WorksetPath = Path.GetDirectoryName(filePath);
                WorksetFile = Path.GetFileName(filePath);
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(WorksetFile) && File.Exists(FullPath) && !string.IsNullOrEmpty(SelectedGroup);
        }

        private void OnOk(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }

        private void TriggerLoadAvailableGroups()
        {
            string path = FullPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                AvailableGroups.Clear();
                SelectedGroup = null;
                return;
            }

            IsLoading = true;
            Task.Run(() =>
            {
                List<string>? sheetNames = null;
                try
                {
                    var excel = new Excel(path);
                    sheetNames = excel.WorkSheetNames();
                }
                catch (Exception)
                {
                    // Catch Excel Interop launch failures or file-lock issues gracefully
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableGroups.Clear();
                    if (sheetNames != null)
                    {
                        foreach (var name in sheetNames)
                        {
                            AvailableGroups.Add(name);
                        }
                    }

                    if (_selectedGroup != null && AvailableGroups.Contains(_selectedGroup))
                    {
                        SelectedGroup = _selectedGroup;
                    }
                    else if (AvailableGroups.Count > 0)
                    {
                        SelectedGroup = AvailableGroups[0];
                    }
                    else
                    {
                        SelectedGroup = null;
                    }
                    IsLoading = false;
                });
            });
        }
    }
}
