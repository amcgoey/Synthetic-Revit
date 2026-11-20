using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Synthetic.Modules.ShadowStudy.Models;

namespace Synthetic.Modules.ShadowStudy.Services
{
    /// <summary>
    /// Pure path sanitization, collision-only view labeling, and output filename helpers for Shadow Study.
    /// </summary>
    public static class ShadowStudyPathHelper
    {
        private const int SafeMaxPathLength = 255;

        private static readonly HashSet<char> InvalidChars = BuildInvalidCharSet();

        /// <summary>
        /// Replaces illegal Windows filename/path characters with underscores.
        /// </summary>
        public static string SanitizePathSegment(string? input, string fallback = "Untitled")
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return fallback;
            }

            var sb = new StringBuilder(input!.Length);
            foreach (char ch in input)
            {
                if (ch < 32 || InvalidChars.Contains(ch))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            string sanitized = sb.ToString().Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
        }

        /// <summary>
        /// Assigns <see cref="ShadowStudyViewItem.ViewLabel"/> on each view, appending <c> (&lt;ViewType&gt;)</c>
        /// only when sanitized view names collide.
        /// </summary>
        public static void AssignViewLabels(IList<ShadowStudyViewItem> views)
        {
            if (views == null || views.Count == 0)
            {
                return;
            }

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var baseNames = new string[views.Count];
            for (int i = 0; i < views.Count; i++)
            {
                string sanitized = SanitizePathSegment(views[i].Name);
                baseNames[i] = sanitized;
                nameCounts[sanitized] = nameCounts.TryGetValue(sanitized, out int existing) ? existing + 1 : 1;
            }

            var usedLabels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < views.Count; i++)
            {
                string baseName = baseNames[i];
                string candidate = nameCounts[baseName] > 1 && !string.IsNullOrWhiteSpace(views[i].ViewType)
                    ? SanitizePathSegment($"{baseName} ({views[i].ViewType})")
                    : baseName;

                if (usedLabels.TryGetValue(candidate, out int suffixIndex))
                {
                    suffixIndex++;
                    usedLabels[candidate] = suffixIndex;
                    candidate = $"{candidate}_{suffixIndex}";
                }
                else
                {
                    usedLabels[candidate] = 1;
                }

                views[i].ViewLabel = candidate;
            }
        }

        /// <summary>
        /// Formats a study date label as <c>yyMMdd</c> or <c>yyMMdd - PresetName</c>.
        /// </summary>
        public static string FormatDateLabel(DateTime date, string? presetName = null)
        {
            string yyMmDd = date.ToString("yyMMdd", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(presetName)
                ? yyMmDd
                : $"{yyMmDd} - {SanitizePathSegment(presetName)}";
        }

        /// <summary>
        /// Formats a 24-hour <c>HHmm</c> frame filename stem.
        /// </summary>
        public static string FormatTimeSlot(DateTime projectLocalTime)
        {
            return projectLocalTime.ToString("HHmm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds the Shadow Composite filename <c>&lt;ViewLabel&gt; - &lt;DateLabel&gt; - Composite.png</c>.
        /// </summary>
        public static string BuildCompositeFileName(string viewLabel, string dateLabel)
        {
            string safeView = SanitizePathSegment(viewLabel);
            string safeDate = SanitizePathSegment(dateLabel);
            return $"{safeView} - {safeDate} - Composite.png";
        }

        /// <summary>
        /// Builds the Shadow Frame subfolder name <c>&lt;ViewLabel&gt; - &lt;DateLabel&gt;</c>.
        /// </summary>
        public static string BuildFrameFolderName(string viewLabel, string dateLabel)
        {
            string safeView = SanitizePathSegment(viewLabel);
            string safeDate = SanitizePathSegment(dateLabel);
            return $"{safeView} - {safeDate}";
        }

        /// <summary>
        /// Builds an individual Shadow Frame filename <c>&lt;ViewLabel&gt; - &lt;DateLabel&gt; - &lt;HHmm&gt;.png</c>.
        /// </summary>
        public static string BuildFrameFileName(string viewLabel, string dateLabel, DateTime projectLocalTime)
        {
            string safeView = SanitizePathSegment(viewLabel);
            string safeDate = SanitizePathSegment(dateLabel);
            string hhMm = FormatTimeSlot(projectLocalTime);
            return $"{safeView} - {safeDate} - {hhMm}.png";
        }

        /// <summary>
        /// Validates that the destination path stays safely below Windows MAX_PATH limits.
        /// </summary>
        public static void ValidateOutputPathLength(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException("Output file path cannot be blank.", nameof(fullPath));
            }

            if (fullPath.Length >= SafeMaxPathLength)
            {
                throw new PathTooLongException(
                    $"Output path length ({fullPath.Length} characters) exceeds the Windows safe limit of {SafeMaxPathLength - 1} characters: {fullPath}");
            }
        }

        private static HashSet<char> BuildInvalidCharSet()
        {
            var set = new HashSet<char>(Path.GetInvalidFileNameChars());
            foreach (char ch in Path.GetInvalidPathChars())
            {
                set.Add(ch);
            }
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
