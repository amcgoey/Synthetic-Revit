using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;

using Synthetic.Shared.UI;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for lists where items can be selected using checkboxes.
    /// </summary>
    public class ListByCheckboxViewModel : ViewModelBase
    {
        private string _title = "Title";
        private string _instruction = "Instructions";
        private ObservableCollection<CheckableItem> _items = new ObservableCollection<CheckableItem>();
        private bool _isSorted = true;
        private bool _isSingleSelection;
        private bool _isUpdating;

        /// <summary>
        /// Initializes a new instance of the <see cref="ListByCheckboxViewModel"/> class.
        /// </summary>
        public ListByCheckboxViewModel()
        {
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
            SelectAllCommand = new RelayCommand(OnSelectAll, CanSelectAll);
            SelectNoneCommand = new RelayCommand(OnSelectNone, CanSelectNone);
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
            set
            {
                if (SetProperty(ref _instruction, value))
                {
                    OnPropertyChanged(nameof(InstructionVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the visibility status of the instructions.
        /// </summary>
        public System.Windows.Visibility InstructionVisibility => 
            string.IsNullOrEmpty(Instruction) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        /// <summary>
        /// Gets or sets a value indicating whether items are sorted alphabetically.
        /// </summary>
        public bool IsSorted
        {
            get => _isSorted;
            set => SetProperty(ref _isSorted, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether only one item can be selected at a time.
        /// </summary>
        public bool IsSingleSelection
        {
            get => _isSingleSelection;
            set
            {
                if (SetProperty(ref _isSingleSelection, value))
                {
                    OnPropertyChanged(nameof(SelectAllVisibility));
                }
            }
        }

        /// <summary>
        /// Gets the visibility status of the select all command.
        /// </summary>
        public System.Windows.Visibility SelectAllVisibility =>
            IsSingleSelection ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        /// <summary>
        /// Gets or sets the observable collection of checkable items.
        /// </summary>
        public ObservableCollection<CheckableItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// Gets the list of names of items that are checked.
        /// </summary>
        public List<string> CheckedItems
        {
            get
            {
                return Items.Where(i => i.IsChecked).Select(i => i.Name).ToList();
            }
        }

        /// <summary>
        /// Gets the list of ElementIds of items that are checked.
        /// </summary>
        public List<ElementId> CheckedElementIds
        {
            get
            {
                return Items.Where(i => i.IsChecked && i.Value != null).Select(i => i.Value!).ToList();
            }
        }

        /// <summary>
        /// Sets the checkable items from a list of item names.
        /// </summary>
        /// <param name="itemNames">The collection of item names.</param>
        /// <param name="checkAll">Whether to check all items by default.</param>
        public void SetItems(IEnumerable<string> itemNames, bool checkAll = false)
        {
            _isUpdating = true;
            Items.Clear();
            
            var list = itemNames ?? new List<string>();
            if (IsSorted)
            {
                list = list.OrderBy(x => x).ToList();
            }

            foreach (var name in list)
            {
                Items.Add(new CheckableItem(name, checkAll, OnItemCheckChanged));
            }
            _isUpdating = false;
        }

        /// <summary>
        /// Sets the checkable items from a list of tuples containing item names and ElementIds.
        /// </summary>
        /// <param name="items">The collection of tuples containing name and ElementId.</param>
        /// <param name="checkAll">Whether to check all items by default.</param>
        public void SetItems(IEnumerable<Tuple<string, ElementId>> items, bool checkAll = false)
        {
            _isUpdating = true;
            Items.Clear();

            var list = items ?? new List<Tuple<string, ElementId>>();
            if (IsSorted)
            {
                list = list.OrderBy(x => x.Item1).ToList();
            }

            foreach (var item in list)
            {
                Items.Add(new CheckableItem(item.Item1, item.Item2, checkAll, OnItemCheckChanged));
            }
            _isUpdating = false;
        }

        private void OnItemCheckChanged(CheckableItem changedItem)
        {
            if (_isUpdating) return;

            if (IsSingleSelection && changedItem.IsChecked)
            {
                _isUpdating = true;
                foreach (var item in Items)
                {
                    if (item != changedItem)
                    {
                        item.IsChecked = false;
                    }
                }
                _isUpdating = false;
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

        /// <summary>
        /// Gets the Select All command.
        /// </summary>
        public ICommand SelectAllCommand { get; }

        /// <summary>
        /// Gets the Select None command.
        /// </summary>
        public ICommand SelectNoneCommand { get; }

        private bool CanOk(object parameter)
        {
            return Items.Any(i => i.IsChecked);
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        private bool CanSelectAll(object parameter) => !IsSingleSelection;
        private void OnSelectAll(object parameter)
        {
            _isUpdating = true;
            foreach (var item in Items)
            {
                item.IsChecked = true;
            }
            _isUpdating = false;
        }

        private bool CanSelectNone(object parameter) => true;
        private void OnSelectNone(object parameter)
        {
            _isUpdating = true;
            foreach (var item in Items)
            {
                item.IsChecked = false;
            }
            _isUpdating = false;
        }
    }

    /// <summary>
    /// Represents an item in a checklist that can be checked or unchecked.
    /// </summary>
    public class CheckableItem : ViewModelBase
    {
        private string _name = string.Empty;
        private ElementId? _value;
        private bool _isChecked;
        private readonly Action<CheckableItem>? _onCheckChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckableItem"/> class.
        /// </summary>
        /// <param name="name">The display name of the item.</param>
        /// <param name="isChecked">The initial check state of the item.</param>
        /// <param name="onCheckChanged">Action to invoke when checked status changes.</param>
        public CheckableItem(string name, bool isChecked = false, Action<CheckableItem>? onCheckChanged = null)
        {
            _name = name;
            _isChecked = isChecked;
            _onCheckChanged = onCheckChanged;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckableItem"/> class.
        /// </summary>
        /// <param name="name">The display name of the item.</param>
        /// <param name="value">The ElementId associated with the item.</param>
        /// <param name="isChecked">The initial check state of the item.</param>
        /// <param name="onCheckChanged">Action to invoke when checked status changes.</param>
        public CheckableItem(string name, ElementId? value, bool isChecked = false, Action<CheckableItem>? onCheckChanged = null)
        {
            _name = name;
            _value = value;
            _isChecked = isChecked;
            _onCheckChanged = onCheckChanged;
        }

        /// <summary>
        /// Gets or sets the display name of the item.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Gets or sets the ElementId value associated with the item.
        /// </summary>
        public ElementId? Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the item is checked.
        /// </summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                {
                    _onCheckChanged?.Invoke(this);
                }
            }
        }
    }
}
