using Synthetic.Modules.MergeDuplicates.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Newtonsoft.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.RevitAPI;
using Synthetic.Infrastructure.IO;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Handlers;

using Synthetic.Modules.MergeDuplicates.Views;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for scanning and merging duplicate elements in the Revit database.
    /// </summary>
    public class MergeDuplicatesViewModel : ViewModelBase
    {
        private ObservableCollection<DuplicateClusterModel> _scannedClusters;
        private MergeQueueViewModel _queueVM;
        private double _progressValue;
        private double _progressMax = 100;
        private IntPtr _mainWindowHandle;
        private Document? _document;
        private ProcessMergeEventHandler? _mergeHandler;
        private ExternalEvent? _mergeEvent;
        private System.Threading.CancellationTokenSource _cts = new System.Threading.CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeDuplicatesViewModel"/> class.
        /// </summary>
        public MergeDuplicatesViewModel()
        {
            _scannedClusters = new ObservableCollection<DuplicateClusterModel>();
            _queueVM = new MergeQueueViewModel(cluster =>
            {
                if (cluster != null && !_scannedClusters.Contains(cluster))
                {
                    _scannedClusters.Add(cluster);
                }
            });

            CmdScanModel = new RelayCommand(ExecuteScanModel);
            CmdLoadSelection = new RelayCommand(ExecuteLoadSelection);
            CmdAddToQueue = new RelayCommand(ExecuteAddToQueue);
            CmdProcessMerge = new RelayCommand(ExecuteProcessMerge);
            CmdCancelScan = new RelayCommand(ExecuteCancelScan);
            CmdMoveItem = new RelayCommand(ExecuteMoveItem);
            CmdDetailedReview = new RelayCommand(ExecuteDetailedReview);
            CmdAddStragglers = new RelayCommand(ExecuteAddStragglers);
            CmdRemoveFromQueue = new RelayCommand(ExecuteRemoveFromQueue);

            try
            {
                _mergeHandler = new ProcessMergeEventHandler();
                _mergeEvent = ExternalEvent.Create(_mergeHandler);
            }
            catch (Exception)
            {
                // Safety fallback if instantiated outside Revit API thread/session context
            }
        }

        /// <summary>
        /// Gets or sets the main window handle.
        /// </summary>
        [JsonIgnore]
        public IntPtr MainWindowHandle
        {
            get => _mainWindowHandle;
            set => SetProperty(ref _mainWindowHandle, value);
        }

        /// <summary>
        /// Gets or sets the active Revit document.
        /// </summary>
        [JsonIgnore]
        public Document? Document
        {
            get => _document;
            set => SetProperty(ref _document, value);
        }

        /// <summary>
        /// Gets or sets the list of scanned duplicate clusters.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> ScannedClusters
        {
            get => _scannedClusters;
            set => SetProperty(ref _scannedClusters, value);
        }

        /// <summary>
        /// Gets or sets the merge queue view model.
        /// </summary>
        public MergeQueueViewModel QueueVM
        {
            get => _queueVM;
            set
            {
                if (SetProperty(ref _queueVM, value))
                {
                    OnPropertyChanged(nameof(QueuedClusters));
                }
            }
        }

        /// <summary>
        /// Gets the collection of duplicate clusters queued for merging.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel>? QueuedClusters => QueueVM?.QueuedClusters;

        /// <summary>
        /// Gets or sets the current progress value of the scan.
        /// </summary>
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        /// <summary>
        /// Gets or sets the maximum progress value of the scan.
        /// </summary>
        public double ProgressMax
        {
            get => _progressMax;
            set => SetProperty(ref _progressMax, value);
        }

        /// <summary>
        /// Gets the command to scan the model for duplicates.
        /// </summary>
        public ICommand CmdScanModel { get; }

        /// <summary>
        /// Gets the command to load the current Revit selection.
        /// </summary>
        public ICommand CmdLoadSelection { get; }

        /// <summary>
        /// Gets the command to add a cluster to the merge queue.
        /// </summary>
        public ICommand CmdAddToQueue { get; }

        /// <summary>
        /// Gets the command to remove a cluster from the merge queue.
        /// </summary>
        public ICommand CmdRemoveFromQueue { get; }

        /// <summary>
        /// Gets the command to execute the merge queue.
        /// </summary>
        public ICommand CmdProcessMerge { get; }

        /// <summary>
        /// Gets the command to cancel an active scan.
        /// </summary>
        public ICommand CmdCancelScan { get; }

        /// <summary>
        /// Gets the command to move an item from one cluster to another.
        /// </summary>
        public ICommand CmdMoveItem { get; }

        /// <summary>
        /// Gets the command to launch the detailed review window for a cluster.
        /// </summary>
        public ICommand CmdDetailedReview { get; }
        /// <summary>
        /// Gets the command to add straggler elements to the duplicate cluster.
        /// </summary>
        public ICommand CmdAddStragglers { get; }

        private void ProcessAndLoadClusters(IEnumerable<DuplicateClusterModel> clusters, CancellationToken token)
        {
            ScannedClusters.Clear();
            foreach (var cluster in clusters)
            {
                token.ThrowIfCancellationRequested();
                MergeAnalysisEngine.RunDeepScan(cluster, token);
                MergeAnalysisEngine.GenerateRecommendations(cluster);
            }
            ScannedClusters = new ObservableCollection<DuplicateClusterModel>(clusters);
        }

        private void ExecuteScanModel(object parameter)
        {
            if (Document == null) return;

            try
            {
                var clusters = RevitMergeDataCollector.RunFastScan(Document, _cts.Token);
                ProcessAndLoadClusters(clusters, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Scan cancelled
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Scan Model Error", ex.Message);
            }
        }

        private void ExecuteLoadSelection(object parameter)
        {
            if (Document == null) return;

            try
            {
                var clusters = RevitMergeDataCollector.RunTargetedScan(Document, _cts.Token);
                ProcessAndLoadClusters(clusters, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Selection load cancelled
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Load Selection Error", ex.Message);
            }
        }

        private void ExecuteAddToQueue(object parameter)
        {
            if (Document == null) return;
            if (parameter is DuplicateClusterModel cluster)
            {
                // Run deep scan to ensure current state is up-to-date
                MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);

                // Run GenerateRecommendations to analyze parameter conflicts
                MergeAnalysisEngine.GenerateRecommendations(cluster);

                // Check if there are schema mismatches or parameter conflicts
                bool hasSchemaMismatch = cluster.HasSchemaMismatch;
                bool hasConflicts = false;

                foreach (var mapping in cluster.TypeMappings)
                {
                    if (mapping.ParameterResolutions.Any(r => r.HasConflict || r.IsSchemaMismatch))
                    {
                        hasConflicts = true;
                    }
                }

                // Block if mismatches/conflicts exist and no manual resolutions have been committed
                int resolutionsCount = cluster.ParameterResolutions?.Count ?? 0;
                bool isBlocked = (hasSchemaMismatch || hasConflicts) && (resolutionsCount == 0);

                if (isBlocked)
                {
                    cluster.IsBlocked = true;
                    ExecuteDetailedReview(cluster);
                    return;
                }

                cluster.IsBlocked = false;
                if (QueueVM != null && !QueueVM.QueuedClusters.Contains(cluster))
                {
                    QueueVM.QueuedClusters.Add(cluster);
                    ScannedClusters.Remove(cluster);
                }
            }
        }

        private void ExecuteMoveItem(object parameter)
        {
            if (!(parameter is DuplicateClusterModel sourceCluster)) return;
            if (sourceCluster.Items == null || sourceCluster.Items.Count == 0) return;

            // Prompt 1: Select item to move
            var itemNames = sourceCluster.Items.Select(i => i.ItemName).ToList();
            var itemSelectVM = new DropdownSelectionViewModel
            {
                Title = "Move Item - Select Item",
                Instruction = $"Select an item to move from '{sourceCluster.ClusterName}':",
                ItemLabel = "Item to Move:",
                Items = itemNames,
                IsSorted = true
            };

            var itemSelectView = new Synthetic.Shared.UI.DropdownSelectionView(MainWindowHandle)
            {
                DataContext = itemSelectVM
            };

            if (itemSelectView.ShowDialog() != true) return;

            string? selectedItemName = itemSelectVM.SelectedItem;
            var itemToMove = sourceCluster.Items.FirstOrDefault(i => i.ItemName == selectedItemName);
            if (itemToMove == null) return;

            // Prompt 2: Select target cluster
            var targetClusterNames = ScannedClusters
                .Where(c => c != sourceCluster)
                .Select(c => c.ClusterName)
                .ToList();

            if (targetClusterNames.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Move Item", "There are no other clusters available to move this item to.");
                return;
            }

            var clusterSelectVM = new DropdownSelectionViewModel
            {
                Title = "Move Item - Select Target Cluster",
                Instruction = $"Select target cluster for item '{itemToMove.ItemName}':",
                ItemLabel = "Target Cluster:",
                Items = targetClusterNames,
                IsSorted = true
            };

            var clusterSelectView = new Synthetic.Shared.UI.DropdownSelectionView(MainWindowHandle)
            {
                DataContext = clusterSelectVM
            };

            if (clusterSelectView.ShowDialog() != true) return;

            string? selectedClusterName = clusterSelectVM.SelectedItem;
            var targetCluster = ScannedClusters.FirstOrDefault(c => c.ClusterName == selectedClusterName);
            if (targetCluster == null) return;

            // Perform movement
            sourceCluster.Items.Remove(itemToMove);
            targetCluster.Items.Add(itemToMove);

            // Re-evaluate primary item for source cluster if the item we moved was primary
            if (itemToMove.IsPrimary && sourceCluster.Items.Count > 0)
            {
                var newPrimary = sourceCluster.Items.OrderBy(i => i.ItemName.Length).First();
                sourceCluster.UpdatePrimaryItem(newPrimary);
            }

            // Set primary for target cluster (coordinates sync)
            if (itemToMove.IsPrimary)
            {
                targetCluster.UpdatePrimaryItem(itemToMove);
            }
            else
            {
                if (targetCluster.SelectedPrimary == null && targetCluster.Items.Count > 0)
                {
                    var newPrimary = targetCluster.Items.OrderBy(i => i.ItemName.Length).First();
                    targetCluster.UpdatePrimaryItem(newPrimary);
                }
            }

            // Run deep scan on both affected clusters
            MergeAnalysisEngine.RunDeepScan(sourceCluster, System.Threading.CancellationToken.None);
            MergeAnalysisEngine.RunDeepScan(targetCluster, System.Threading.CancellationToken.None);

            // If source cluster now has 1 or fewer items, remove it from list
            if (sourceCluster.Items.Count <= 1)
            {
                ScannedClusters.Remove(sourceCluster);
            }
        }

        private void ExecuteDetailedReview(object parameter)
        {
            if (Document == null) return;
            if (parameter is DuplicateClusterModel cluster)
            {
                if (cluster.TypeMappings == null || cluster.TypeMappings.Count == 0)
                {
                    MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                }

                var vm = new MergeDetailedReviewViewModel(cluster, c =>
                {
                    // Callback when confirmed: Add directly to queue, bypassing the gatekeeper since it has been resolved
                    c.IsBlocked = false;
                    if (QueueVM != null && !QueueVM.QueuedClusters.Contains(c))
                    {
                        QueueVM.QueuedClusters.Add(c);
                        ScannedClusters.Remove(c);
                    }
                }, c =>
                {
                    // Callback when Move item is requested from Detailed Review
                    ExecuteMoveItem(c);
                });
                var view = new Views.MergeDetailedReviewWindow(MainWindowHandle)
                {
                    DataContext = vm
                };
                view.Show();
            }
        }

        private void ExecuteProcessMerge(object parameter)
        {
            if (QueueVM == null || QueueVM.QueuedClusters.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Merge Duplicates", "No merges in the queue to process.");
                return;
            }

            if (_mergeHandler != null && _mergeEvent != null)
            {
                var window = parameter as System.Windows.Window;
                // Pass _cts.Token to external event handler request
                _mergeHandler.QueueRequest(QueueVM, window, this, _cts.Token);
                _mergeEvent.Raise();
            }
        }

        private void ExecuteCancelScan(object parameter)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _cts = new System.Threading.CancellationTokenSource();
        }


        private void ExecuteRemoveFromQueue(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                if (QueueVM != null && QueueVM.QueuedClusters.Contains(cluster))
                {
                    QueueVM.QueuedClusters.Remove(cluster);
                    if (!ScannedClusters.Contains(cluster))
                    {
                        ScannedClusters.Add(cluster);
                    }
                }
            }
        }

        private void ExecuteAddStragglers(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                if (Document == null) return;

                // Determine category name from first item or default to CategoryName of cluster items
                string? categoryName = cluster.Items.FirstOrDefault()?.CategoryName;
                if (string.IsNullOrEmpty(categoryName)) return;

                // Find candidate elements in the document of the same category
                var candidateList = new List<Tuple<string, ElementId>>();
                var existingIds = new HashSet<ElementId>(cluster.Items.Where(i => i.RevitElementId != null).Select(i => i.RevitElementId!.ToElementId()));

                candidateList = RevitMergeDataCollector.GetCandidateStragglers(Document!, categoryName, existingIds);

                if (candidateList.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Add Stragglers", $"No other elements of category '{categoryName}' found in the document.");
                    return;
                }

                var listVM = new ListByCheckboxViewModel
                {
                    Title = "Add Stragglers",
                    Instruction = $"Select elements of category '{categoryName}' to force into cluster '{cluster.ClusterName}':",
                    IsSorted = true
                };
                listVM.SetItems(candidateList);

                var listWindow = new Synthetic.Shared.UI.ListByCheckboxView(MainWindowHandle)
                {
                    DataContext = listVM
                };

                if (listWindow.ShowDialog() == true)
                {
                    var selectedIds = listVM.CheckedElementIds;
                    foreach (var id in selectedIds)
                    {
                        var elem = Document!.GetElement(id);
                        if (elem == null) continue;

                        // Convert to POCO first and use the shared engine to create the item model
                        var elementModel = RevitMergeDataCollector.ConvertToPoco(Document!, elem);
                        var item = MergeAnalysisEngine.CreateItemFromModel(elementModel, categoryName!);

                        cluster.Items.Add(item);
                    }

                    // Run deep scan and regenerate recommendations on updated cluster
                    MergeAnalysisEngine.RunDeepScan(cluster, System.Threading.CancellationToken.None);
                    MergeAnalysisEngine.GenerateRecommendations(cluster);
                    cluster.IsBlocked = false; // Reset block status so they can attempt queueing again
                }
            }
        }
    }
}
