using System;
using System.Collections.ObjectModel;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Base ViewModel class for items in the hierarchical standards selection TreeView.
    /// Manages cascading checked/unchecked/indeterminate states recursively.
    /// </summary>
    public abstract class SourceTreeItemViewModel : ViewModelBase
    {
        private string _name = string.Empty;
        private bool? _isChecked = false;
        private SourceTreeItemViewModel? _parent;

        /// <summary>
        /// Gets or sets the display name of the tree node.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Gets or sets the checked state of the node.
        /// Supports nullable bool (true = Checked, false = Unchecked, null = Indeterminate).
        /// </summary>
        public bool? IsChecked
        {
            get => _isChecked;
            set => SetIsChecked(value, true, true);
        }

        private bool _isVisible = true;
        private bool _isExpanded;

        /// <summary>
        /// Gets or sets a value indicating whether this tree node is visible.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this tree node is expanded.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// Gets or sets the parent node reference.
        /// </summary>
        public SourceTreeItemViewModel? Parent
        {
            get => _parent;
            set => _parent = value;
        }

        /// <summary>
        /// Gets the children of this node.
        /// </summary>
        public ObservableCollection<SourceTreeItemViewModel> Children { get; } = new ObservableCollection<SourceTreeItemViewModel>();

        /// <summary>
        /// Updates the checkbox state, propagating updates to parents or children recursively.
        /// </summary>
        public void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
        {
            if (_isChecked == value) return;

            _isChecked = value;
            OnPropertyChanged(nameof(IsChecked));

            if (updateChildren && value.HasValue)
            {
                foreach (var child in Children)
                {
                    child.SetIsChecked(value, true, false);
                }
            }

            if (updateParent && Parent != null)
            {
                Parent.VerifyCheckedState();
            }
        }

        /// <summary>
        /// Examines child states to update this parent node's checked state accordingly.
        /// </summary>
        public void VerifyCheckedState()
        {
            bool? state = null;

            if (Children.Count > 0)
            {
                bool allChecked = true;
                bool allUnchecked = true;

                foreach (var child in Children)
                {
                    if (child.IsChecked == true)
                    {
                        allUnchecked = false;
                    }
                    else if (child.IsChecked == false)
                    {
                        allChecked = false;
                    }
                    else
                    {
                        allChecked = false;
                        allUnchecked = false;
                    }
                }

                if (allChecked) state = true;
                else if (allUnchecked) state = false;
                else state = null; // Indeterminate
            }

            SetIsChecked(state, false, true);
        }
    }
}
