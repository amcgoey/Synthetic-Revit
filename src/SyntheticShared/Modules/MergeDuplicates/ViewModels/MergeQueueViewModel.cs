using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.MergeDuplicates.ViewModels
{
    /// <summary>
    /// ViewModel for managing a queue of elements to merge.
    /// </summary>
    public class MergeQueueViewModel : ViewModelBase
    {
        private ObservableCollection<DuplicateClusterModel> _queuedClusters;
        private readonly Action<DuplicateClusterModel>? _onRemoved;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.
        /// </summary>
        public MergeQueueViewModel() : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeQueueViewModel"/> class.
        /// </summary>
        /// <param name="onRemoved">Callback triggered when an item is removed from the queue.</param>
        public MergeQueueViewModel(Action<DuplicateClusterModel>? onRemoved)
        {
            _queuedClusters = new ObservableCollection<DuplicateClusterModel>();
            _onRemoved = onRemoved;

            CmdRemoveFromQueue = new RelayCommand(OnRemoveFromQueue);
            CmdClearQueue = new RelayCommand(OnClearQueue);
        }

        /// <summary>
        /// Gets or sets the collection of duplicate clusters queued for merging.
        /// </summary>
        public ObservableCollection<DuplicateClusterModel> QueuedClusters
        {
            get => _queuedClusters;
            set => SetProperty(ref _queuedClusters, value);
        }

        /// <summary>
        /// Gets the command to remove a cluster from the merge queue.
        /// </summary>
        public ICommand CmdRemoveFromQueue { get; }

        /// <summary>
        /// Gets the command to clear the entire merge queue.
        /// </summary>
        public ICommand CmdClearQueue { get; }

        /// <summary>
        /// Removes a duplicate cluster from the queue.
        /// </summary>
        /// <param name="cluster">The duplicate cluster to remove.</param>
        public void RemoveFromQueue(DuplicateClusterModel cluster)
        {
            if (cluster != null && _queuedClusters.Contains(cluster))
            {
                _queuedClusters.Remove(cluster);
                _onRemoved?.Invoke(cluster);
            }
        }

        /// <summary>
        /// Clears all duplicate clusters from the queue.
        /// </summary>
        public void ClearQueue()
        {
            var clusters = _queuedClusters.ToList();
            _queuedClusters.Clear();
            foreach (var cluster in clusters)
            {
                _onRemoved?.Invoke(cluster);
            }
        }

        private void OnRemoveFromQueue(object parameter)
        {
            if (parameter is DuplicateClusterModel cluster)
            {
                RemoveFromQueue(cluster);
            }
        }

        private void OnClearQueue(object parameter)
        {
            ClearQueue();
        }
    }
}
