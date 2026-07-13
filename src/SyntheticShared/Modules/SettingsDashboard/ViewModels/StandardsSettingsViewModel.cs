using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Standards configuration settings in the Dashboard.
    /// </summary>
    public class StandardsSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private StandardsSettings _settings;
        private bool _isOverridden;
        private bool _isDirty;
        private string _standardsFilePath;
        private readonly IFileDialogService _fileDialogService;

        /// <inheritdoc/>
        public string ModuleName => "Standards";

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
                return !string.IsNullOrEmpty(StandardsFilePath) && 
                       StandardsFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Gets or sets the path to the standards JSON file.
        /// </summary>
        public string StandardsFilePath
        {
            get => _standardsFilePath;
            set
            {
                if (SetProperty(ref _standardsFilePath, value))
                {
                    _settings.StandardsFilePath = value;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                string prefix;
                string filePath = string.Empty;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    var displaySettings = new StandardsSettings();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(StandardsSettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<StandardsSettings>(StandardsSettings.Name);
                            }
                        }
                    }
                    catch { }
                    filePath = displaySettings?.StandardsFilePath ?? string.Empty;
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    filePath = StandardsFilePath;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    return prefix + "(No standards file path configured)";
                }

                return prefix + $"Standards File Path:\n{filePath}";
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the command that allows browsing for a standards JSON file.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public StandardsSettingsViewModel(Document doc, IntPtr mainWindowHandle, IFileDialogService? fileDialogService = null)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            _settings = SettingsManager.Get<StandardsSettings>(_doc);
            _standardsFilePath = _settings.StandardsFilePath;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + StandardsSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            BrowseCommand = new RelayCommand(OnBrowse, _ => IsOverridden);
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON files (*.json)|*.json", "Select Standards File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                StandardsFilePath = filePath;
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
                SettingsManager.Delete(_doc, StandardsSettings.Name);
            }

            // [AG2_TEST_START: StandardsSettingsQA]
            // REVERT_METHOD: To remove, safely delete this entire block.
            Console.WriteLine("Jrn.Directive \"SyntheticQA\", \"SettingsSaved: StandardsFilePath\"");
            // [AG2_TEST_END: StandardsSettingsQA]
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<StandardsSettings>(_doc);
            _standardsFilePath = _settings.StandardsFilePath;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + StandardsSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(StandardsFilePath));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
