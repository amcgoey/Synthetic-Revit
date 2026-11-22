using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Material Library configuration settings.
    /// </summary>
    public class MaterialLibraryViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private MaterialLibrarySettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Material Library";

        /// <inheritdoc/>
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
                    var defaultSettings = new MaterialLibrarySettings().Defaults();
                    try
                     {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(MaterialLibrarySettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<MaterialLibrarySettings>(MaterialLibrarySettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nLibrary Directory: {defaultSettings.LibraryFolderPath}";
                }

                return $"Overridden for Project:\nLibrary Directory: {_settings.LibraryFolderPath}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialLibraryViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public MaterialLibraryViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<MaterialLibrarySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + MaterialLibrarySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            string initialPath = _settings.LibraryFolderPath;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                initialPath = "C:\\Materials\\";
            }

            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Material Library Folder", initialPath);
            if (selectedPath != null)
            {

                _settings.LibraryFolderPath = selectedPath;
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
                SettingsManager.Delete(_doc, MaterialLibrarySettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<MaterialLibrarySettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + MaterialLibrarySettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
