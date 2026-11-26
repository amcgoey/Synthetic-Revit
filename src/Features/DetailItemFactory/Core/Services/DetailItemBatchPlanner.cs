using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Synthetic.Features.DetailItemFactory.Models;

namespace Synthetic.Features.DetailItemFactory.Services
{
    /// <summary>
    /// Pure Core planner for Detail Item Factory filename sanitization, path assembly,
    /// orientation default resolution, overwrite classification, batch target deduplication,
    /// and temporary DWG/PCP export stem generation and cleanup.
    /// </summary>
    public static class DetailItemBatchPlanner
    {
        public const string TempExportFilePrefix = "temp_export_";
        public const int TempRunTokenLength = 8;

        private static readonly HashSet<char> InvalidFileNameChars = BuildInvalidFileNameCharSet();

        private static readonly HashSet<string> WindowsReservedDeviceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Sanitizes a family, type, or orientation segment into a safe Windows filename component.
        /// Replaces invalid characters, strips trailing dots/spaces, and neutralizes reserved DOS device names.
        /// </summary>
        public static string SanitizeFileNameSegment(string? rawName, string fallback = "Unknown")
        {
            string safeFallback = string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback.Trim();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return safeFallback;
            }

            char[] buffer = rawName!.Trim().ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                char ch = buffer[i];
                if (ch < 32 || InvalidFileNameChars.Contains(ch))
                {
                    buffer[i] = '_';
                }
            }

            string sanitized = new string(buffer).TrimEnd('.', ' ').TrimStart(' ');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return safeFallback;
            }

            int dotIndex = sanitized.IndexOf('.');
            string stem = dotIndex >= 0 ? sanitized.Substring(0, dotIndex) : sanitized;
            if (WindowsReservedDeviceNames.Contains(stem))
            {
                sanitized = "_" + sanitized;
            }

            return sanitized;
        }

        /// <summary>
        /// Builds the standardized Detail Item family filename (<c>2D - {Family} - {Type} - {Orientation}.rfa</c>).
        /// </summary>
        public static string BuildTargetFileName(string cleanFamilyName, string cleanTypeName, string orientation)
        {
            string family = SanitizeFileNameSegment(cleanFamilyName, "UnknownFamily");
            string type = SanitizeFileNameSegment(cleanTypeName, "UnknownType");
            string resolvedOrientation = SanitizeFileNameSegment(orientation, DetailItemOrientations.Plan);
            return $"2D - {family} - {type} - {resolvedOrientation}.rfa";
        }

        /// <summary>
        /// Resolves the smart default orientation for an element in the active 2D view.
        /// </summary>
        public static string ResolveDefaultOrientation(
            DetailItemSourceViewKind activeViewKind,
            bool isLegendComponent,
            string? legendComponentViewValue)
        {
            if (activeViewKind == DetailItemSourceViewKind.Legend && isLegendComponent)
            {
                return ResolveLegendComponentOrientation(legendComponentViewValue);
            }

            if (activeViewKind == DetailItemSourceViewKind.ElevationOrSection)
            {
                return DetailItemOrientations.ElevFront;
            }

            return DetailItemOrientations.Plan;
        }

        /// <summary>
        /// Generates a short fixed-width alphanumeric run token for isolating temporary CAD exports across batch runs.
        /// </summary>
        public static string CreateTempRunToken()
        {
            return Guid.NewGuid().ToString("N").Substring(0, TempRunTokenLength);
        }

        /// <summary>
        /// Generates a short fixed-width temporary export prefix (<c>temp_export_&lt;runToken&gt;_&lt;index:D4&gt;_</c>)
        /// so per-element <c>.dwg</c> and <c>.pcp</c> export stems stay concise and collision-free.
        /// </summary>
        public static string BuildTempExportPrefix(string runToken, int sequenceIndex)
        {
            if (string.IsNullOrWhiteSpace(runToken))
            {
                throw new ArgumentException("Run token cannot be blank.", nameof(runToken));
            }

            if (sequenceIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sequenceIndex), "Sequence index cannot be negative.");
            }

            string cleanToken = SanitizeFileNameSegment(runToken, "run");
            return string.Concat(
                TempExportFilePrefix,
                cleanToken,
                "_",
                sequenceIndex.ToString("D4", CultureInfo.InvariantCulture),
                "_");
        }

        /// <summary>
        /// Deletes temporary <c>.dwg</c> and <c>.pcp</c> export files in <paramref name="outputFolder"/>
        /// matching <paramref name="fileNamePrefix"/> (or all <c>temp_export_*</c> exports when <paramref name="fileNamePrefix"/> is <c>null</c>).
        /// Per-file IO errors are captured in the returned <see cref="DetailItemTempCleanupSummary"/> without throwing.
        /// </summary>
        public static DetailItemTempCleanupSummary CleanupTempExportFiles(
            string outputFolder,
            string? fileNamePrefix = null)
        {
            string resolvedPrefix;
            if (fileNamePrefix == null)
            {
                resolvedPrefix = TempExportFilePrefix;
            }
            else if (string.IsNullOrWhiteSpace(fileNamePrefix) ||
                     !fileNamePrefix.StartsWith(TempExportFilePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Temporary export prefix must start with '{TempExportFilePrefix}'.",
                    nameof(fileNamePrefix));
            }
            else
            {
                resolvedPrefix = fileNamePrefix;
            }

            if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
            {
                return new DetailItemTempCleanupSummary(
                    outputFolder: outputFolder ?? string.Empty,
                    fileNamePrefix: resolvedPrefix,
                    directoryExists: false);
            }

            var deletedFiles = new List<string>();
            var errors = new List<string>();
            int failedCount = 0;

            try
            {
                var candidateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(outputFolder, resolvedPrefix + "*.dwg", SearchOption.TopDirectoryOnly))
                {
                    candidateFiles.Add(file);
                }
                foreach (string file in Directory.GetFiles(outputFolder, resolvedPrefix + "*.pcp", SearchOption.TopDirectoryOnly))
                {
                    candidateFiles.Add(file);
                }

                foreach (string file in candidateFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileName.StartsWith(resolvedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(file);
                        deletedFiles.Add(file);
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        errors.Add($"Failed to delete '{file}': {ex.Message}");
                    }
                }

                return new DetailItemTempCleanupSummary(
                    outputFolder: outputFolder,
                    fileNamePrefix: resolvedPrefix,
                    directoryExists: true,
                    deletedFiles: deletedFiles,
                    failedCount: failedCount,
                    errors: errors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                return new DetailItemTempCleanupSummary(
                    outputFolder: outputFolder,
                    fileNamePrefix: resolvedPrefix,
                    directoryExists: true,
                    deletedFiles: deletedFiles,
                    failedCount: failedCount,
                    errors: errors,
                    enumerationError: ex.Message);
            }
        }

        /// <summary>
        /// Builds an immutable <see cref="DetailItemBatchPlan"/> from the user's finalized UI selections,
        /// performing filename sanitization, overwrite-skip checks, and case-insensitive target path deduplication.
        /// </summary>
        public static DetailItemBatchPlan BuildPlan(
            string activeViewUniqueId,
            string outputFolder,
            string targetSubcategory,
            bool overwriteExisting,
            IEnumerable<DetailItemElementSelection> selectedElements,
            Func<string, bool>? fileExistsCheck = null,
            bool cleanupTempFiles = true)
        {
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new ArgumentException("Output folder cannot be blank.", nameof(outputFolder));
            }

            if (selectedElements == null)
            {
                throw new ArgumentNullException(nameof(selectedElements));
            }

            string normalizedOutputFolder = outputFolder.Trim();
            Func<string, bool> existsPredicate = fileExistsCheck ?? File.Exists;
            var plannedItems = new List<DetailItemPlannedElement>();
            var scheduledTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int index = 0;
            foreach (DetailItemElementSelection selection in selectedElements)
            {
                if (selection == null)
                {
                    continue;
                }

                string cleanFamily = SanitizeFileNameSegment(selection.FamilyName, "UnknownFamily");
                string cleanType = SanitizeFileNameSegment(selection.TypeName, "UnknownType");
                string cleanOrientation = SanitizeFileNameSegment(selection.SelectedOrientation, DetailItemOrientations.Plan);
                string fileName = BuildTargetFileName(cleanFamily, cleanType, cleanOrientation);
                string targetPath = Path.Combine(normalizedOutputFolder, fileName);
                string dedupKey = NormalizeTargetComparisonKey(targetPath);

                DetailItemPlanDisposition disposition;
                string? skipReason = null;

                if (!scheduledTargets.Add(dedupKey))
                {
                    disposition = DetailItemPlanDisposition.SkipDuplicate;
                    skipReason = $"Duplicate target family '{fileName}' skipped (already included in batch).";
                }
                else if (!overwriteExisting && existsPredicate(targetPath))
                {
                    disposition = DetailItemPlanDisposition.SkipExisting;
                    skipReason = "Family file already exists and overwrite flag is disabled.";
                }
                else
                {
                    disposition = DetailItemPlanDisposition.Execute;
                }

                plannedItems.Add(new DetailItemPlannedElement(
                    sequenceIndex: index,
                    elementUniqueId: selection.ElementUniqueId,
                    elementId: selection.ElementId,
                    displayName: string.IsNullOrWhiteSpace(selection.DisplayName)
                        ? $"{cleanFamily} - {cleanType}"
                        : selection.DisplayName,
                    cleanFamilyName: cleanFamily,
                    cleanTypeName: cleanType,
                    orientation: cleanOrientation,
                    targetFileName: fileName,
                    targetPath: targetPath,
                    disposition: disposition,
                    skipReason: skipReason));

                index++;
            }

            return new DetailItemBatchPlan(
                activeViewUniqueId: activeViewUniqueId,
                outputFolder: normalizedOutputFolder,
                targetSubcategory: targetSubcategory,
                overwriteExisting: overwriteExisting,
                items: plannedItems,
                cleanupTempFiles: cleanupTempFiles);
        }

        private static string ResolveLegendComponentOrientation(string? legendComponentViewValue)
        {
            if (string.IsNullOrWhiteSpace(legendComponentViewValue))
            {
                return DetailItemOrientations.Plan;
            }

            string val = legendComponentViewValue!;
            if (val.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.Plan;
            if (val.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevFront;
            if (val.IndexOf("back", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevBack;
            if (val.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                val.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 ||
                val.IndexOf("side", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevSide;
            if (val.IndexOf("top", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevTop;
            if (val.IndexOf("bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevBottom;
            if (val.IndexOf("section", StringComparison.OrdinalIgnoreCase) >= 0)
                return DetailItemOrientations.ElevSection;

            return DetailItemOrientations.Plan;
        }

        private static string NormalizeTargetComparisonKey(string targetPath)
        {
            try
            {
                if (Path.IsPathRooted(targetPath))
                {
                    return Path.GetFullPath(targetPath);
                }
            }
            catch (Exception)
            {
                // Fall back to raw path comparison when non-filesystem test paths are passed.
            }

            return targetPath;
        }

        private static HashSet<char> BuildInvalidFileNameCharSet()
        {
            var set = new HashSet<char>(Path.GetInvalidFileNameChars());
            set.Add('/');
            set.Add('\\');
            set.Add(':');
            set.Add('*');
            set.Add('?');
            set.Add('"');
            set.Add('<');
            set.Add('>');
            set.Add('|');
            return set;
        }
    }
}
