using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Synthetic.Features.MultiModelPdfExport.Services
{
    /// <summary>
    /// Pure Windows path sanitization, folder validation, and safe path-length utilities
    /// for Multi-Model PDF Export.
    /// </summary>
    public static class MultiModelPdfPathHelper
    {
        /// <summary>
        /// Maximum permitted output path length (strictly less than 255 characters).
        /// </summary>
        public const int SafeMaxPathLength = 255;

        /// <summary>
        /// Default filename stem used when a detached or unsaved model resolves to an empty title.
        /// </summary>
        public const string FallbackDocumentToken = "Untitled";

        private static readonly HashSet<char> InvalidSegmentChars = BuildInvalidSegmentCharSet();

        private static readonly HashSet<string> DosReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Sanitizes a Revit document title into a safe base filename token (without <c>.pdf</c> extension).
        /// </summary>
        public static string SanitizeDocumentTitleToken(string? documentTitle)
        {
            string trimmed = (documentTitle ?? string.Empty).Trim();
            if (trimmed.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }

            return SanitizeSegment(trimmed, FallbackDocumentToken);
        }

        /// <summary>
        /// Sanitizes a single path segment by replacing invalid Windows characters, stripping leading
        /// whitespace and trailing dots/spaces, neutralizing DOS reserved names, and falling back when empty.
        /// </summary>
        public static string SanitizeSegment(string? input, string fallback = FallbackDocumentToken)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return fallback;
            }

            var sb = new StringBuilder(input!.Length);
            foreach (char ch in input)
            {
                if (ch < 32 || InvalidSegmentChars.Contains(ch))
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            string cleaned = sb.ToString().TrimStart().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return fallback;
            }

            int dotIndex = cleaned.IndexOf('.');
            string stem = dotIndex >= 0 ? cleaned.Substring(0, dotIndex) : cleaned;
            if (DosReservedNames.Contains(stem))
            {
                cleaned = "_" + cleaned;
            }

            return cleaned;
        }

        /// <summary>
        /// Normalizes an Output Folder path by trimming whitespace and redundant trailing directory separators
        /// while preserving drive roots such as <c>C:\</c>.
        /// </summary>
        public static string NormalizeFolderPath(string? folderPath)
        {
            string trimmed = (folderPath ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.Length == 3 &&
                char.IsLetter(trimmed[0]) &&
                trimmed[1] == ':' &&
                (trimmed[2] == '\\' || trimmed[2] == '/'))
            {
                return trimmed[0] + ":\\";
            }

            return trimmed.TrimEnd('\\', '/');
        }

        /// <summary>
        /// Validates that <paramref name="folderPath"/> is a rooted drive or UNC folder path free of
        /// invalid Windows path characters, DOS reserved segment names, and trailing dots/spaces.
        /// </summary>
        public static bool TryValidateOutputFolder(string? folderPath, out string errorMessage)
        {
            string trimmed = (folderPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                errorMessage = "Output folder path is required.";
                return false;
            }

            string remainder;
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string uncBody = trimmed.Substring(2);
                int firstSep = uncBody.IndexOfAny(new[] { '\\', '/' });
                if (firstSep <= 0)
                {
                    errorMessage = "UNC output folder must include both server and share names (\\\\server\\share).";
                    return false;
                }

                string server = uncBody.Substring(0, firstSep);
                string afterServer = uncBody.Substring(firstSep + 1);
                int secondSep = afterServer.IndexOfAny(new[] { '\\', '/' });
                string share = secondSep < 0 ? afterServer : afterServer.Substring(0, secondSep);
                remainder = secondSep < 0 ? string.Empty : afterServer.Substring(secondSep + 1);

                if (!IsValidFolderSegment(server, checkReservedName: false, out errorMessage) ||
                    !IsValidFolderSegment(share, checkReservedName: true, out errorMessage))
                {
                    return false;
                }
            }
            else if (trimmed.Length >= 3 &&
                     char.IsLetter(trimmed[0]) &&
                     trimmed[1] == ':' &&
                     (trimmed[2] == '\\' || trimmed[2] == '/'))
            {
                remainder = trimmed.Substring(3);
            }
            else
            {
                errorMessage = "Output folder must be a rooted drive (e.g. C:\\Exports) or UNC path (\\\\server\\share).";
                return false;
            }

            if (!string.IsNullOrEmpty(remainder))
            {
                string[] segments = remainder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    if (!IsValidFolderSegment(segment, checkReservedName: true, out errorMessage))
                    {
                        return false;
                    }
                }
            }

            string normalized = NormalizeFolderPath(trimmed);
            if (normalized.Length >= SafeMaxPathLength)
            {
                errorMessage = $"Output folder path must stay under {SafeMaxPathLength} characters.";
                return false;
            }

            try
            {
                _ = Path.GetFullPath(normalized);
            }
            catch (Exception ex)
            {
                errorMessage = $"Output folder path is invalid: {ex.Message}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="folderPath"/> passes <see cref="TryValidateOutputFolder"/>.
        /// </summary>
        public static bool IsValidOutputFolder(string? folderPath) =>
            TryValidateOutputFolder(folderPath, out _);

        /// <summary>
        /// Builds the resolved Combined PDF destination path <c>&lt;OutputFolder&gt;\&lt;Token&gt;.pdf</c>.
        /// </summary>
        public static string BuildCombinedPdfPath(string outputFolder, string outputFileToken)
        {
            string normalizedFolder = NormalizeFolderPath(outputFolder);
            string safeToken = SanitizeSegment(outputFileToken, FallbackDocumentToken);
            return Path.Combine(normalizedFolder, safeToken + ".pdf");
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="fullPath"/> is non-blank and strictly shorter than 255 characters.
        /// </summary>
        public static bool IsPathWithinSafeLimit(string? fullPath) =>
            !string.IsNullOrWhiteSpace(fullPath) && fullPath!.Length < SafeMaxPathLength;

        /// <summary>
        /// Throws <see cref="PathTooLongException"/> if <paramref name="fullPath"/> reaches or exceeds 255 characters.
        /// </summary>
        public static void ValidateOutputPathLength(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentException("Output file path cannot be blank.", nameof(fullPath));
            }

            if (fullPath!.Length >= SafeMaxPathLength)
            {
                throw new PathTooLongException(
                    $"Output path length ({fullPath.Length} characters) must be strictly less than {SafeMaxPathLength} characters: {fullPath}");
            }
        }

        private static bool IsValidFolderSegment(string segment, bool checkReservedName, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
            {
                errorMessage = "Output folder contains an empty or relative path segment.";
                return false;
            }

            foreach (char ch in segment)
            {
                if (ch < 32 || InvalidSegmentChars.Contains(ch))
                {
                    errorMessage = "Output folder contains invalid path characters.";
                    return false;
                }
            }

            if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
            {
                errorMessage = "Output folder segment cannot end with a space or period.";
                return false;
            }

            if (checkReservedName)
            {
                int dotIndex = segment.IndexOf('.');
                string stem = dotIndex >= 0 ? segment.Substring(0, dotIndex) : segment;
                if (DosReservedNames.Contains(stem))
                {
                    errorMessage = $"Output folder cannot contain reserved Windows name '{stem}'.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        private static HashSet<char> BuildInvalidSegmentCharSet()
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
