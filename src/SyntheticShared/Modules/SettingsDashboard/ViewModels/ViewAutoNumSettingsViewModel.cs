using System;
using System.IO;
using System.Linq;
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
using Synthetic.Modules.SettingsDashboard.Views;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for managing View Auto-Numbering configuration settings.
    /// </summary>
    public class ViewAutoNumSettingsViewModel : ViewModelBase, ISettingModuleViewModel
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ViewAutoNumSettings _settings;
        private bool _isOverridden;

        /// <inheritdoc/>
        public string ModuleName => "View Auto-Numbering";

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
                    // Fetch default/firm settings from disk to display in summary
                    var defaultSettings = new ViewAutoNumSettings().Defaults();
                    try
                    {
                        string defaultPath = Path.Combine(Config.addinPath, "SyntheticSettings.json");
                        if (File.Exists(defaultPath))
                        {
                            Config? defaultConfig = Config.ReadFromFile(defaultPath);
                            if (defaultConfig != null && defaultConfig.Contains(ViewAutoNumSettings.Name))
                            {
                                defaultSettings = defaultConfig.GetSettings<ViewAutoNumSettings>(ViewAutoNumSettings.Name);
                            }
                        }
                    }
                    catch { }
                    return $"Using Firmwide Defaults:\nFamily: {defaultSettings.ViewAutoNumFamily}\nType: {defaultSettings.ViewAutoNumFamilyType}\nGrid X Param: {defaultSettings.ViewAutoNumXGridName}\nGrid Y Param: {defaultSettings.ViewAutoNumYGridName}";
                }

                return $"Overridden for Project:\nFamily: {_settings.ViewAutoNumFamily}\nType: {_settings.ViewAutoNumFamilyType}\nGrid X Param: {_settings.ViewAutoNumXGridName}\nGrid Y Param: {_settings.ViewAutoNumYGridName}";
            }
        }

        /// <inheritdoc/>
        public ICommand ConfigureCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumSettingsViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit document.</param>
        /// <param name="mainWindowHandle">The Revit main window handle.</param>
        public ViewAutoNumSettingsViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            // Load settings using SettingsManager
            _settings = SettingsManager.Get<ViewAutoNumSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ViewAutoNumSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));

            ConfigureCommand = new RelayCommand(OnConfigure, _ => IsOverridden);
        }

        private void OnConfigure(object parameter)
        {

            // Draft vs Commit pattern: Clone settings before opening wizard
            var draftSettings = new ViewAutoNumSettings(
                _settings.ViewAutoNumFamily, 
                _settings.ViewAutoNumFamilyType, 
                _settings.ViewAutoNumXGridName, 
                _settings.ViewAutoNumYGridName
            );
            var wizardVM = new ViewAutoNumWizardViewModel(_doc, draftSettings);
            var wizardWindow = new ViewAutoNumWizardWindow(_mainWindowHandle)
            {
                DataContext = wizardVM
            };

            var dialogResult = wizardWindow.ShowDialog();



            if (dialogResult == true)
            {


                // Commit settings from wizard
                _settings.ViewAutoNumFamily = wizardVM.SelectedFamily;
                _settings.ViewAutoNumFamilyType = wizardVM.SelectedType;
                _settings.ViewAutoNumXGridName = wizardVM.XGridName;
                _settings.ViewAutoNumYGridName = wizardVM.YGridName;

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
                SettingsManager.Delete(_doc, ViewAutoNumSettings.Name);
            }
        }

        /// <inheritdoc/>
        public void Reload()
        {
            _settings = SettingsManager.Get<ViewAutoNumSettings>(_doc);

            var dataStorages = new FilteredElementCollector(_doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();
            string storageName = "Synthetic_" + ViewAutoNumSettings.Name;
            _isOverridden = dataStorages.Any(ds => ds.Name.Equals(storageName, StringComparison.OrdinalIgnoreCase));
            _isDirty = false; // Reset to clean state on reload

            OnPropertyChanged(nameof(IsOverridden));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsValid));
        }
    }
}
