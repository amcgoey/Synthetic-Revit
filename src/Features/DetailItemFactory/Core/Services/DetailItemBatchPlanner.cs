using System;
using System.Collections.Generic;
using System.IO;
using Synthetic.Features.DetailItemFactory.Models;

namespace Synthetic.Features.DetailItemFactory.Services
{
    /// <summary>
    /// Pure Core planner for Detail Item Factory filename sanitization, path assembly,
    /// orientation default resolution, overwrite classification, and batch target deduplication.
    /// </summary>
    public static class DetailItemBatchPlanner
    {
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
        /// Builds an immutable <see cref="DetailItemBatchPlan"/> from the user's finalized UI selections,
        /// performing filename sanitization, overwrite-skip checks, and case-insensitive target path deduplication.
        /// </summary>
        public static DetailItemBatchPlan BuildPlan(
            string activeViewUniqueId,
            string outputFolder,
            string targetSubcategory,
            bool overwriteExisting,
            IEnumerable<DetailItemElementSelection> selectedElements,
            Func<string, bool>? fileExistsCheck = null)
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
                items: plannedItems);
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
