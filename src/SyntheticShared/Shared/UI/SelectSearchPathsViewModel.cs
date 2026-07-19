using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Shared.UI
{
    /// <summary>
    /// ViewModel for selecting search paths.
    /// </summary>
    public class SelectSearchPathsViewModel : ViewModelBase
    {
        private string _title = "Select Search Paths";
        private string _instruction = "Add and select the folder paths that you want to search for files.";
        private ObservableCollection<CheckableItem> _userPaths = new ObservableCollection<CheckableItem>();
        private ObservableCollection<CheckableItem> _defaultPaths = new ObservableCollection<CheckableItem>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectSearchPathsViewModel"/> class.
        /// </summary>
        public SelectSearchPathsViewModel()
        {
            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
            AddPathCommand = new RelayCommand(OnAddPath);
            SelectAllUserPathsCommand = new RelayCommand(OnSelectAllUserPaths);
            SelectNoneUserPathsCommand = new RelayCommand(OnSelectNoneUserPaths);
            SelectAllDefaultPathsCommand = new RelayCommand(OnSelectAllDefaultPaths);
            SelectNoneDefaultPathsCommand = new RelayCommand(OnSelectNoneDefaultPaths);
        }

        /// <summary>
        /// Gets or sets the title of the window.
        /// </summary>
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// Gets or sets the instruction text displayed to the user.
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
        /// Gets the visibility status of the instruction text.
        /// </summary>
        public Visibility InstructionVisibility =>
            string.IsNullOrEmpty(Instruction) ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>
        /// Gets or sets the list of search paths added by the user.
        /// </summary>
        public ObservableCollection<CheckableItem> UserPaths
        {
            get => _userPaths;
            set => SetProperty(ref _userPaths, value);
        }

        /// <summary>
        /// Gets or sets the list of default search paths.
        /// </summary>
        public ObservableCollection<CheckableItem> DefaultPaths
        {
            get => _defaultPaths;
            set => SetProperty(ref _defaultPaths, value);
        }

        /// <summary>
        /// Gets the list of checked search path strings.
        /// </summary>
        public List<string> CheckedItems
        {
            get
            {
                var result = new List<string>();
                result.AddRange(UserPaths.Where(x => x.IsChecked).Select(x => x.Name));
                result.AddRange(DefaultPaths.Where(x => x.IsChecked).Select(x => x.Name));
                return result;
            }
        }

        /// <summary>
        /// Checks all user search paths.
        /// </summary>
        public void CheckAllItems()
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = true;
            }
        }

        /// <summary>
        /// Checks all default search paths.
        /// </summary>
        public void CheckAllDefaults()
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = true;
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
        /// Gets the Add Path command.
        /// </summary>
        public ICommand AddPathCommand { get; }

        /// <summary>
        /// Gets the command to select all user paths.
        /// </summary>
        public ICommand SelectAllUserPathsCommand { get; }

        /// <summary>
        /// Gets the command to deselect all user paths.
        /// </summary>
        public ICommand SelectNoneUserPathsCommand { get; }

        /// <summary>
        /// Gets the command to select all default paths.
        /// </summary>
        public ICommand SelectAllDefaultPathsCommand { get; }

        /// <summary>
        /// Gets the command to deselect all default paths.
        /// </summary>
        public ICommand SelectNoneDefaultPathsCommand { get; }

        private void OnOk(object parameter)
        {
            CloseAction?.Invoke(true);
        }

        private void OnCancel(object parameter)
        {
            CloseAction?.Invoke(false);
        }

        private void OnAddPath(object parameter)
        {
            IntPtr ownerHandle = IntPtr.Zero;
            if (parameter is Window window)
            {
                ownerHandle = new WindowInteropHelper(window).Handle;
            }

            string selected = FileDialogHelper.SelectFolder(ownerHandle, "Select Search Path to Add");
            if (!string.IsNullOrEmpty(selected))
            {
                // Prevent adding duplicate paths to the user list
                if (!UserPaths.Any(x => string.Equals(x.Name, selected, StringComparison.OrdinalIgnoreCase)))
                {
                    UserPaths.Add(new CheckableItem(selected, true));
                }
            }
        }

        private void OnSelectAllUserPaths(object parameter)
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = true;
            }
        }

        private void OnSelectNoneUserPaths(object parameter)
        {
            foreach (var item in UserPaths)
            {
                item.IsChecked = false;
            }
        }

        private void OnSelectAllDefaultPaths(object parameter)
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = true;
            }
        }

        private void OnSelectNoneDefaultPaths(object parameter)
        {
            foreach (var item in DefaultPaths)
            {
                item.IsChecked = false;
            }
        }
    }
}
