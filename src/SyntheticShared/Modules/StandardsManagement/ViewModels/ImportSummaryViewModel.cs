using System.Collections.ObjectModel;
using System.Linq;

using Synthetic.Modules.RevitDOM;
using Synthetic.Infrastructure.Serialization;

using Synthetic.Shared.UI;
using Synthetic.Modules.StandardsManagement.Models;
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
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Project Standards Consolidation Execution Report");
            sb.AppendLine();
            sb.AppendLine($"- **Date:** {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## Summary Statistics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Count |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| **Elements Created** | {CreatedCount} |");
            sb.AppendLine($"| **Elements Updated** | {UpdatedCount} |");
            sb.AppendLine($"| **Aliases Renamed** | {RenamedCount} |");
            sb.AppendLine($"| **Errors / Failed** | {ErrorsCount} |");
            sb.AppendLine();
            sb.AppendLine("## Detailed Execution Log");
            sb.AppendLine();
            sb.AppendLine("| Action | Class | Element Name | Details / Message |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var item in LogItems)
            {
                string action = item.Action ?? string.Empty;
                string cls = item.Class ?? string.Empty;
                string name = (item.ElementName ?? string.Empty).Replace("|", "\\|");
                string msg = (item.Message ?? string.Empty).Replace("|", "\\|");
                sb.AppendLine($"| {action} | {cls} | {name} | {msg} |");
            }
            return sb.ToString();
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
