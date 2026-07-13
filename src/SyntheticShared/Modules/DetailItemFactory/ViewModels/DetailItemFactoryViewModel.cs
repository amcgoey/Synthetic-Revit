using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using View = Autodesk.Revit.DB.View;

using Synthetic.Shared.UI;
using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.DetailItemFactory.ViewModels
{
    /// <summary>
    /// ViewModel for the Detail Item Factory WPF dialog.
    /// Provides bindings and validation logic for batch processing Revit model geometry to 2D Detail Items.
    /// Supports per-element configuration of view orientations.
    /// </summary>
    public class DetailItemFactoryViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private readonly View _activeView;
        private readonly IntPtr _mainWindowHandle;

        private string _outputPath = string.Empty;
        private string _selectedSubcategory = "Detail Items";
        private bool _overwriteExisting = true;

        /// <summary>
        /// Gets the collection of elements configured for conversion.
        /// </summary>
        public ObservableCollection<SelectedElementItemViewModel> Elements { get; } = new ObservableCollection<SelectedElementItemViewModel>();

        /// <summary>
        /// Gets or sets the target folder path where family files are saved.
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets the collection of OST_DetailComponents subcategory names available in the document.
        /// </summary>
        public ObservableCollection<string> Subcategories { get; } = new ObservableCollection<string>();

        /// <summary>
        /// Gets or sets the selected subcategory name.
        /// </summary>
        public string SelectedSubcategory
        {
            get => _selectedSubcategory;
            set
            {
                if (SetProperty(ref _selectedSubcategory, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether existing family files should be overwritten on disk.
        /// </summary>
        public bool OverwriteExisting
        {
            get => _overwriteExisting;
            set => SetProperty(ref _overwriteExisting, value);
        }

        /// <summary>
        /// Command to browse for an output folder.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Command to remove an element from the queue.
        /// </summary>
        public ICommand RemoveElementCommand { get; }

        /// <summary>
        /// Command to trigger processing (runs validation and closes window with a success result).
        /// </summary>
        public ICommand RunCommand { get; }

        /// <summary>
        /// Command to abort processing and close the window with a cancelled result.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Delegate assigned by the View to request window closure, passing the dialog result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryViewModel"/> class.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <param name="activeView">The active Revit view.</param>
        /// <param name="selectedIds">The selected element IDs.</param>
        /// <param name="mainWindowHandle">The parent Revit window handle.</param>
        public DetailItemFactoryViewModel(Document doc, View activeView, IEnumerable<ElementId> selectedIds, IntPtr mainWindowHandle)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
            _mainWindowHandle = mainWindowHandle;

            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            // Resolve dynamic default output path based on document status
            string resolvedPath = string.Empty;
            if (string.IsNullOrEmpty(_doc.PathName))
            {
                resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (_doc.IsModelInCloud)
            {
                resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (_doc.IsWorkshared)
            {
                ModelPath centralPath = _doc.GetWorksharingCentralModelPath();
                if (centralPath != null)
                {
                    string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                    if (!string.IsNullOrEmpty(userVisiblePath))
                    {
                        try
                        {
                            resolvedPath = Path.GetDirectoryName(userVisiblePath) ?? string.Empty;
                        }
                        catch
                        {
                            resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        }
                    }
                    else
                    {
                        resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }
                }
                else
                {
                    resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
            else
            {
                try
                {
                    resolvedPath = Path.GetDirectoryName(_doc.PathName) ?? string.Empty;
                }
                catch
                {
                    resolvedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }

            var settings = SettingsManager.Get<DetailItemFactorySettings>(_doc);
            if (settings != null && !string.IsNullOrEmpty(settings.OutputPath))
            {
                string storedPath = settings.OutputPath;
                if (!storedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    storedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = storedPath;
            }
            else if (!string.IsNullOrEmpty(resolvedPath))
            {
                if (!resolvedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    resolvedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = resolvedPath;
            }

            // Wire commands
            BrowseCommand = new RelayCommand(ExecuteBrowse);
            RemoveElementCommand = new RelayCommand(ExecuteRemoveElement);
            RunCommand = new RelayCommand(ExecuteRun, CanExecuteRun);
            CancelCommand = new RelayCommand(ExecuteCancel);

            // Populate Elements collection (filtering for Model elements only)
            foreach (ElementId id in selectedIds)
            {
                Element element = _doc.GetElement(id);
                if (element != null && element.Category != null && element.Category.CategoryType == CategoryType.Model)
                {
                    Elements.Add(new SelectedElementItemViewModel(element, _activeView));
                }
            }

            // Populate OST_DetailComponents subcategories
            Category detailComponentsCategory = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailComponentsCategory != null)
            {
                foreach (Category subCat in detailComponentsCategory.SubCategories)
                {
                    if (!string.IsNullOrEmpty(subCat.Name))
                    {
                        Subcategories.Add(subCat.Name);
                    }
                }
            }

            // Ensure "Detail Items" is in the list
            if (!Subcategories.Contains("Detail Items"))
            {
                Subcategories.Add("Detail Items");
            }

            // Set default SelectedSubcategory with fallback routing
            string targetSubcat = settings?.TargetSubcategory ?? "Detail Items";
            if (string.IsNullOrWhiteSpace(targetSubcat) || !Subcategories.Contains(targetSubcat))
            {
                targetSubcat = "Detail Items";
            }
            SelectedSubcategory = targetSubcat;
        }

        private void ExecuteBrowse(object obj)
        {
            string initialPath = OutputPath;
            string selectedPath = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Output Folder", initialPath);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (!selectedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    selectedPath += Path.DirectorySeparatorChar;
                }
                OutputPath = selectedPath;
            }
        }

        private void ExecuteRemoveElement(object parameter)
        {
            if (parameter is SelectedElementItemViewModel item)
            {
                Elements.Remove(item);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool CanExecuteRun(object obj)
        {
            return Elements.Count > 0 && !string.IsNullOrWhiteSpace(OutputPath) && !string.IsNullOrWhiteSpace(SelectedSubcategory);
        }

        private void ExecuteRun(object obj)
        {
            CloseAction?.Invoke(true);
        }

        private void ExecuteCancel(object obj)
        {
            CloseAction?.Invoke(false);
        }
    }
}
