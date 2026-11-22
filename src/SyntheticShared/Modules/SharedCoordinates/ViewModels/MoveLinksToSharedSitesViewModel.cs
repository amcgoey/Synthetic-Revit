using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.Models;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.SharedCoordinates.ViewModels
{
    /// <summary>
    /// ViewModel for the Move Links to Shared Sites dialog.
    /// Manages link group checklists, aggregate instance tallies, search filtering,
    /// and atomic 3D repositioning dispatch.
    /// </summary>
    public class MoveLinksToSharedSitesViewModel : ViewModelBase
    {
        private readonly ISharedCoordinatesAdapter _adapter;
        private string _searchText = string.Empty;
        private string _statusMessage = string.Empty;
        private RepositionLinksSummary? _lastSummary;

        /// <summary>
        /// Raised when the dialog should close, passing true on successful execution or false on cancellation.
        /// </summary>
        public event Action<bool>? RequestClose;

        /// <summary>
        /// Gets all loaded link groups in the active document.
        /// </summary>
        public ObservableCollection<LinkRepositionGroupItem> AllGroups { get; } = new ObservableCollection<LinkRepositionGroupItem>();

        /// <summary>
        /// Gets the filtered collection of link groups displayed in the UI checklist.
        /// </summary>
        public ObservableCollection<LinkRepositionGroupItem> FilteredGroups { get; } = new ObservableCollection<LinkRepositionGroupItem>();

        /// <summary>
        /// Gets or sets the search filter text for link types.
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
        /// Gets or sets an informational status message displayed in the dialog footer.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets the total count of link instances across all loaded groups.
        /// </summary>
        public int TotalInstancesCount => AllGroups.Sum(g => g.TotalInstanceCount);

        /// <summary>
        /// Gets the count of link instances that are out of sync across all loaded groups.
        /// </summary>
        public int OutOfSyncInstancesCount => AllGroups.Sum(g => g.OutOfSyncCount);

        /// <summary>
        /// Gets the count of link instances with unassigned or not-shared sites across all loaded groups.
        /// </summary>
        public int UnassignedInstancesCount => AllGroups.Sum(g => g.UnassignedCount);

        /// <summary>
        /// Gets the summary result of the most recent repositioning execution.
        /// </summary>
        public RepositionLinksSummary? LastSummary
        {
            get => _lastSummary;
            private set => SetProperty(ref _lastSummary, value);
        }

        /// <summary>
        /// Command to select (check) all link groups.
        /// </summary>
        public ICommand SelectAllCommand { get; }

        /// <summary>
        /// Command to deselect (uncheck) all link groups.
        /// </summary>
        public ICommand SelectNoneCommand { get; }

        /// <summary>
        /// Command to execute repositioning for all selected link groups.
        /// </summary>
        public ICommand RepositionCommand { get; }

        /// <summary>
        /// Command to cancel and close the dialog without executing.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MoveLinksToSharedSitesViewModel"/> class.
        /// </summary>
        /// <param name="adapter">The shared coordinates database adapter seam.</param>
        public MoveLinksToSharedSitesViewModel(ISharedCoordinatesAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

            SelectAllCommand = new RelayCommand(_ => SetAllChecked(true), _ => AllGroups.Count > 0);
            SelectNoneCommand = new RelayCommand(_ => SetAllChecked(false), _ => AllGroups.Count > 0);
            RepositionCommand = new RelayCommand(_ => ExecuteReposition(), CanReposition);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

            LoadGroups();
        }

        private void LoadGroups()
        {
            AllGroups.Clear();
            FilteredGroups.Clear();

            var groups = _adapter.GetLinkRepositionGroups();
            foreach (var g in groups)
            {
                // All groups checked by default
                g.IsChecked = true;
                g.PropertyChanged += GroupItem_PropertyChanged;
                AllGroups.Add(g);
                FilteredGroups.Add(g);
            }

            OnPropertyChanged(nameof(TotalInstancesCount));
            OnPropertyChanged(nameof(OutOfSyncInstancesCount));
            OnPropertyChanged(nameof(UnassignedInstancesCount));

            UpdateStatusAndCommands();
        }

        private void GroupItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinkRepositionGroupItem.IsChecked))
            {
                UpdateStatusAndCommands();
            }
        }

        private void SetAllChecked(bool isChecked)
        {
            foreach (var group in FilteredGroups)
            {
                group.IsChecked = isChecked;
            }
            UpdateStatusAndCommands();
        }

        private void ApplyFilter()
        {
            FilteredGroups.Clear();

            string query = _searchText?.Trim() ?? string.Empty;
            foreach (var group in AllGroups)
            {
                if (string.IsNullOrEmpty(query) ||
                    group.TypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FilteredGroups.Add(group);
                }
            }

            UpdateStatusAndCommands();
        }

        private bool CanReposition(object? parameter)
        {
            return AllGroups.Any(g => g.IsChecked);
        }

        private void ExecuteReposition()
        {
            var selectedTypeIds = AllGroups
                .Where(g => g.IsChecked)
                .Select(g => g.TypeId)
                .ToList();

            if (selectedTypeIds.Count == 0)
            {
                StatusMessage = "Please select at least one link type to reposition.";
                return;
            }

            var request = new RepositionLinksRequest
            {
                SelectedTypeIds = selectedTypeIds
            };

            var summary = _adapter.RepositionLinks(request);
            LastSummary = summary;

            RequestClose?.Invoke(true);
        }

        private void UpdateStatusAndCommands()
        {
            int checkedCount = AllGroups.Count(g => g.IsChecked);
            int selectedOutOfSync = AllGroups.Where(g => g.IsChecked).Sum(g => g.OutOfSyncCount);

            if (AllGroups.Count == 0)
            {
                StatusMessage = "No linked models found in the active project.";
            }
            else if (checkedCount == 0)
            {
                StatusMessage = "No link types selected. Check one or more link types to reposition.";
            }
            else if (selectedOutOfSync == 0)
            {
                StatusMessage = "All selected link instances are already aligned with their Shared Sites.";
            }
            else
            {
                StatusMessage = $"{selectedOutOfSync} out-of-sync instance(s) ready to reposition across {checkedCount} link type(s).";
            }

            CommandManager.InvalidateRequerySuggested();


        }
    }
}
