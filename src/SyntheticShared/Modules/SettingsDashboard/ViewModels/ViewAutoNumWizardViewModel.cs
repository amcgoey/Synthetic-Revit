using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the View Autonumbering Configuration Wizard.
    /// </summary>
    public class ViewAutoNumWizardViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private string _selectedFamily = string.Empty;
        private string _selectedType = string.Empty;
        private string _xGridName;
        private string _yGridName;

        /// <summary>
        /// Gets the collection of available annotation families.
        /// </summary>
        public ObservableCollection<string> AvailableFamilies { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets the collection of available types for the selected family.
        /// </summary>
        public ObservableCollection<string> AvailableTypes { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected annotation family name.
        /// </summary>
        public string SelectedFamily
        {
            get => _selectedFamily;
            set
            {
                if (SetProperty(ref _selectedFamily, value))
                {
                    LoadAvailableTypes();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected family type name.
        /// </summary>
        public string SelectedType
        {
            get => _selectedType;
            set => SetProperty(ref _selectedType, value);
        }

        /// <summary>
        /// Gets or sets the X Grid parameter name.
        /// </summary>
        public string XGridName
        {
            get => _xGridName;
            set => SetProperty(ref _xGridName, value);
        }

        /// <summary>
        /// Gets or sets the Y Grid parameter name.
        /// </summary>
        public string YGridName
        {
            get => _yGridName;
            set => SetProperty(ref _yGridName, value);
        }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ViewAutoNumWizardViewModel"/> class.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <param name="settings">The ViewAutoNum settings to edit.</param>
        public ViewAutoNumWizardViewModel(Document doc, ViewAutoNumSettings settings)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _xGridName = settings.ViewAutoNumXGridName;
            _yGridName = settings.ViewAutoNumYGridName;

            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);

            LoadAvailableFamilies(settings.ViewAutoNumFamily, settings.ViewAutoNumFamilyType);
        }

        private void LoadAvailableFamilies(string? targetFamily, string? targetType)
        {
            try
            {
                var families = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null && fs.Category.CategoryType == CategoryType.Annotation)
                    .Select(fs => fs.Family?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();

                AvailableFamilies.Clear();
                foreach (var family in families)
                {
                    AvailableFamilies.Add(family);
                }

                if (targetFamily != null && AvailableFamilies.Contains(targetFamily))
                {
                    SelectedFamily = targetFamily;
                    
                    // Trigger loading types for selected family, then select the target type if it exists
                    LoadAvailableTypes();
                    if (targetType != null && AvailableTypes.Contains(targetType))
                    {
                        SelectedType = targetType;
                    }
                }
                else if (AvailableFamilies.Count > 0)
                {
                    SelectedFamily = AvailableFamilies[0];
                }
            }
            catch (Exception)
            {
                // Gracefully handle any Revit API collection querying exceptions
            }
        }

        private void LoadAvailableTypes()
        {
            AvailableTypes.Clear();
            if (string.IsNullOrEmpty(SelectedFamily)) return;

            try
            {
                var types = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Family != null && fs.Family.Name.Equals(SelectedFamily, StringComparison.OrdinalIgnoreCase))
                    .Select(fs => fs.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList();

                foreach (var type in types)
                {
                    AvailableTypes.Add(type);
                }

                if (AvailableTypes.Count > 0 && string.IsNullOrEmpty(SelectedType))
                {
                    SelectedType = AvailableTypes[0];
                }
            }
            catch (Exception)
            {
                // Gracefully handle Revit API exceptions
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(SelectedFamily) &&
                   !string.IsNullOrEmpty(SelectedType) &&
                   !string.IsNullOrEmpty(XGridName) &&
                   !string.IsNullOrEmpty(YGridName);
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
    }
}
