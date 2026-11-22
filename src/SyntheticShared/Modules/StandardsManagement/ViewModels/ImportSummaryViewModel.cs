using System.Collections.ObjectModel;
using System.Linq;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.RevitDOM.Operations.Standards;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// ViewModel for the import summary view, summarizing the results of an import operation.
    /// </summary>
    public class ImportSummaryViewModel : ViewModelBase
    {
        /// <summary>
        /// Gets the collection of import log items.
        /// </summary>
        public ObservableCollection<ImportLogItem> LogItems { get; }

        /// <summary>
        /// Gets the count of items that were created during import.
        /// </summary>
        public int CreatedCount => LogItems.Count(item => item.Action == "Created");

        /// <summary>
        /// Gets the count of items that were updated during import.
        /// </summary>
        public int UpdatedCount => LogItems.Count(item => item.Action == "Updated");

        /// <summary>
        /// Gets the count of items that were renamed during import.
        /// </summary>
        public int RenamedCount => LogItems.Count(item => item.Action == "Renamed");

        /// <summary>
        /// Gets the count of items that were unchanged during import.
        /// </summary>
        public int UnchangedCount => LogItems.Count(item => item.Action == "Unchanged");

        private readonly IFileDialogService? _dialogService;

        /// <summary>
        /// Gets the count of failed import operations.
        /// </summary>
        public int ErrorsCount => LogItems.Count(item => item.Action == "Failed");

        /// <summary>
        /// Gets the command to export the execution log to a markdown file.
        /// </summary>
        public System.Windows.Input.ICommand ExportLogCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImportSummaryViewModel"/> class.
        /// </summary>
        /// <param name="logItems">The list of import log items to summarize.</param>
        /// <param name="dialogService">The dialog service for choosing file save paths.</param>
        public ImportSummaryViewModel(ObservableCollection<ImportLogItem> logItems, IFileDialogService? dialogService = null)
        {
            LogItems = logItems;
            _dialogService = dialogService;
            ExportLogCommand = new RelayCommand(ExecuteExportLog, CanExecuteExportLog);
        }

        /// <summary>
        /// Generates a formatted markdown report from the summary log items.
        /// </summary>
        public string GenerateMarkdown()
        {
            return StandardsReportGenerator.GenerateMarkdown(LogItems);
        }

        private void ExecuteExportLog(object parameter)
        {
            if (_dialogService == null) return;

            string? path = _dialogService.SaveFileDialog("Markdown Files (*.md)|*.md", "Export Execution Log", "ExecutionSummary.md");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    string markdown = GenerateMarkdown();
                    System.IO.File.WriteAllText(path, markdown);
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"Failed to write markdown log: {ex.Message}");
                }
            }
        }

        private bool CanExecuteExportLog(object parameter)
        {
            return _dialogService != null && LogItems != null && LogItems.Count > 0;
        }
    }
}
