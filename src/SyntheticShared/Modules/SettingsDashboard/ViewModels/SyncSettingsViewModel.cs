using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Sync Configuration and Link settings.
    /// </summary>
    public class SyncSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private SyncSettings _settings;
        private bool _isOverridden;
        private readonly IFileDialogService _fileDialogService;
        private readonly IUserPromptService _userPromptService;

        /// <inheritdoc/>
        public string ModuleName => "Sync & Link";

        private bool _isDirty;

        /// <inheritdoc/>
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsOverridden
        {
            get => _isOverridden;
            set
            {
                if (SetProperty(ref _isOverridden, value))
                {
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public bool IsValid
        {
            get
            {
                if (!IsOverridden) return true;
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (!IsOverridden)
                {
                    return "Sync Settings are disabled by default. Enable overrides to link this project to an external configuration file.";
                }

                if (string.IsNullOrEmpty(_settings.LinkedFilePath))
                {
                    return "Overridden for Project:\nLinked File: (Not configured yet)";
                }

                return $"Overridden for Project:\nLinked File: {_settings.LinkedFilePath}";
            }
        }

        /// <summary>
        /// Gets or sets the path to the linked settings configuration JSON file.
        /// </summary>
        public string LinkedFilePath
        {
            get => _settings.LinkedFilePath;
            set
            {
                if (_settings != null && _settings.LinkedFilePath != value)
                {
                    _settings.LinkedFilePath = value;
                    OnPropertyChanged(nameof(LinkedFilePath));
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the link file command.
        /// </summary>
        public ICommand LinkFileCommand { get; }

        /// <summary>
        /// Gets the export settings command.
        /// </summary>
        public ICommand ExportCommand { get; }

        /// <summary>
        /// Gets the import settings command.
        /// </summary>
        public ICommand ImportCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public SyncSettingsViewModel(Document doc, IntPtr mainWindowHandle, IFileDialogService? fileDialogService = null, IUserPromptService? userPromptService = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();
            _userPromptService = userPromptService ?? new WindowsUserPromptService();

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<SyncSettings>(_doc) ?? new SyncSettings();

            // Determine if overridden in document extensible storage
            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + SyncSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            LinkFileCommand = new RelayCommand(OnLinkFile, _ => IsOverridden);
            ExportCommand = new RelayCommand(OnExport);
            ImportCommand = new RelayCommand(OnImport);
        }

        private void OnLinkFile(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Select Linked Settings JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                LinkedFilePath = filePath;
                IsDirty = true;
            }
        }

        private void OnExport(object parameter)
        {
            var filePath = _fileDialogService.SaveFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Export Settings to JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    SettingsManager.ExportAllToFile(_doc, filePath);
                    _userPromptService.ShowMessage("Successfully exported active project settings.", "Export Succeeded");
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to export settings: {ex.Message}", "Export Failed");
                }
            }
        }

        private void OnImport(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Import Settings from JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    SettingsManager.ImportAllFromFile(_doc, filePath);

                    // Refresh all ViewModels in the Dashboard
                    if (parameter is Window dashboardWindow)
                    {
                        if (dashboardWindow.DataContext is SettingsDashboardViewModel dashboardVM)
                        {
                            foreach (var module in dashboardVM.SettingModules)
                            {
                                module.Reload();
                                module.IsDirty = false;
                            }
                        }
                    }
                    else
                    {
                        Reload();
                        IsDirty = false;
                    }

                    _userPromptService.ShowMessage("Successfully imported project settings. Dashboard UI has been updated.", "Import Succeeded");
                }
                catch (Exception ex)
                {
                    _userPromptService.ShowMessage($"Failed to import settings: {ex.Message}", "Import Failed");
                }
            }
        }

        /// <inheritdoc/>
        public void Save()
        {
            if (IsOverridden)
            {
                SettingsManager.Save(_doc, _settings);
            }
            else
            {
                SettingsManager.Delete(_doc, SyncSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<SyncSettings>(_doc) ?? new SyncSettings();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + SyncSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false; // Reset to clean state on reload

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(LinkedFilePath));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
