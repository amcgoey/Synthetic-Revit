using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.SettingsDashboard.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for managing Detail Item Factory configurations in the Settings Dashboard.
    /// </summary>
    public class DetailItemFactorySettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private DetailItemFactorySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;
        private string _outputPath = string.Empty;
        private string _selectedSubcategoryName = string.Empty;

        /// <inheritdoc/>
        public string ModuleName => "Detail Item Factory";

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

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    _settings.OutputPath = value;
                    IsDirty = true;
                    OnPropertyChanged(nameof(SummaryText));
                    OnPropertyChanged(nameof(IsValid));
                }
            }
        }

        /// <summary>
        /// Gets the collection of available subcategory names from BuiltInCategory.OST_DetailComponents.
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
                if (SetProperty(ref _selectedSubcategoryName, value))
                {
                    _settings.TargetSubcategory = value;
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
                string path = string.Empty;
                string subcategory = "Detail Items";

                if (!IsOverridden)
                {
                    prefix = "Using Firmwide Defaults:\n\n";
                    var displaySettings = new DetailItemFactorySettings();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(DetailItemFactorySettings.Name))
                            {
                                displaySettings = defaultConfig.GetSettings<DetailItemFactorySettings>(DetailItemFactorySettings.Name);
                            }
                        }
                    }
                    catch { }
                    path = displaySettings?.OutputPath ?? string.Empty;
                    subcategory = displaySettings?.TargetSubcategory ?? "Detail Items";
                }
                else
                {
                    prefix = "Overridden for Project:\n\n";
                    path = OutputPath;
                    subcategory = SelectedSubcategoryName;
                }

                if (string.IsNullOrWhiteSpace(subcategory))
                {
                    subcategory = "Detail Items";
                }

                string displayPath = string.IsNullOrEmpty(path) ? "(Default Dynamic Document Path)" : path;
                return prefix + $"Output Folder Path:\n{displayPath}\n\nTarget Subcategory:\n{subcategory}";
            }
        }

        /// <inheritdoc/>
        public ICommand? ConfigureCommand => null;

        /// <summary>
        /// Gets the command that allows browsing for an output folder.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public DetailItemFactorySettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            _outputPath = _settings.OutputPath;

            // Harvest subcategories of BuiltInCategory.OST_DetailComponents from the active document
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        AvailableSubcategories.Add(subCat.Name);
                    }
                }
            }

            // Ensure "Detail Items" is in the list
            if (!AvailableSubcategories.Contains("Detail Items"))
            {
                AvailableSubcategories.Add("Detail Items");
            }

            // Set default SelectedSubcategoryName with validation
            string targetSubcat = _settings.TargetSubcategory;
            if (string.IsNullOrWhiteSpace(targetSubcat) || !AvailableSubcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            _selectedSubcategoryName = targetSubcat;

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + DetailItemFactorySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            BrowseCommand = new RelayCommand(OnBrowse, _ => IsOverridden);
        }

        private void OnBrowse(object parameter)
        {
            string initialPath = string.IsNullOrEmpty(OutputPath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : OutputPath;
            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Default Output Folder", initialPath);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                OutputPath = selectedPath;
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
                SettingsManager.Delete(_doc, DetailItemFactorySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            _outputPath = _settings.OutputPath;

            // Reload available subcategories just in case they changed
            AvailableSubcategories.Clear();
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        AvailableSubcategories.Add(subCat.Name);
                    }
                }
            }
            if (!AvailableSubcategories.Contains("Detail Items"))
            {
                AvailableSubcategories.Add("Detail Items");
            }

            string targetSubcat = _settings.TargetSubcategory;
            if (string.IsNullOrWhiteSpace(targetSubcat) || !AvailableSubcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            _selectedSubcategoryName = targetSubcat;

            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(SelectedSubcategoryName));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
