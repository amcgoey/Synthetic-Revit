using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.ViewModels
{
    /// <summary>
    /// Headless ViewModel for the Detail Item Factory batch results dashboard.
    /// Provides result items and manual temporary DWG/PCP cleanup using <see cref="IUserPromptService"/>.
    /// </summary>
    public class DetailItemFactoryResultsViewModel : ViewModelBase
    {
        private readonly string _outputFolder;
        private readonly IUserPromptService? _userPromptService;

        /// <summary>
        /// Gets the collection of element conversion results.
        /// </summary>
        public ObservableCollection<DetailItemResultItem> Results { get; }

        /// <summary>
        /// Gets the command to delete temporary DWG and PCP files from the output folder.
        /// </summary>
        public ICommand DeleteTempDwgsCommand { get; }

        /// <summary>
        /// Gets the command to close the results window.
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Event raised when the ViewModel requests window closure.
        /// </summary>
        public event Action? RequestClose;

        /// <summary>
        /// Legacy close delegate cleared automatically when the hosting Window closes.
        /// </summary>
        public Action? CloseAction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactoryResultsViewModel"/> class.
        /// </summary>
        /// <param name="outputFolder">The output folder path where files were saved.</param>
        /// <param name="results">The list of execution result items.</param>
        /// <param name="userPromptService">Optional prompt service for reporting manual cleanup feedback.</param>
        public DetailItemFactoryResultsViewModel(
            string outputFolder,
            IEnumerable<DetailItemResultItem> results,
            IUserPromptService? userPromptService = null)
        {
            _outputFolder = outputFolder ?? throw new ArgumentNullException(nameof(outputFolder));
            _userPromptService = userPromptService;
            Results = new ObservableCollection<DetailItemResultItem>(results ?? Array.Empty<DetailItemResultItem>());

            DeleteTempDwgsCommand = new RelayCommand(_ => ExecuteDeleteTempDwgs());
            CloseCommand = new RelayCommand(_ => ExecuteClose());
        }

        private void ExecuteDeleteTempDwgs()
        {
            if (string.IsNullOrWhiteSpace(_outputFolder) || !Directory.Exists(_outputFolder))
            {
                _userPromptService?.ShowMessage("The output folder does not exist.", "Detail Item Factory");
                return;
            }

            int deletedCount = 0;
            int failedCount = 0;

            try
            {
                var files = new List<string>();
                files.AddRange(Directory.GetFiles(_outputFolder, "*.dwg"));
                files.AddRange(Directory.GetFiles(_outputFolder, "*.pcp"));

                foreach (string file in files)
                {
                    string filename = Path.GetFileName(file);
                    if (filename.StartsWith("temp_export_", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception)
                        {
                            failedCount++;
                        }
                    }
                }

                string msg = $"Garbage Collection Complete.\n\nSuccessfully deleted {deletedCount} temporary CAD file{(deletedCount == 1 ? "" : "s")} (.dwg/.pcp).";
                if (failedCount > 0)
                {
                    msg += $"\nFailed to delete {failedCount} file{(failedCount == 1 ? "" : "s")} (possibly locked or in use).";
                }

                _userPromptService?.ShowMessage(msg, "Detail Item Factory - Cleanup");
            }
            catch (Exception ex)
            {
                _userPromptService?.ShowMessage(
                    $"An error occurred during DWG cleanup:\n{ex.Message}",
                    "Detail Item Factory - Error");
            }
        }

        private void ExecuteClose()
        {
            RequestClose?.Invoke();
            CloseAction?.Invoke();
        }
    }
}
