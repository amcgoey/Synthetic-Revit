using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Models
{
    /// <summary>
    /// Model representing a cluster of duplicate elements.
    /// Manages the selection of the primary element and details about schema or origin conflicts.
    /// </summary>
    public class DuplicateClusterModel : ObjectModel, INotifyPropertyChanged
    {
        private ObservableCollection<DuplicateItemModel> _items;
        private string _clusterName = string.Empty;
        private bool _hasSchemaMismatch;
        private bool _hasOriginMismatch;

        /// <summary>
        /// Initializes a new instance of the DuplicateClusterModel class.
        /// </summary>
        public DuplicateClusterModel()
        {
            _items = new ObservableCollection<DuplicateItemModel>();
            SubscribeToItems();
        }

        /// <summary>
        /// Gets or sets the collection of duplicate items in this cluster.
        /// </summary>
        public ObservableCollection<DuplicateItemModel> Items
        {
            get => _items;
            set
            {
                UnsubscribeFromItems();
                if (SetProperty(ref _items, value))
                {
                    SubscribeToItems();
                }
            }
        }

        /// <summary>
        /// Gets or sets the name of the cluster.
        /// </summary>
        public string ClusterName
        {
            get => _clusterName;
            set => SetProperty(ref _clusterName, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether there is a schema mismatch within the cluster.
        /// </summary>
        public bool HasSchemaMismatch
        {
            get => _hasSchemaMismatch;
            set => SetProperty(ref _hasSchemaMismatch, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether there is a coordinate origin mismatch within the cluster.
        /// </summary>
        public bool HasOriginMismatch
        {
            get => _hasOriginMismatch;
            set => SetProperty(ref _hasOriginMismatch, value);
        }

        private bool _isBlocked;

        /// <summary>
        /// Gets or sets a value indicating whether this cluster is blocked from being merged.
        /// </summary>
        public bool IsBlocked
        {
            get => _isBlocked;
            set => SetProperty(ref _isBlocked, value);
        }

        private Dictionary<string, ElementId> _parameterResolutions = new Dictionary<string, ElementId>();

        /// <summary>
        /// Gets or sets the dictionary of parameter resolutions (which element's parameter value wins).
        /// </summary>
        public Dictionary<string, ElementId> ParameterResolutions
        {
            get => _parameterResolutions;
            set => SetProperty(ref _parameterResolutions, value);
        }

        private ObservableCollection<TypeMappingModel> _typeMappings = new ObservableCollection<TypeMappingModel>();

        /// <summary>
        /// Gets or sets the collection of type mappings for this cluster.
        /// </summary>
        public ObservableCollection<TypeMappingModel> TypeMappings
        {
            get => _typeMappings;
            set => SetProperty(ref _typeMappings, value);
        }

        /// <summary>
        /// Gets or sets the duplicate item designated as the primary/surviving element.
        /// </summary>
        public DuplicateItemModel? SelectedPrimary
        {
            get => Items?.FirstOrDefault(i => i.IsPrimary);
            set
            {
                if (value != null && SelectedPrimary != value)
                {
                    UpdatePrimaryItem(value);
                    OnPropertyChanged(nameof(SelectedPrimary));
                }
            }
        }

        /// <summary>
        /// Updates the designated primary item, resetting the primary flag on other items in the cluster.
        /// </summary>
        /// <param name="newPrimary">The new primary duplicate item.</param>
        public void UpdatePrimaryItem(DuplicateItemModel newPrimary)
        {
            if (Items == null) return;
            UnsubscribeFromItems();
            foreach (var item in Items)
            {
                item.IsPrimary = (item == newPrimary);
            }
            SubscribeToItems();
            OnPropertyChanged(nameof(SelectedPrimary));
        }

        #region Items Subscriptions

        private void SubscribeToItems()
        {
            if (Items != null)
            {
                foreach (var item in Items)
                {
                    item.PropertyChanged += OnItemPropertyChanged;
                }
                Items.CollectionChanged += OnItemsCollectionChanged;
            }
        }

        private void UnsubscribeFromItems()
        {
            if (Items != null)
            {
                foreach (var item in Items)
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                }
                Items.CollectionChanged -= OnItemsCollectionChanged;
            }
        }

        private void OnItemsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (DuplicateItemModel item in e.OldItems)
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (DuplicateItemModel item in e.NewItems)
                {
                    item.PropertyChanged += OnItemPropertyChanged;
                }
            }
            OnPropertyChanged(nameof(SelectedPrimary));
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DuplicateItemModel.IsPrimary))
            {
                if (sender is DuplicateItemModel updatedItem && updatedItem.IsPrimary)
                {
                    UnsubscribeFromItems();
                    foreach (var item in Items)
                    {
                        if (item != updatedItem)
                        {
                            item.IsPrimary = false;
                        }
                    }
                    SubscribeToItems();
                    OnPropertyChanged(nameof(SelectedPrimary));
                }
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and raises the PropertyChanged event if the value changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">Reference to the backing field.</param>
        /// <param name="value">The new value to set.</param>
        /// <param name="propertyName">The name of the property (automatically populated).</param>
        /// <returns>True if the value was modified; otherwise, false.</returns>
        protected virtual bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
