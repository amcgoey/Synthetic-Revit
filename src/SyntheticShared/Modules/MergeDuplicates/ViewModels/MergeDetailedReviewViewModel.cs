using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Engine;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for the detailed merge review view.
    /// </summary>
    public class MergeDetailedReviewViewModel : ViewModelBase
    {
        private DuplicateClusterModel _cluster;
        private TypeMappingModel? _selectedTypeMapping;

        private Action<DuplicateClusterModel>? _commitCallback;
        private Action<DuplicateClusterModel>? _moveItemCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDetailedReviewViewModel"/> class.
        /// </summary>
        /// <param name="cluster">The duplicate cluster model to review.</param>
        /// <param name="commitCallback">Optional callback when committing a cluster.</param>
        /// <param name="moveItemCallback">Optional callback when moving an item.</param>
        public MergeDetailedReviewViewModel(
            DuplicateClusterModel cluster, 
            Action<DuplicateClusterModel>? commitCallback = null,
            Action<DuplicateClusterModel>? moveItemCallback = null)
        {
            _cluster = cluster;
            _commitCallback = commitCallback;
            _moveItemCallback = moveItemCallback;

            CmdCommitToQueue = new RelayCommand(ExecuteCommitToQueue);
            CmdMoveItem = new RelayCommand(ExecuteMoveItem);

            if (cluster != null)
            {
                // Set up default view grouping by Family
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(cluster.TypeMappings);
                if (view != null)
                {
                    view.GroupDescriptions.Clear();
                    view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("SourceFamily.ItemName"));
                }

                // Subscribe to SelectedPrimary changes to re-evaluate recommendations
                cluster.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DuplicateClusterModel.SelectedPrimary) || e.PropertyName == nameof(DuplicateClusterModel.IsBlocked))
                    {
                        MergeAnalysisEngine.GenerateRecommendations(cluster);
                        if (cluster.TypeMappings.Count > 0)
                        {
                            SelectedTypeMapping = cluster.TypeMappings[0];
                        }
                        else
                        {
                            SelectedTypeMapping = null;
                        }
                        System.Windows.Data.CollectionViewSource.GetDefaultView(cluster.TypeMappings)?.Refresh();
                        OnPropertyChanged(nameof(Rows));
                        OnPropertyChanged(nameof(IsBlockedMessageVisible));
                        OnPropertyChanged(nameof(BlockedReasonMessage));
                    }
                };

                if (cluster.TypeMappings.Count > 0)
                {
                    SelectedTypeMapping = cluster.TypeMappings[0];
                }
            }
        }

        /// <summary>
        /// Gets or sets the duplicate cluster model under review.
        /// </summary>
        public DuplicateClusterModel Cluster
        {
            get => _cluster;
            set
            {
                if (SetProperty(ref _cluster, value))
                {
                    OnPropertyChanged(nameof(IsBlockedMessageVisible));
                    OnPropertyChanged(nameof(BlockedReasonMessage));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the blocked message is visible.
        /// </summary>
        public bool IsBlockedMessageVisible
        {
            get => Cluster != null && Cluster.IsBlocked;
        }

        /// <summary>
        /// Gets the reason message why the cluster is blocked from being queued.
        /// </summary>
        public string BlockedReasonMessage
        {
            get
            {
                if (Cluster == null) return string.Empty;

                bool hasSchema = Cluster.HasSchemaMismatch;
                bool hasConflicts = false;
                if (Cluster.TypeMappings != null)
                {
                    foreach (var mapping in Cluster.TypeMappings)
                    {
                        if (mapping.ParameterResolutions != null && mapping.ParameterResolutions.Any(r => r.HasConflict || r.IsSchemaMismatch))
                        {
                            hasConflicts = true;
                            break;
                        }
                    }
                }

                if (hasSchema && hasConflicts)
                {
                    return "Blocked from Queue: Schema mismatches and parameter value conflicts detected. Resolve them below.";
                }
                if (hasSchema)
                {
                    return "Blocked from Queue: Schema mismatch detected. Please select compatible type mappings.";
                }
                if (hasConflicts)
                {
                    return "Blocked from Queue: Parameter value conflicts detected. Choose winning values under Winning Value Selection.";
                }

                return "Blocked from Queue: Conflicts or mismatches must be resolved.";
            }
        }

        /// <summary>
        /// Gets or sets the currently selected type mapping.
        /// </summary>
        public TypeMappingModel? SelectedTypeMapping
        {
            get => _selectedTypeMapping;
            set
            {
                var oldMapping = _selectedTypeMapping;
                if (SetProperty(ref _selectedTypeMapping, value))
                {
                    if (oldMapping != null)
                    {
                        oldMapping.PropertyChanged -= SelectedTypeMapping_PropertyChanged;
                    }
                    if (_selectedTypeMapping != null)
                    {
                        _selectedTypeMapping.PropertyChanged += SelectedTypeMapping_PropertyChanged;
                        // Reload parameters for the newly selected mapping
                        MergeAnalysisEngine.UpdateParameterResolutions(_selectedTypeMapping);
                    }
                    OnPropertyChanged(nameof(Rows));
                    OnPropertyChanged(nameof(DiffComparisonContext));
                }
            }
        }

        private void SelectedTypeMapping_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypeMappingModel.TargetType) ||
                e.PropertyName == nameof(TypeMappingModel.RecommendedAction) ||
                e.PropertyName == nameof(TypeMappingModel.MigrateRenameText))
            {
                if (SelectedTypeMapping != null)
                {
                    MergeAnalysisEngine.UpdateParameterResolutions(SelectedTypeMapping);
                }
                OnPropertyChanged(nameof(Rows));
                OnPropertyChanged(nameof(DiffComparisonContext));
            }
        }

        /// <summary>
        /// Gets the text context of the parameter difference comparison.
        /// </summary>
        public string DiffComparisonContext
        {
            get
            {
                if (SelectedTypeMapping == null) return string.Empty;

                string target = string.Empty;
                if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Merge)
                {
                    target = SelectedTypeMapping.TargetType?.Name ?? string.Empty;
                }
                else if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Migrate)
                {
                    target = SelectedTypeMapping.MigrateRenameText ?? string.Empty;
                }
                else if (SelectedTypeMapping.RecommendedAction == RecommendedAction.Exclude)
                {
                    target = "[Excluded]";
                }

                return $"Comparing: {SelectedTypeMapping.SourceType?.Name} to {target}";
            }
        }

        /// <summary>
        /// Gets the parameter diff rows for the selected type mapping.
        /// </summary>
        public ObservableCollection<ParameterDiffRowModel>? Rows
        {
            get => SelectedTypeMapping?.ParameterResolutions;
        }

        /// <summary>
        /// Gets the command to commit the cluster to the queue.
        /// </summary>
        public ICommand CmdCommitToQueue { get; }

        /// <summary>
        /// Gets the command to move the duplicate item out of this cluster.
        /// </summary>
        public ICommand CmdMoveItem { get; }

        /// <summary>
        /// Gets or sets the action to close the window.
        /// </summary>
        [JsonIgnore]
        public Action? CloseAction { get; set; }

        private void ExecuteCommitToQueue(object parameter)
        {
            if (Cluster != null)
            {
                Cluster.IsBlocked = false;
                Cluster.ParameterResolutions = new Dictionary<string, ElementId>();
                foreach (var mapping in Cluster.TypeMappings)
                {
                    if (mapping.ParameterResolutions != null)
                    {
                        foreach (var row in mapping.ParameterResolutions)
                        {
                            if (row.WinningValueElementId != null)
                            {
                                Cluster.ParameterResolutions[row.ParameterName] = row.WinningValueElementId;
                            }
                        }
                    }
                }
                _commitCallback?.Invoke(Cluster);
            }
            CloseAction?.Invoke();
        }

        private void ExecuteMoveItem(object parameter)
        {
            if (Cluster != null)
            {
                _moveItemCallback?.Invoke(Cluster);

                if (Cluster.Items == null || Cluster.Items.Count <= 1)
                {
                    CloseAction?.Invoke();
                    return;
                }

                MergeAnalysisEngine.GenerateRecommendations(Cluster);
                if (Cluster.TypeMappings.Count > 0)
                {
                    SelectedTypeMapping = Cluster.TypeMappings[0];
                }
                else
                {
                    SelectedTypeMapping = null;
                }
                System.Windows.Data.CollectionViewSource.GetDefaultView(Cluster.TypeMappings)?.Refresh();
                OnPropertyChanged(nameof(Rows));
            }
        }
    }
}
