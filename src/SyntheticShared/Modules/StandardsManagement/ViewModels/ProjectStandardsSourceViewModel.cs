using System;
using System.Collections.ObjectModel;
using Synthetic.Shared.UI;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// Represents a tabbed source standard file or Revit model context loaded inside the Project Standards workspace.
    /// </summary>
    public class ProjectStandardsSourceViewModel : ViewModelBase
    {
        private string _displayName = string.Empty;
        private string _sourcePath = string.Empty;
        private bool _isRevitSource;


        /// <summary>
        /// Gets or sets the display name shown on the source tab.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// Gets or sets the absolute path or identifier of the source data.
        /// </summary>
        public string SourcePath
        {
            get => _sourcePath;
            set => SetProperty(ref _sourcePath, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether this source is an active Revit model.
        /// </summary>
        public bool IsRevitSource
        {
            get => _isRevitSource;
            set => SetProperty(ref _isRevitSource, value);
        }



        /// <summary>
        /// Gets the hierarchical tree view dataset for this source.
        /// </summary>
        public ObservableCollection<StandardGroupModel> SourceHierarchy { get; } = new ObservableCollection<StandardGroupModel>();

        /// <summary>
        /// Gets the command to select all elements in the source hierarchy.
        /// </summary>
        public System.Windows.Input.ICommand SelectAllCommand { get; }

        /// <summary>
        /// Gets the command to deselect all elements in the source hierarchy.
        /// </summary>
        public System.Windows.Input.ICommand SelectNoneCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectStandardsSourceViewModel"/> class.
        /// </summary>
        public ProjectStandardsSourceViewModel()
        {
            SelectAllCommand = new RelayCommand(ExecuteSelectAll);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
        }

        private void ExecuteSelectAll(object parameter)
        {
            foreach (var group in SourceHierarchy)
            {
                group.IsChecked = true;
            }
        }

        private void ExecuteSelectNone(object parameter)
        {
            foreach (var group in SourceHierarchy)
            {
                group.IsChecked = false;
            }
        }
    }
}
