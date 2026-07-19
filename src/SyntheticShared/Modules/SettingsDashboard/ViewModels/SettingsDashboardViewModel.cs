using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.Infrastructure.Persistence;
using Synthetic.Shared.UI;

using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// Main ViewModel for the Unified Settings Dashboard.
    /// </summary>
    public class SettingsDashboardViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly IntPtr _mainWindowHandle;
        private ISettingModuleViewModel? _selectedModule;

        /// <summary>
        /// Gets the list of settings modules.
        /// </summary>
        public ObservableCollection<ISettingModuleViewModel> SettingModules { get; }

        /// <summary>
        /// Gets or sets the selected settings module.
        /// </summary>
        public ISettingModuleViewModel? SelectedModule
        {
            get => _selectedModule;
            set => SetProperty(ref _selectedModule, value);
        }

        /// <summary>
        /// Gets the save command.
        /// </summary>
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Gets the cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsDashboardViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="mainWindowHandle">The main window handle.</param>
        public SettingsDashboardViewModel(Document doc, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _mainWindowHandle = mainWindowHandle;

            SettingModules = new ObservableCollection<ISettingModuleViewModel>
            {
                new WorksetSettingsViewModel(_doc, _mainWindowHandle),
                new ViewAutoNumSettingsViewModel(_doc, _mainWindowHandle),
                new MaterialLibraryViewModel(_doc, _mainWindowHandle),
                new ProjectMaterialsViewModel(_doc, _mainWindowHandle),
                new FileUtilitySettingsViewModel(_doc, _mainWindowHandle),
                new StandardsSettingsViewModel(_doc, _mainWindowHandle),
                new DetailItemFactorySettingsViewModel(_doc, _mainWindowHandle),
                new SyncSettingsViewModel(_doc, _mainWindowHandle)
            };

            foreach (var module in SettingModules)
            {
                module.PropertyChanged += Module_PropertyChanged;
            }

            SelectedModule = SettingModules.FirstOrDefault();

            SaveCommand = new RelayCommand(OnSave, _ => CanSave());
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void Module_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettingModuleViewModel.IsDirty) || e.PropertyName == nameof(ISettingModuleViewModel.IsValid))
            {


                // Safely update the SaveCommand CanExecute state on the UI thread after activation is completed
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }));
            }
        }

        private bool CanSave()
        {
            if (_doc == null || !_doc.IsValidObject)
            {
                return false;
            }

            bool anyDirty = SettingModules.Any(m => m.IsDirty);
            bool allDirtyValid = SettingModules.Where(m => m.IsDirty).All(m => m.IsValid);
            


            return anyDirty && allDirtyValid;
        }

        private void OnSave(object parameter)
        {
            try
            {
                // Revit operations require manual transactions if we make changes.
                // SettingsManager handles transactions internally when needed, so we just run Save() on each module.
                foreach (var module in SettingModules)
                {
                    if (module.IsDirty)
                    {
                        module.Save();
                        module.IsDirty = false;
                    }
                }

                System.Windows.Input.CommandManager.InvalidateRequerySuggested();

                if (parameter is Window window)
                {
                    window.DialogResult = true;
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Settings Error", $"Failed to save configurations: {ex.Message}");
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
    }
}
