using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Core;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Network Paths and File Utility Configuration Wizard.
    /// </summary>
    public class NetworkPathsWizardViewModel : ViewModelBase
    {
        private readonly IntPtr _mainWindowHandle;
        private string? _selectedArchiveDirectory;
        private PathMappingViewModel? _selectedMapping;
        private bool _isValidating;

        /// <summary>
        /// Gets the collection of archive directories.
        /// </summary>
        public ObservableCollection<string> ArchiveDirectories { get; }

        /// <summary>
        /// Gets the collection of alternate paths mapping views.
        /// </summary>
        public ObservableCollection<PathMappingViewModel> AlternateMappings { get; }

        /// <summary>
        /// Gets or sets the selected archive directory path.
        /// </summary>
        public string? SelectedArchiveDirectory
        {
            get => _selectedArchiveDirectory;
            set => SetProperty(ref _selectedArchiveDirectory, value);
        }

        /// <summary>
        /// Gets or sets the selected path mapping item.
        /// </summary>
        public PathMappingViewModel? SelectedMapping
        {
            get => _selectedMapping;
            set => SetProperty(ref _selectedMapping, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether paths are currently being validated in the background.
        /// </summary>
        public bool IsValidating
        {
            get => _isValidating;
            set => SetProperty(ref _isValidating, value);
        }

        /// <summary>
        /// Gets the command to add a new archive directory.
        /// </summary>
        public ICommand AddArchiveCommand { get; }

        /// <summary>
        /// Gets the command to remove the selected archive directory.
        /// </summary>
        public ICommand RemoveArchiveCommand { get; }

        /// <summary>
        /// Gets the command to browse and select an archive directory.
        /// </summary>
        public ICommand BrowseArchiveCommand { get; }

        /// <summary>
        /// Gets the command to add a new alternate path mapping row.
        /// </summary>
        public ICommand AddMappingCommand { get; }

        /// <summary>
        /// Gets the command to remove the selected alternate path mapping row.
        /// </summary>
        public ICommand RemoveMappingCommand { get; }

        /// <summary>
        /// Gets the command to browse and select a local mapped path.
        /// </summary>
        public ICommand BrowseMappingCommand { get; }

        /// <summary>
        /// Gets the command to validate paths and close the wizard on success.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command to cancel editing and close the wizard.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkPathsWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The initial settings copy to edit.</param>
        /// <param name="mainWindowHandle">The parent main window handle.</param>
        public NetworkPathsWizardViewModel(FileUtilitySettings settings, IntPtr mainWindowHandle)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _mainWindowHandle = mainWindowHandle;

            ArchiveDirectories = new ObservableCollection<string>(settings.ArchiveDirectories ?? new List<string>());
            AlternateMappings = new ObservableCollection<PathMappingViewModel>();

            if (settings.AlternatePaths != null)
            {
                foreach (var kvp in settings.AlternatePaths)
                {
                    string mapped = kvp.Value != null && kvp.Value.Count > 0 ? kvp.Value[0] : string.Empty;
                    AlternateMappings.Add(new PathMappingViewModel(kvp.Key, mapped));
                }
            }

            // Bind Commands
            AddArchiveCommand = new RelayCommand(OnAddArchive);
            RemoveArchiveCommand = new RelayCommand(OnRemoveArchive, _ => !string.IsNullOrEmpty(SelectedArchiveDirectory));
            BrowseArchiveCommand = new RelayCommand(OnBrowseArchive, _ => !string.IsNullOrEmpty(SelectedArchiveDirectory));

            AddMappingCommand = new RelayCommand(OnAddMapping);
            RemoveMappingCommand = new RelayCommand(OnRemoveMapping, _ => SelectedMapping != null);
            BrowseMappingCommand = new RelayCommand(OnBrowseMapping, _ => SelectedMapping != null);

            OkCommand = new RelayCommand(OnOk, _ => !IsValidating);
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void OnAddArchive(object parameter)
        {
            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Archive Directory");
            if (!string.IsNullOrEmpty(selected))
            {
                ArchiveDirectories.Add(selected);
                SelectedArchiveDirectory = selected;
            }
        }

        private void OnRemoveArchive(object parameter)
        {
            if (!string.IsNullOrEmpty(SelectedArchiveDirectory))
            {
                ArchiveDirectories.Remove(SelectedArchiveDirectory!);
                SelectedArchiveDirectory = ArchiveDirectories.FirstOrDefault();
            }
        }

        private void OnBrowseArchive(object parameter)
        {
            if (string.IsNullOrEmpty(SelectedArchiveDirectory)) return;

            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Browse Archive Directory", SelectedArchiveDirectory);
            if (!string.IsNullOrEmpty(selected))
            {
                int index = ArchiveDirectories.IndexOf(SelectedArchiveDirectory!);
                if (index >= 0)
                {
                    ArchiveDirectories[index] = selected;
                    SelectedArchiveDirectory = selected;
                }
            }
        }

        private void OnAddMapping(object parameter)
        {
            var newMapping = new PathMappingViewModel();
            AlternateMappings.Add(newMapping);
            SelectedMapping = newMapping;
        }

        private void OnRemoveMapping(object parameter)
        {
            if (SelectedMapping != null)
            {
                AlternateMappings.Remove(SelectedMapping);
                SelectedMapping = AlternateMappings.FirstOrDefault();
            }
        }

        private void OnBrowseMapping(object parameter)
        {
            if (SelectedMapping == null) return;

            string initialPath = SelectedMapping.LocalMappedPath;
            string selected = FileDialogHelper.SelectFolder(_mainWindowHandle, "Select Local Mapped Path", initialPath);
            if (!string.IsNullOrEmpty(selected))
            {
                SelectedMapping.LocalMappedPath = selected;
            }
        }

        private void OnOk(object parameter)
        {
            IsValidating = true;
            Task.Run(() =>
            {
                var pathsToCheck = new List<string>();

                // Gather archive paths
                foreach (var dir in ArchiveDirectories)
                {
                    if (!string.IsNullOrEmpty(dir))
                    {
                        pathsToCheck.Add(dir);
                    }
                }

                // Gather alternate mappings paths (we validate local mapped paths or original server paths if they are UNC/existing)
                foreach (var mapping in AlternateMappings)
                {
                    if (!string.IsNullOrEmpty(mapping.LocalMappedPath))
                    {
                        pathsToCheck.Add(mapping.LocalMappedPath);
                    }
                }

                bool anyOffline = false;
                foreach (var path in pathsToCheck.Distinct())
                {
                    try
                    {
                        if (!Directory.Exists(path))
                        {
                            anyOffline = true;
                            break;
                        }
                    }
                    catch
                    {
                        anyOffline = true;
                        break;
                    }
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsValidating = false;

                    if (anyOffline)
                    {
                        var dialog = new Autodesk.Revit.UI.TaskDialog("Network Path Validation")
                        {
                            MainInstruction = "One or more network paths cannot be found.",
                            MainContent = "They may be offline or typed incorrectly.\n\nDo you want to save them anyway?",
                            CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.No,
                            DefaultButton = Autodesk.Revit.UI.TaskDialogResult.No
                        };

                        var result = dialog.Show();
                        if (result == Autodesk.Revit.UI.TaskDialogResult.No)
                        {
                            return; // Keep window open so user can adjust paths
                        }
                    }

                    // Success or override save: close window
                    if (parameter is Window window)
                    {
                        window.DialogResult = true;
                        window.Close();
                    }
                });
            });
        }

        private void OnCancel(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }
}
