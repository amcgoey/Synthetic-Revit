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
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;

namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing Project-specific Material paths.
    /// </summary>
    public class ProjectMaterialsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ProjectMaterialSettings _settings;
        private bool _isOverridden;
        private bool _isDirty;

        /// <inheritdoc/>
        public string ModuleName => "Project Materials";

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
                if (_doc == null || !_doc.IsValidObject)
                {
                    return false;
                }
                if (!IsOverridden)
                {
                    if (_doc.IsModelInCloud)
                    {
                        return false;
                    }
                    return true;
                }
                return _settings != null && _settings.IsValid(_doc);
            }
        }

        /// <inheritdoc/>
        public string SummaryText
        {
            get
            {
                if (_doc == null || !_doc.IsValidObject)
                {
                    return "Unresolved (Document is closed)";
                }
                if (!IsOverridden && _doc.IsModelInCloud)
                {
                    return "⚠️ Cloud model detected.\nYou must override and set an absolute project-specific path for materials.";
                }

                string? resolved = _settings.GetResolvedPath(_doc);
                string? resolvedText = string.IsNullOrEmpty(resolved) ? "Unresolved (Document is unsaved)" : resolved;

                if (!IsOverridden)
                {
                    var defaultSettings = new ProjectMaterialSettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(ProjectMaterialSettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<ProjectMaterialSettings>(ProjectMaterialSettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nDefault Relative Path: {defaultSettings.DefaultRelativePath}\nResolved Path: {resolvedText}";
                }

                return $"Overridden for Project:\nDefault Relative Path: {_settings.DefaultRelativePath}\nOverride Folder Path: {string.Join("", _settings.OverrideFolderPath)}\nResolved Path: {resolvedText}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMaterialsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public ProjectMaterialsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            _settings = SettingsManager.Get<ProjectMaterialSettings>(_doc) ?? new ProjectMaterialSettings().Defaults();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ProjectMaterialSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            string? initialPath = _settings.OverrideFolderPath;
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                initialPath = _settings.GetResolvedPath(_doc);
            }

            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Project Materials Folder", initialPath);
            if (selectedPath != null)
            {

                _settings.OverrideFolderPath = selectedPath;
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
                SettingsManager.Delete(_doc, ProjectMaterialSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<ProjectMaterialSettings>(_doc) ?? new ProjectMaterialSettings().Defaults();

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ProjectMaterialSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false;

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
