using System;
using System.IO;

namespace Synthetic.Platform.IO
{
    /// <summary>
    /// Resolves user-local diagnostic log directories and file paths under <c>%LOCALAPPDATA%\Synthetic\Logs</c>.
    /// </summary>
    public static class DiagnosticLogPaths
    {
        /// <summary>
        /// The default filename for MergeDuplicates Step 6 execution result logs.
        /// </summary>
        public const string Step6ResultsLogFileName = "MergeDuplicates_Step6_ExecutionResult.json";

        /// <summary>
        /// Resolves the user-local diagnostic logs directory (<c>%LOCALAPPDATA%\Synthetic\Logs</c>).
        /// </summary>
        /// <param name="localAppDataOverride">Optional local AppData root directory override for parallel test isolation.</param>
        /// <returns>The resolved logs directory path, or an empty string if local AppData cannot be resolved.</returns>
        public static string ResolveLogsDir(string? localAppDataOverride = null)
        {
            string localAppData = !string.IsNullOrWhiteSpace(localAppDataOverride)
                ? localAppDataOverride!
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                return string.Empty;
            }

            return Path.Combine(localAppData, "Synthetic", "Logs");
        }

        /// <summary>
        /// Resolves a diagnostic log file path inside <c>%LOCALAPPDATA%\Synthetic\Logs</c>.
        /// </summary>
        /// <param name="fileName">The log file name.</param>
        /// <param name="localAppDataOverride">Optional local AppData root directory override for parallel test isolation.</param>
        /// <returns>The full diagnostic log file path.</returns>
        public static string ResolveLogPath(string fileName, string? localAppDataOverride = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Log file name must not be empty.", nameof(fileName));
            }

            return Path.Combine(ResolveLogsDir(localAppDataOverride), fileName);
        }

        /// <summary>
        /// Resolves the MergeDuplicates Step 6 execution results log path
        /// (<c>%LOCALAPPDATA%\Synthetic\Logs\MergeDuplicates_Step6_ExecutionResult.json</c>).
        /// </summary>
        /// <param name="localAppDataOverride">Optional local AppData root directory override for parallel test isolation.</param>
        /// <returns>The full Step 6 results log file path.</returns>
        public static string GetStep6ResultsLogPath(string? localAppDataOverride = null)
        {
            return ResolveLogPath(Step6ResultsLogFileName, localAppDataOverride);
        }
    }
}
