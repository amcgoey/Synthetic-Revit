using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Generic ViewModel for selecting a single item from a list.
    /// </summary>
    /// <typeparam name="T">The type of item to select.</typeparam>
    public class SingleItemSelectionViewModel<T> : ViewModelBase, ISingleItemSelectionViewModel
    {
        private string _title = "Select Item";
        private string _prompt = "Select an item from the list below:";
        private T? _selectedItem;
        private readonly Func<T, string> _displayMemberPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleItemSelectionViewModel{T}"/> class.
        /// </summary>
        /// <param name="items">The items available to select from.</param>
        /// <param name="prompt">The prompt message to display to the user.</param>
        /// <param name="displayMemberPath">A delegate to resolve the display name for each item.</param>
        public SingleItemSelectionViewModel(IEnumerable<T> items, string prompt, Func<T, string> displayMemberPath)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _displayMemberPath = displayMemberPath ?? throw new ArgumentNullException(nameof(displayMemberPath));
            _prompt = prompt;
            Items = new ObservableCollection<T>(items);
            OkCommand = new RelayCommand(OnOk, CanOk);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// Gets or sets the title of the selection window.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instructions or prompt shown to the user.
        /// </summary>
        public string Prompt
        {
            get => _prompt;
            set => SetProperty(ref _prompt, value);
        }

        /// <summary>
        /// Gets the collection of items to select from.
        /// </summary>
        public ObservableCollection<T> Items { get; }

        /// <summary>
        /// Gets or sets the selected item.
        /// </summary>
        public T? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        /// <summary>
        /// Gets or sets the action to close the window, passing a boolean dialog result.
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
            return SelectedItem != null;
        }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        /// <inheritdoc/>
        public string GetItemDisplayName(object item)
        {
            if (item is T typedItem)
            {
                return _displayMemberPath(typedItem);
            }
            return item?.ToString() ?? string.Empty;
        }
    }
}
