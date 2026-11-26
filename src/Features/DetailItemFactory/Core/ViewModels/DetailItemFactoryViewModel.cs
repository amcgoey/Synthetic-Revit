using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.ViewModels
{
    /// <summary>
    /// Headless ViewModel for the Detail Item Factory WPF dialog.
    /// Provides bindings, folder browsing, row removal, validation, and batch plan construction.
    /// </summary>
    public class DetailItemFactoryViewModel : ViewModelBase
    {
        private readonly DetailItemSessionSnapshot _snapshot;
        private readonly IFolderDialogService? _folderDialogService;

        private string _outputPath = string.Empty;
        private string _selectedSubcategory = DetailItemFactorySettings.DefaultTargetSubcategory;
        private bool _overwriteExisting = true;
        private bool _cleanupTempFiles = true;

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
                if (SetProperty(ref _outputPath, value ?? string.Empty))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Gets the collection of <c>OST_DetailComponents</c> subcategory names available in the document.
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
                if (SetProperty(ref _selectedSubcategory, value ?? string.Empty))
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
        /// Gets or sets a value indicating whether temporary DWG and PCP export files are deleted after each element finishes.
        /// </summary>
        public bool CleanupTempFiles
        {
            get => _cleanupTempFiles;
            set => SetProperty(ref _cleanupTempFiles, value);
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
        /// Command to trigger processing (runs validation and requests window closure with <c>true</c>).
        /// </summary>
        public ICommand RunCommand { get; }

        /// <summary>
        /// Command to abort processing and request window closure with <c>false</c>.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Event raised when the ViewModel requests dialog closure with the specified dialog result.
        /// </summary>
        public event Action<bool>? RequestClose;

        /// <summary>
        /// Legacy close delegate cleared automatically when the hosting Window closes.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryViewModel"/> class.
        /// </summary>
        /// <param name="snapshot">The immutable session snapshot harvested from Revit.</param>
        /// <param name="folderDialogService">Optional folder picker service for browsing output folders.</param>
        public DetailItemFactoryViewModel(
            DetailItemSessionSnapshot snapshot,
            IFolderDialogService? folderDialogService = null)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _folderDialogService = folderDialogService;

            string initialPath = !string.IsNullOrWhiteSpace(_snapshot.Settings.OutputPath)
                ? _snapshot.Settings.OutputPath
                : _snapshot.DefaultOutputFolder;

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                OutputPath = EnsureTrailingSeparator(initialPath);
            }

            BrowseCommand = new RelayCommand(_ => ExecuteBrowse(), _ => _folderDialogService != null);
            RemoveElementCommand = new RelayCommand(ExecuteRemoveElement);
            RunCommand = new RelayCommand(_ => ExecuteRun(), _ => CanExecuteRun());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());

            foreach (DetailItemSessionElement element in _snapshot.Elements)
            {
                Elements.Add(new SelectedElementItemViewModel(element, _snapshot.ActiveViewKind));
            }

            foreach (string subCatName in _snapshot.AvailableSubcategories)
            {
                Subcategories.Add(subCatName);
            }

            string requestedSubcat = (_snapshot.Settings.TargetSubcategory ?? string.Empty).Trim();
            string? matchedSubcat = Subcategories.FirstOrDefault(
                s => string.Equals(s, requestedSubcat, StringComparison.OrdinalIgnoreCase));
            SelectedSubcategory = string.IsNullOrWhiteSpace(matchedSubcat)
                ? DetailItemFactorySettings.DefaultTargetSubcategory
                : matchedSubcat!;
        }

        /// <summary>
        /// Builds an immutable <see cref="DetailItemBatchPlan"/> reflecting the user's current UI configuration.
        /// </summary>
        public DetailItemBatchPlan BuildBatchPlan(Func<string, bool>? fileExistsCheck = null)
        {
            return DetailItemBatchPlanner.BuildPlan(
                activeViewUniqueId: _snapshot.ActiveViewUniqueId,
                outputFolder: OutputPath,
                targetSubcategory: SelectedSubcategory,
                overwriteExisting: OverwriteExisting,
                selectedElements: Elements.Select(e => new DetailItemElementSelection(
                    elementUniqueId: e.ElementUniqueId,
                    elementId: e.ElementId,
                    displayName: e.DisplayName,
                    familyName: e.FamilyName,
                    typeName: e.TypeName,
                    selectedOrientation: e.SelectedOrientation)),
                fileExistsCheck: fileExistsCheck,
                cleanupTempFiles: CleanupTempFiles);
        }

        private void ExecuteBrowse()
        {
            if (_folderDialogService == null)
            {
                return;
            }

            string? selectedPath = _folderDialogService.SelectFolder("Select Output Folder", OutputPath);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                OutputPath = EnsureTrailingSeparator(selectedPath!);
            }
        }

        private void ExecuteRemoveElement(object? parameter)
        {
            if (parameter is SelectedElementItemViewModel item && Elements.Remove(item))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool CanExecuteRun()
        {
            return Elements.Count > 0 &&
                   !string.IsNullOrWhiteSpace(OutputPath) &&
                   !string.IsNullOrWhiteSpace(SelectedSubcategory) &&
                   DetailItemFactorySettings.IsValidOutputPathSyntax(OutputPath);
        }

        private void ExecuteRun()
        {
            if (!CanExecuteRun())
            {
                return;
            }

            RequestClose?.Invoke(true);
            CloseAction?.Invoke(true);
        }

        private void ExecuteCancel()
        {
            RequestClose?.Invoke(false);
            CloseAction?.Invoke(false);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string trimmed = path.Trim();
            string sep = Path.DirectorySeparatorChar.ToString();
            if (!trimmed.EndsWith(sep, StringComparison.Ordinal) &&
                !trimmed.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return trimmed + sep;
            }

            return trimmed;
        }
    }
}
