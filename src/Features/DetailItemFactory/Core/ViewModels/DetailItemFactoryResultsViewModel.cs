using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
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
            DetailItemTempCleanupSummary summary = DetailItemBatchPlanner.CleanupTempExportFiles(_outputFolder);

            if (!summary.DirectoryExists)
            {
                _userPromptService?.ShowMessage("The output folder does not exist.", "Detail Item Factory");
                return;
            }

            if (!string.IsNullOrEmpty(summary.EnumerationError))
            {
                _userPromptService?.ShowMessage(
                    $"An error occurred during DWG cleanup:\n{summary.EnumerationError}",
                    "Detail Item Factory - Error");
                return;
            }

            string msg = $"Garbage Collection Complete.\n\nSuccessfully deleted {summary.DeletedCount} temporary CAD file{(summary.DeletedCount == 1 ? "" : "s")} (.dwg/.pcp).";
            if (summary.FailedCount > 0)
            {
                msg += $"\nFailed to delete {summary.FailedCount} file{(summary.FailedCount == 1 ? "" : "s")} (possibly locked or in use).";
            }

            _userPromptService?.ShowMessage(msg, "Detail Item Factory - Cleanup");
        }

        private void ExecuteClose()
        {
            RequestClose?.Invoke();
        }
    }
}
