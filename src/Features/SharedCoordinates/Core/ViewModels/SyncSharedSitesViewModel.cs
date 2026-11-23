using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Synthetic.Features.SharedCoordinates.Adapters;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Platform.UI;

namespace Synthetic.Features.SharedCoordinates.ViewModels
{
    /// <summary>
    /// ViewModel for the Sync Shared Sites to Levels dialog.
    /// Manages linked host model selection, Reference Datum Level defaults,
    /// elevation-sorted linked level checklist filtering, and atomic execution dispatch.
    /// </summary>
    public class SyncSharedSitesViewModel : ViewModelBase
    {
        private readonly ISharedCoordinatesAdapter _adapter;
        private LinkedModelItem? _selectedLinkedModel;
        private LevelItem? _selectedReferenceDatumLevel;
        private string _searchText = string.Empty;
        private string _statusMessage = string.Empty;
        private SyncSharedSitesSummary? _lastSummary;

        /// <summary>
        /// Raised when the dialog should close, passing true on successful execution or false on cancellation.
        /// </summary>
        public event Action<bool>? RequestClose;

        /// <summary>
        /// Gets the collection of loaded linked models available for selection.
        /// </summary>
        public ObservableCollection<LinkedModelItem> LinkedModels { get; } = new ObservableCollection<LinkedModelItem>();

        /// <summary>
        /// Gets or sets the currently selected linked host model.
        /// </summary>
        public LinkedModelItem? SelectedLinkedModel
        {
            get => _selectedLinkedModel;
            set
            {
                if (SetProperty(ref _selectedLinkedModel, value))
                {
                    LoadLinkedLevels();
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets the collection of active unit model levels available as the Reference Datum Level.
        /// </summary>
        public ObservableCollection<LevelItem> ActiveModelLevels { get; } = new ObservableCollection<LevelItem>();

        /// <summary>
        /// Gets or sets the selected Reference Datum Level in the active unit model.
        /// Defaults to the active level whose internal elevation is closest to 0'-0" (smallest |Elevation|).
        /// </summary>
        public LevelItem? SelectedReferenceDatumLevel
        {
            get => _selectedReferenceDatumLevel;
            set
            {
                if (SetProperty(ref _selectedReferenceDatumLevel, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets all candidate target sites for the selected linked model.
        /// </summary>
        public ObservableCollection<SharedSiteSyncTarget> AllTargetSites { get; } = new ObservableCollection<SharedSiteSyncTarget>();

        /// <summary>
        /// Gets the filtered collection of target sites displayed in the UI checklist.
        /// </summary>
        public ObservableCollection<SharedSiteSyncTarget> FilteredTargetSites { get; } = new ObservableCollection<SharedSiteSyncTarget>();

        /// <summary>
        /// Gets or sets the search filter text for linked levels.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>
        /// Gets or sets a status or informational message for the user.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets the summary report of the last completed synchronization.
        /// </summary>
        public SyncSharedSitesSummary? LastSummary
        {
            get => _lastSummary;
            private set => SetProperty(ref _lastSummary, value);
        }

        /// <summary>
        /// Gets a value indicating whether the Sync Sites command can execute.
        /// Requires a valid linked model, a reference datum level, and at least one checked target level.
        /// </summary>
        public bool CanExecuteSync =>
            SelectedLinkedModel != null &&
            SelectedReferenceDatumLevel != null &&
            AllTargetSites.Any(t => t.IsChecked);

        /// <summary>
        /// Gets the command to select all currently visible target sites.
        /// </summary>
        public ICommand SelectAllCommand { get; }

        /// <summary>
        /// Gets the command to deselect all currently visible target sites.
        /// </summary>
        public ICommand SelectNoneCommand { get; }

        /// <summary>
        /// Gets the command to execute Shared Site Sync.
        /// </summary>
        public ICommand SyncCommand { get; }

        /// <summary>
        /// Gets the command to cancel and close the dialog without executing.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSharedSitesViewModel"/> class.
        /// </summary>
        /// <param name="adapter">The seam adapter providing Revit database access.</param>
        public SyncSharedSitesViewModel(ISharedCoordinatesAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

            SelectAllCommand = new RelayCommand(_ => ExecuteSelectAll());
            SelectNoneCommand = new RelayCommand(_ => ExecuteSelectNone());
            SyncCommand = new RelayCommand(_ => ExecuteSync(), _ => CanExecuteSync);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            Initialize();
        }

        private void Initialize()
        {
            // 1. Populate loaded linked models
            LinkedModels.Clear();
            var loadedLinks = _adapter.GetLoadedLinkedModels();
            foreach (var link in loadedLinks)
            {
                LinkedModels.Add(link);
            }

            // 2. Auto-select linked model:
            // Prefer viewport-preselected link, otherwise auto-select if only 1 loaded link exists
            var preselected = LinkedModels.FirstOrDefault(m => m.IsSelected);
            if (preselected != null)
            {
                SelectedLinkedModel = preselected;
            }
            else if (LinkedModels.Count == 1)
            {
                SelectedLinkedModel = LinkedModels[0];
            }
            else
            {
                SelectedLinkedModel = null;
            }

            // 3. Populate active model levels
            ActiveModelLevels.Clear();
            var activeLevels = _adapter.GetActiveModelLevels();
            foreach (var level in activeLevels)
            {
                ActiveModelLevels.Add(level);
            }

            // 4. Reference Datum Level defaults to level closest to internal 0'-0" (smallest |Elevation|)
            if (ActiveModelLevels.Count > 0)
            {
                SelectedReferenceDatumLevel = ActiveModelLevels
                    .OrderBy(l => Math.Abs(l.Elevation))
                    .FirstOrDefault();
            }

            UpdateCommandStates();
        }

        private void LoadLinkedLevels()
        {
            AllTargetSites.Clear();
            FilteredTargetSites.Clear();

            if (SelectedLinkedModel == null)
            {
                StatusMessage = "Please select a linked host model.";
                return;
            }

            var linkedLevels = _adapter.GetLinkedModelLevels(SelectedLinkedModel.Id);
            var existingSiteNames = new HashSet<string>(_adapter.GetExistingSharedSiteNames(), StringComparer.OrdinalIgnoreCase);

            // Checklist is sorted ascending by elevation
            var sortedLevels = linkedLevels.OrderBy(l => l.SharedElevation).ToList();

            foreach (var level in sortedLevels)
            {
                bool exists = existingSiteNames.Contains(level.Name);
                var target = new SharedSiteSyncTarget
                {
                    LevelName = level.Name,
                    SharedElevation = level.SharedElevation,
                    SiteExists = exists,
                    IsChecked = exists // Automatically pre-check levels exactly matching existing sites
                };

                target.PropertyChanged += OnTargetPropertyChanged;
                AllTargetSites.Add(target);
            }

            ApplyFilter();
            UpdateStatusMessage();
        }

        private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SharedSiteSyncTarget.IsChecked))
            {
                UpdateCommandStates();
                UpdateStatusMessage();
            }
        }

        private void ApplyFilter()
        {
            FilteredTargetSites.Clear();
            IEnumerable<SharedSiteSyncTarget> items = AllTargetSites;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                items = items.Where(t => t.LevelName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            foreach (var item in items)
            {
                FilteredTargetSites.Add(item);
            }
        }

        private void ExecuteSelectAll()
        {
            foreach (var item in FilteredTargetSites)
            {
                item.IsChecked = true;
            }
            UpdateCommandStates();
            UpdateStatusMessage();
        }

        private void ExecuteSelectNone()
        {
            foreach (var item in FilteredTargetSites)
            {
                item.IsChecked = false;
            }
            UpdateCommandStates();
            UpdateStatusMessage();
        }

        private void UpdateStatusMessage()
        {
            int checkedCount = AllTargetSites.Count(t => t.IsChecked);
            int totalCount = AllTargetSites.Count;
            int updateCount = AllTargetSites.Count(t => t.IsChecked && t.SiteExists);
            int createCount = AllTargetSites.Count(t => t.IsChecked && !t.SiteExists);

            if (totalCount == 0)
            {
                StatusMessage = SelectedLinkedModel != null
                    ? "No levels found in selected linked model."
                    : "Please select a linked host model.";
            }
            else
            {
                StatusMessage = $"{checkedCount} of {totalCount} levels selected ({createCount} to create, {updateCount} to update).";
            }
        }

        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(CanExecuteSync));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ExecuteSync()
        {
            if (!CanExecuteSync) return;

            var checkedTargets = AllTargetSites.Where(t => t.IsChecked).ToList();
            var request = new SyncSharedSitesRequest
            {
                LinkedModelId = SelectedLinkedModel!.Id,
                ReferenceDatumLevel = SelectedReferenceDatumLevel!,
                TargetSites = checkedTargets
            };

            LastSummary = _adapter.SyncSharedSites(request);
            RequestClose?.Invoke(true);
        }
    }
}
