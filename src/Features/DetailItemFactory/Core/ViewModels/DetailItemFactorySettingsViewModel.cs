using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.ViewModels
{
    /// <summary>
    /// Settings Dashboard ViewModel for managing Detail Item Factory defaults
    /// with deep-clone edit isolation and pure injected delegates.
    /// </summary>
    public class DetailItemFactorySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Func<DetailItemFactorySettings> _loadSettings;
        private readonly Action<DetailItemFactorySettings> _saveSettings;
        private readonly Action _deleteSettings;
        private readonly Func<bool> _isOverriddenProvider;
        private readonly Func<IReadOnlyList<string>> _getAvailableSubcategories;
        private readonly IFolderDialogService? _folderDialogService;
        private readonly Func<DetailItemFactorySettings> _loadFirmwideDefaults;

        private DetailItemFactorySettings _settings = new DetailItemFactorySettings().Defaults();
        private DetailItemFactorySettings _firmwideDefaults = new DetailItemFactorySettings().Defaults();
        private bool _isOverridden;
        private bool _isDirty;
        private string _outputPath = string.Empty;
        private string _selectedSubcategoryName = DetailItemFactorySettings.DefaultTargetSubcategory;

        /// <summary>
        /// Initializes a new headless instance of the <see cref="DetailItemFactorySettingsViewModel"/> class
        /// using injected persistence, subcategory harvesting, and folder dialog services.
        /// </summary>
        public DetailItemFactorySettingsViewModel(
            Func<DetailItemFactorySettings> loadSettings,
            Action<DetailItemFactorySettings> saveSettings,
            Action deleteSettings,
            Func<bool> isOverriddenProvider,
            Func<IReadOnlyList<string>> getAvailableSubcategories,
            IFolderDialogService? folderDialogService = null,
            Func<DetailItemFactorySettings>? loadFirmwideDefaults = null)
        {
            _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
            _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
            _deleteSettings = deleteSettings ?? throw new ArgumentNullException(nameof(deleteSettings));
            _isOverriddenProvider = isOverriddenProvider ?? throw new ArgumentNullException(nameof(isOverriddenProvider));
            _getAvailableSubcategories = getAvailableSubcategories ?? throw new ArgumentNullException(nameof(getAvailableSubcategories));
            _folderDialogService = folderDialogService;
            _loadFirmwideDefaults = loadFirmwideDefaults ?? LoadFirmwideDisplaySettings;

            BrowseCommand = new RelayCommand(_ => BrowseOutputFolder(), _ => IsOverridden);

            Reload();
        }

        /// <inheritdoc />
        public string ModuleName => "Detail Item Factory";

        /// <summary>
        /// Gets the unique settings module key (<c>"DetailItemFactorySettings"</c>).
        /// </summary>
        public string ModuleKey => DetailItemFactorySettings.Name;

        /// <inheritdoc />
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc />
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
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <inheritdoc />
        public bool IsValid
        {
            get
            {
                if (!IsOverridden)
                {
                    return true;
                }

                return _settings != null && _settings.IsValid();
            }
        }

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _outputPath, normalized))
                {
                    _settings.OutputPath = normalized;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <summary>
        /// Gets the collection of available subcategory names from <c>OST_DetailComponents</c>.
        /// </summary>
        public ObservableCollection<string> AvailableSubcategories { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected subcategory name.
        /// </summary>
        public string SelectedSubcategoryName
        {
            get => _selectedSubcategoryName;
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _selectedSubcategoryName, normalized))
                {
                    _settings.TargetSubcategory = normalized;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <inheritdoc />
        public string SummaryText
        {
            get
            {
                string prefix;
                string path;
                string subcategory;

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    path = _firmwideDefaults?.OutputPath ?? string.Empty;
                    subcategory = _firmwideDefaults?.TargetSubcategory ?? DetailItemFactorySettings.DefaultTargetSubcategory;
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    path = OutputPath;
                    subcategory = SelectedSubcategoryName;
                }

                if (string.IsNullOrWhiteSpace(subcategory))
                {
                    subcategory = DetailItemFactorySettings.DefaultTargetSubcategory;
                }

                string displayPath = string.IsNullOrEmpty(path) ? "(Default Dynamic Document Path)" : path;
                return prefix + $"Output Folder Path:\n{displayPath}\n\nTarget Subcategory:\n{subcategory}";
            }
        }

        /// <inheritdoc />
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the command that allows browsing for an output folder.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <inheritdoc />
        public void Save()
        {
            if (IsOverridden)
            {
                _saveSettings(_settings.DeepClone());
            }
            else
            {
                _deleteSettings();
            }
        }

        /// <inheritdoc />
        public void Reload()
        {
            _firmwideDefaults = (_loadFirmwideDefaults() ?? new DetailItemFactorySettings().Defaults()).DeepClone();
            _settings = (_loadSettings() ?? new DetailItemFactorySettings().Defaults()).DeepClone();
            _outputPath = _settings.OutputPath ?? string.Empty;

            RebuildAvailableSubcategories();

            string targetSubcat = (_settings.TargetSubcategory ?? string.Empty).Trim();
            string? matchedSubcat = AvailableSubcategories.FirstOrDefault(
                s => string.Equals(s, targetSubcat, StringComparison.OrdinalIgnoreCase));
            _selectedSubcategoryName = string.IsNullOrWhiteSpace(matchedSubcat)
                ? DetailItemFactorySettings.DefaultTargetSubcategory
                : matchedSubcat!;

            _isOverridden = _isOverriddenProvider();
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(SelectedSubcategoryName));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
            CommandManager.InvalidateRequerySuggested();
        }

        private void RebuildAvailableSubcategories()
        {
            AvailableSubcategories.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<string>? harvested = _getAvailableSubcategories();
            if (harvested != null)
            {
                foreach (string name in harvested)
                {
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    {
                        AvailableSubcategories.Add(name);
                    }
                }
            }

            if (seen.Add(DetailItemFactorySettings.DefaultTargetSubcategory))
            {
                AvailableSubcategories.Add(DetailItemFactorySettings.DefaultTargetSubcategory);
            }
        }

        private void BrowseOutputFolder()
        {
            if (!IsOverridden || _folderDialogService == null)
            {
                return;
            }

            string initialPath = string.IsNullOrEmpty(OutputPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : OutputPath;
            string? selectedPath = _folderDialogService.SelectFolder("Select Default Output Folder", initialPath);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                OutputPath = selectedPath!;
            }
        }

        private static DetailItemFactorySettings LoadFirmwideDisplaySettings()
        {
            var displaySettings = new DetailItemFactorySettings().Defaults();
            try
            {
                string defaultPath = Path.Combine(Config.addinPath, Config.defaultSettingsFile);
                if (File.Exists(defaultPath))
                {
                    Config? defaultConfig = Config.ReadFromFile(defaultPath);
                    if (defaultConfig != null && defaultConfig.Contains(DetailItemFactorySettings.Name))
                    {
                        displaySettings = defaultConfig.GetSettings<DetailItemFactorySettings>(DetailItemFactorySettings.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DetailItemFactorySettingsViewModel: failed to read firmwide defaults: {ex.Message}");
            }

            return displaySettings ?? new DetailItemFactorySettings().Defaults();
        }
    }
}
