using System.Collections.Generic;
using Synthetic.Platform.UI;

namespace Synthetic.Modules.SharedCoordinates.Models
{
    /// <summary>
    /// Represents a linked model type and its instance counts for Shared Site Link Repositioning.
    /// </summary>
    public class LinkRepositionGroupItem : ViewModelBase
    {
        private string _typeId = string.Empty;
        private string _typeName = string.Empty;
        private int _totalInstanceCount;
        private int _outOfSyncCount;
        private int _unassignedCount;
        private bool _isChecked = true;

        /// <summary>
        /// Gets or sets the unique identifier of the RevitLinkType.
        /// </summary>
        public string TypeId
        {
            get => _typeId;
            set => SetProperty(ref _typeId, value);
        }

        /// <summary>
        /// Gets or sets the display name of the linked model type.
        /// </summary>
        public string TypeName
        {
            get => _typeName;
            set => SetProperty(ref _typeName, value);
        }

        /// <summary>
        /// Gets or sets the total number of loaded instances of this link type.
        /// </summary>
        public int TotalInstanceCount
        {
            get => _totalInstanceCount;
            set => SetProperty(ref _totalInstanceCount, value);
        }

        /// <summary>
        /// Gets or sets the number of instances whose placement transform is out of sync with their assigned Shared Site.
        /// </summary>
        public int OutOfSyncCount
        {
            get => _outOfSyncCount;
            set => SetProperty(ref _outOfSyncCount, value);
        }

        /// <summary>
        /// Gets or sets the number of instances with unassigned or not-shared sites.
        /// </summary>
        public int UnassignedCount
        {
            get => _unassignedCount;
            set => SetProperty(ref _unassignedCount, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this link group is selected for repositioning.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        /// <summary>
        /// Gets or sets the individual instance items comprising this link group.
        /// </summary>
        public List<LinkRepositionInstanceItem> Instances { get; set; } = new List<LinkRepositionInstanceItem>();
    }
}
