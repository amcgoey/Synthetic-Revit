using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Collections.Generic;
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
using Synthetic.Modules.SettingsDashboard.Views;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing File Utility and Network Path configuration settings in the Dashboard.
    /// </summary>
    public class FileUtilitySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private FileUtilitySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Firm Network Paths";

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
                FileUtilitySettings displaySettings;
                string prefix;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    // Attempt to load firm settings from SyntheticSettings.json or fallback defaults
                    displaySettings = new FileUtilitySettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(FileUtilitySettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<FileUtilitySettings>(FileUtilitySettings.Name);
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    displaySettings = _settings;
                }

                if (displaySettings == null)
                {
                    return prefix + "(No settings configured)";
                }

                var sb = new StringBuilder(prefix);
                sb.AppendLine("Archive Directories:");
                if (displaySettings.ArchiveDirectories != null && displaySettings.ArchiveDirectories.Count > 0)
                {
                    foreach (var dir in displaySettings.ArchiveDirectories)
                    {
                        sb.AppendLine($"  - {dir}");
                    }
                }
                else
                {
                    sb.AppendLine("  (None)");
                }

                sb.AppendLine("\nAlternate Paths:");
                if (displaySettings.AlternatePaths != null && displaySettings.AlternatePaths.Count > 0)
                {
                    foreach (var kvp in displaySettings.AlternatePaths)
                    {
                        string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;
                        sb.AppendLine($"  - {kvp.Key} -> {mapped}");
                    }
                }
                else
                {
                    sb.AppendLine("  (None)");
                }

                return sb.ToString();
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileUtilitySettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public FileUtilitySettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<FileUtilitySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + FileUtilitySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {
            // Draft vs Commit pattern: Clone settings before opening wizard
            var draftSettings = new FileUtilitySettings
            {
                ArchiveDirectories = _settings.ArchiveDirectories != null ? _settings.ArchiveDirectories.ToList() : new List<string>(),
                AlternatePaths = _settings.AlternatePaths != null 
                    ? _settings.AlternatePaths.ToDictionary(kvp => kvp.Key, kvp => kvp.Value != null ? kvp.Value.ToList() : new List<string>())
                    : new Dictionary<string, List<string>>()
            };

            var wizardVM = new NetworkPathsWizardViewModel(draftSettings, _mainWindowHandle);
            var wizardWindow = new NetworkPathsWizardWindow(_mainWindowHandle)
            {
                DataContext = wizardVM
            };

            var dialogResult = wizardWindow.ShowDialog();

            if (dialogResult == true)
            {
                // Commit settings from wizard
                _settings.ArchiveDirectories = wizardVM.ArchiveDirectories.ToList();
                _settings.AlternatePaths = wizardVM.AlternateMappings.ToDictionary(
                    m => m.OriginalServerPath,
                    m => new List<string> { m.LocalMappedPath }
                );

                IsDirty = true;
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsValid));
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
                SettingsManager.Delete(_doc, FileUtilitySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<FileUtilitySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + FileUtilitySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
