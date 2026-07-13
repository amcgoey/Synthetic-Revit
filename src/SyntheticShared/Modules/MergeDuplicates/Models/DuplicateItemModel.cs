using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;

using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.MergeDuplicates.Models
{
    /// <summary>
    /// Represents the parent container (e.g., the Family or parent element) for duplicates.
    /// </summary>
    public class DuplicateItemModel : ObjectModel, INotifyPropertyChanged
    {
        private ElementId? _revitElementId;
        private string _itemName = string.Empty;
        private string _categoryName = string.Empty;
        private ObservableCollection<DuplicateTypeModel> _types;
        private bool _isPrimary;
        private bool _isIncludedForMerge = true;
        private BoundingBoxXYZ? _boundingBox;
        private XYZ? _origin;

        // Compatibility fields
        private Dictionary<string, string> _parameters;
        private int _instanceCount;

        /// <summary>
        /// Initializes a new instance of the DuplicateItemModel class.
        /// </summary>
        public DuplicateItemModel()
        {
            _types = new ObservableCollection<DuplicateTypeModel>();
            _parameters = new Dictionary<string, string>();
        }

        /// <summary>
        /// The Revit ElementId of this parent item.
        /// </summary>
        public ElementId? RevitElementId
        {
            get => _revitElementId;
            set => SetProperty(ref _revitElementId, value);
        }

        /// <summary>
        /// The name of this item.
        /// </summary>
        public string ItemName
        {
            get => _itemName;
            set => SetProperty(ref _itemName, value);
        }

        /// <summary>
        /// The Revit category name.
        /// </summary>
        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        /// <summary>
        /// Nested collection of types belonging to this container.
        /// </summary>
        public ObservableCollection<DuplicateTypeModel> Types
        {
            get => _types;
            set => SetProperty(ref _types, value);
        }

        /// <summary>
        /// Whether this item is chosen as the primary survivor.
        /// </summary>
        public bool IsPrimary
        {
            get => _isPrimary;
            set => SetProperty(ref _isPrimary, value);
        }

        /// <summary>
        /// Whether this item is included for merge processing.
        /// </summary>
        public bool IsIncludedForMerge
        {
            get => _isIncludedForMerge;
            set => SetProperty(ref _isIncludedForMerge, value);
        }

        private bool _isLoadableFamily;

        /// <summary>
        /// True if this item represents a loadable Family element in Revit.
        /// </summary>
        public bool IsLoadableFamily
        {
            get => _isLoadableFamily;
            set => SetProperty(ref _isLoadableFamily, value);
        }

        /// <summary>
        /// Geometric bounding box representation.
        /// </summary>
        public BoundingBoxXYZ? BoundingBox
        {
            get => _boundingBox;
            set => SetProperty(ref _boundingBox, value);
        }

        /// <summary>
        /// Geometric origin point (corresponds to Location).
        /// </summary>
        public XYZ? Origin
        {
            get => _origin;
            set => SetProperty(ref _origin, value);
        }

        #region Compatibility Properties

        /// <summary>
        /// Compatibility wrapper for Location mapping to Origin.
        /// </summary>
        public XYZ? Location
        {
            get => _origin;
            set => SetProperty(ref _origin, value);
        }

        /// <summary>
        /// Compatibility parameters dictionary.
        /// </summary>
        public Dictionary<string, string> Parameters
        {
            get => _parameters;
            set => SetProperty(ref _parameters, value);
        }

        /// <summary>
        /// Compatibility instance count.
        /// </summary>
        public int InstanceCount
        {
            get => _instanceCount;
            set => SetProperty(ref _instanceCount, value);
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
