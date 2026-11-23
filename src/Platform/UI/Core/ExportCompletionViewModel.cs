using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Synthetic.Platform.UI
{
    /// <summary>
    /// Presentation ViewModel for the shared <c>ExportCompletionWindow</c> supporting both
    /// single-file exports and multi-output batch exports with optional warning notices.
    /// </summary>
    public class ExportCompletionViewModel : ViewModelBase
    {
        private readonly Action<string> _openFileAction;
        private readonly Action<string> _openFolderAction;
        private readonly string _folderActionTarget;
        private string? _errorMessage;

        private ExportCompletionViewModel(
            string windowTitle,
            string headerTitle,
            string summaryText,
            string destinationPath,
            string primaryFilePath,
            string folderActionTarget,
            string primaryActionLabel,
            string openFolderLabel,
            bool canOpenPrimaryFile,
            bool canOpenFolder,
            bool hasGeneratedOutput,
            IReadOnlyList<string> warningNotices,
            Action<string> openFileAction,
            Action<string> openFolderAction)
        {
            WindowTitle = windowTitle;
            HeaderTitle = headerTitle;
            SummaryText = summaryText;
            DestinationPath = destinationPath;
            PrimaryFilePath = primaryFilePath;
            _folderActionTarget = folderActionTarget;
            PrimaryActionLabel = primaryActionLabel;
            OpenFolderLabel = openFolderLabel;
            CanOpenPrimaryFile = canOpenPrimaryFile;
            CanOpenFolder = canOpenFolder;
            HasGeneratedOutput = hasGeneratedOutput;
            WarningNotices = warningNotices;
            _openFileAction = openFileAction;
            _openFolderAction = openFolderAction;

            OpenPrimaryFileCommand = new RelayCommand(_ => ExecuteOpenPrimaryFile(), _ => CanOpenPrimaryFile);
            OpenFolderCommand = new RelayCommand(_ => ExecuteOpenFolder(), _ => CanOpenFolder);
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
        }

        /// <summary>
        /// Gets the dialog window title.
        /// </summary>
        public string WindowTitle { get; }

        /// <summary>
        /// Gets the primary header text displayed beside the status icon.
        /// </summary>
        public string HeaderTitle { get; }

        /// <summary>
        /// Gets the summary line describing the completed export output.
        /// </summary>
        public string SummaryText { get; }

        /// <summary>
        /// Gets the saved file path (single-file mode) or output folder path (batch mode).
        /// </summary>
        public string DestinationPath { get; }

        /// <summary>
        /// Gets the primary generated file path opened by <see cref="OpenPrimaryFileCommand"/>, if any.
        /// </summary>
        public string PrimaryFilePath { get; }

        /// <summary>
        /// Gets the label for the primary action button.
        /// </summary>
        public string PrimaryActionLabel { get; }

        /// <summary>
        /// Gets the label for the open folder button.
        /// </summary>
        public string OpenFolderLabel { get; }

        /// <summary>
        /// Gets a value indicating whether the primary output file can be opened.
        /// </summary>
        public bool CanOpenPrimaryFile { get; }

        /// <summary>
        /// Gets a value indicating whether the destination folder can be opened.
        /// </summary>
        public bool CanOpenFolder { get; }

        /// <summary>
        /// Gets a value indicating whether at least one export output was generated.
        /// </summary>
        public bool HasGeneratedOutput { get; }

        /// <summary>
        /// Gets optional non-blocking warning or skipped-item notices from a batch export.
        /// </summary>
        public IReadOnlyList<string> WarningNotices { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="WarningNotices"/> contains any entries.
        /// </summary>
        public bool HasWarnings => WarningNotices.Count > 0;

        /// <summary>
        /// Gets any shell-launch error message encountered when opening a file or folder.
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether <see cref="ErrorMessage"/> is populated.
        /// </summary>
        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        /// <summary>
        /// Gets the command that opens the primary generated file.
        /// </summary>
        public ICommand OpenPrimaryFileCommand { get; }

        /// <summary>
        /// Gets the command that opens the destination folder in Windows File Explorer.
        /// </summary>
        public ICommand OpenFolderCommand { get; }

        /// <summary>
        /// Gets the command that closes the completion dialog.
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Raised when the user triggers <see cref="CloseCommand"/>.
        /// </summary>
        public event Action? RequestClose;

        /// <summary>
        /// Creates a completion ViewModel for a single exported file.
        /// </summary>
        public static ExportCompletionViewModel ForSingleFile(
            string filePath,
            string? windowTitle = null,
            string? headerTitle = null,
            Action<string>? openFileAction = null,
            Action<string>? openFolderAction = null)
        {
            string normalizedPath = filePath ?? string.Empty;
            bool hasPath = !string.IsNullOrWhiteSpace(normalizedPath);

            return new ExportCompletionViewModel(
                windowTitle: string.IsNullOrWhiteSpace(windowTitle) ? "Export Complete" : windowTitle!,
                headerTitle: string.IsNullOrWhiteSpace(headerTitle) ? "Export Complete" : headerTitle!,
                summaryText: "File saved successfully to:",
                destinationPath: normalizedPath,
                primaryFilePath: normalizedPath,
                folderActionTarget: normalizedPath,
                primaryActionLabel: "Open File",
                openFolderLabel: "Open Folder",
                canOpenPrimaryFile: hasPath,
                canOpenFolder: hasPath,
                hasGeneratedOutput: true,
                warningNotices: Array.Empty<string>(),
                openFileAction: openFileAction ?? DefaultOpenFile,
                openFolderAction: openFolderAction ?? DefaultOpenFolderForFile);
        }

        /// <summary>
        /// Creates a completion ViewModel for a multi-output batch export with optional warning notices.
        /// </summary>
        public static ExportCompletionViewModel ForBatch(
            string outputFolder,
            int generatedCount,
            string? primaryFilePath = null,
            string? outputNounPlural = "composites",
            string? primaryActionLabel = "Open Composite",
            IEnumerable<string>? warningNotices = null,
            string? windowTitle = null,
            string? headerTitle = null,
            Action<string>? openFileAction = null,
            Action<string>? openFolderAction = null)
        {
            int safeCount = Math.Max(0, generatedCount);
            string normalizedFolder = outputFolder ?? string.Empty;
            string normalizedPrimary = primaryFilePath ?? string.Empty;
            string nounPlural = string.IsNullOrWhiteSpace(outputNounPlural) ? "files" : outputNounPlural!.Trim();

            IReadOnlyList<string> warnings = (warningNotices ?? Array.Empty<string>())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim())
                .ToList()
                .AsReadOnly();

            bool hasFolder = !string.IsNullOrWhiteSpace(normalizedFolder);
            bool hasGeneratedOutput = safeCount > 0;
            bool canOpenPrimary = hasGeneratedOutput && !string.IsNullOrWhiteSpace(normalizedPrimary);

            string resolvedHeader = !string.IsNullOrWhiteSpace(headerTitle)
                ? headerTitle!
                : (hasGeneratedOutput ? "Export Complete" : "Export Completed with Warnings");

            return new ExportCompletionViewModel(
                windowTitle: string.IsNullOrWhiteSpace(windowTitle) ? "Export Complete" : windowTitle!,
                headerTitle: resolvedHeader,
                summaryText: BuildBatchSummary(safeCount, nounPlural, hasFolder),
                destinationPath: normalizedFolder,
                primaryFilePath: normalizedPrimary,
                folderActionTarget: normalizedFolder,
                primaryActionLabel: string.IsNullOrWhiteSpace(primaryActionLabel) ? "Open Composite" : primaryActionLabel!,
                openFolderLabel: "Open Folder",
                canOpenPrimaryFile: canOpenPrimary,
                canOpenFolder: hasFolder,
                hasGeneratedOutput: hasGeneratedOutput,
                warningNotices: warnings,
                openFileAction: openFileAction ?? DefaultOpenFile,
                openFolderAction: openFolderAction ?? DefaultOpenFolderDirectory);
        }

        /// <summary>
        /// Formats the Windows Explorer command-line argument to select a specific file.
        /// </summary>
        public static string BuildExplorerSelectArgument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            return $"/select,\"{filePath}\"";
        }

        private void ExecuteOpenPrimaryFile()
        {
            if (!CanOpenPrimaryFile)
            {
                return;
            }

            ErrorMessage = null;
            try
            {
                _openFileAction(PrimaryFilePath);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not open file: {ex.Message}";
            }
        }

        private void ExecuteOpenFolder()
        {
            if (!CanOpenFolder)
            {
                return;
            }

            ErrorMessage = null;
            try
            {
                _openFolderAction(_folderActionTarget);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not open folder: {ex.Message}";
            }
        }

        private static string BuildBatchSummary(int count, string nounPlural, bool hasFolder)
        {
            string suffix = hasFolder ? " in:" : ".";
            if (count == 0)
            {
                return $"No {nounPlural} were generated{suffix}";
            }

            if (count == 1)
            {
                string singular = ToSingularNoun(nounPlural);
                return $"Generated 1 {singular}{suffix}";
            }

            return $"Generated {count} {nounPlural}{suffix}";
        }

        private static string ToSingularNoun(string nounPlural)
        {
            if (nounPlural.Length > 1 && nounPlural.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                return nounPlural.Substring(0, nounPlural.Length - 1);
            }

            return nounPlural;
        }

        private static void DefaultOpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }

        private static void DefaultOpenFolderForFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = BuildExplorerSelectArgument(filePath),
                    UseShellExecute = true
                });
                return;
            }

            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
            }
        }

        private static void DefaultOpenFolderDirectory(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
    }
}
