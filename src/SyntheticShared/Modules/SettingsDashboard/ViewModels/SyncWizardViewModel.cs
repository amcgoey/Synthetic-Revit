using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Synthetic.Settings;
using Synthetic.Infrastructure.Persistence;

using Synthetic.Shared.UI;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Modules.SettingsDashboard.ViewModels
{
    /// <summary>
    /// ViewModel for the Sync Settings Configuration Wizard.
    /// </summary>
    public class SyncWizardViewModel : ViewModelBase
    {
        private string _linkedFilePath;
        private readonly IFileDialogService _fileDialogService;

        /// <summary>
        /// Gets or sets the path to the linked settings configuration JSON file.
        /// </summary>
        public string LinkedFilePath
        {
            get => _linkedFilePath;
            set
            {
                if (SetProperty(ref _linkedFilePath, value))
                {
                    OnPropertyChanged(nameof(IsPathValid));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the selected linked path is valid.
        /// </summary>
        public bool IsPathValid => string.IsNullOrEmpty(LinkedFilePath) || File.Exists(LinkedFilePath);

        /// <summary>
        /// Gets the browse command.
        /// </summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Gets the OK command.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the Cancel command.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncWizardViewModel"/> class.
        /// </summary>
        /// <param name="settings">The sync settings to edit.</param>
        public SyncWizardViewModel(SyncSettings settings, IFileDialogService? fileDialogService = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _linkedFilePath = settings.LinkedFilePath;
            _fileDialogService = fileDialogService ?? new WindowsFileDialogService();

            BrowseCommand = new RelayCommand(OnBrowse);
            OkCommand = new RelayCommand(OnOk, _ => CanOk());
            CancelCommand = new RelayCommand(OnCancel);
        }

        private void OnBrowse(object parameter)
        {
            var filePath = _fileDialogService.OpenFileDialog("JSON Files (*.json)|*.json|All Files (*.*)|*.*", "Select Linked Settings JSON File", "");
            if (!string.IsNullOrEmpty(filePath))
            {
                LinkedFilePath = filePath;
            }
        }

        private bool CanOk()
        {
            return !string.IsNullOrEmpty(LinkedFilePath) && File.Exists(LinkedFilePath);
        }

        private void OnOk(object parameter)
        {
            if (parameter is Window window)
            {
                window.DialogResult = true;
                window.Close();
            }
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
