using System.Diagnostics;
using System.IO;

namespace Synthetic.Features.MergeDuplicates.Utilities
{
    /// <summary>
    /// Resolves isolated diagnostic file paths for duplicate-merge execution.
    /// </summary>
    public static class MergeDiagnosticPaths
    {
        /// <summary>
        /// Resolves a per-process deletion error diagnostic file path inside <c>%TEMP%</c>.
        /// </summary>
        /// <param name="processId">Optional process ID override for deterministic testing.</param>
        /// <returns>The full path to the process-scoped deletion error log file.</returns>
        public static string GetDeletionErrorLogPath(int? processId = null)
        {
            int resolvedPid;
            if (processId.HasValue)
            {
                resolvedPid = processId.Value;
            }
            else
            {
                using (Process currentProcess = Process.GetCurrentProcess())
                {
                    resolvedPid = currentProcess.Id;
                }
            }

            return Path.Combine(
                Path.GetTempPath(),
                $"synthetic_merge_deletion_error_{resolvedPid}.txt");
        }
    }
}
