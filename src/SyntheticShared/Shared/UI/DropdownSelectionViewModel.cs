using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for dropdown selection views.
    /// </summary>
    public class DropdownSelectionViewModel : ViewModelBase
    {
        private string _title = "Title";
        private string _instruction = "Instructions";
        private string _itemLabel = "Label";
        private IEnumerable<string> _items = new List<string>();
        private string? _selectedItem;
        private bool _isSorted;

        /// <summary>
        /// Initializes a new instance of the <see cref="DropdownSelectionViewModel"/> class.
        /// </summary>
        public DropdownSelectionViewModel()
        {
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets the title of the view.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instructions shown to the user.
        /// </summary>
        public string Instruction
        {
            get => _instruction;
            set => SetProperty(ref _instruction, value);
        }

        /// <summary>
        /// Gets or sets the label for the dropdown.
        /// </summary>
        public string ItemLabel
        {
            get => _itemLabel;
            set => SetProperty(ref _itemLabel, value);
        }

        /// <summary>
        /// Gets or sets the items in the dropdown list.
        /// </summary>
        public IEnumerable<string> Items
        {
            get => _items;
            set
            {
                var list = value ?? new List<string>();
                if (_isSorted)
                {
                    list = list.OrderBy(x => x).ToList();
                }
                SetProperty(ref _items, list);
                
                // Select first item by default if there are items
                if (list.Any())
                {
                    SelectedItem = list.First();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected item from the dropdown list.
        /// </summary>
        public string? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether items are sorted alphabetically.
        /// </summary>
        public bool IsSorted
        {
            get => _isSorted;
            set
            {
                if (SetProperty(ref _isSorted, value) && _items != null)
                {
                    Items = _items; // Trigger re-sorting
                }
            }
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean result.
        /// </summary>
        public Action<bool>? CloseAction { get; set; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        private bool CanOk(object parameter)
        {
            return !string.IsNullOrEmpty(SelectedItem);
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }
    }
}
