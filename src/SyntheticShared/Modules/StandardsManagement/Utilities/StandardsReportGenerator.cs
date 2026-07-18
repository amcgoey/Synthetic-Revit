using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Synthetic.Modules.StandardsManagement.Models;

namespace Synthetic.Modules.StandardsManagement.Utilities
{
    /// <summary>
    /// Utility class for generating standards reports.
    /// </summary>
    public static class StandardsReportGenerator
    {
        /// <summary>
        /// Generates a formatted markdown report from the summary log items.
        /// </summary>
        /// <param name="logItems">The list of import log items to summarize.</param>
        /// <returns>A markdown formatted string.</returns>
        public static string GenerateMarkdown(IEnumerable<ImportLogItem> logItems)
        {
            if (logItems == null) throw new ArgumentNullException(nameof(logItems));

            var logList = logItems.ToList();
            int createdCount = logList.Count(item => item.Action == "Created");
            int updatedCount = logList.Count(item => item.Action == "Updated");
            int renamedCount = logList.Count(item => item.Action == "Renamed");
            int unchangedCount = logList.Count(item => item.Action == "Unchanged");
            int errorsCount = logList.Count(item => item.Action == "Failed");

            var sb = new StringBuilder();
            sb.AppendLine("# Project Standards Consolidation Execution Report");
            sb.AppendLine();
            sb.AppendLine($"- **Date:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## Summary Statistics");
            sb.AppendLine();
            sb.AppendLine("| Metric | Count |");
            sb.AppendLine("| :--- | :--- |");
            sb.AppendLine($"| **Elements Created** | {createdCount} |");
            sb.AppendLine($"| **Elements Updated** | {updatedCount} |");
            sb.AppendLine($"| **Elements Unchanged** | {unchangedCount} |");
            sb.AppendLine($"| **Aliases Renamed** | {renamedCount} |");
            sb.AppendLine($"| **Errors / Failed** | {errorsCount} |");
            sb.AppendLine();
            sb.AppendLine("## Detailed Execution Log");
            sb.AppendLine();
            sb.AppendLine("| Action | Class | Element Name | Details / Message |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");
            foreach (var item in logList)
            {
                string action = item.Action ?? string.Empty;
                string cls = item.Class ?? string.Empty;
                string name = (item.ElementName ?? string.Empty).Replace("|", "\\|");
                string msg = (item.Message ?? string.Empty).Replace("|", "\\|");
                sb.AppendLine($"| {action} | {cls} | {name} | {msg} |");
            }
            return sb.ToString();
        }
    }
}
