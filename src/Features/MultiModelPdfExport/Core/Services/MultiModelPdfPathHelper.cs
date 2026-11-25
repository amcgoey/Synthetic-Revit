using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Synthetic.Features.MultiModelPdfExport.Models;

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
        public static string BuildCombinedPdfPath(string outputFolder, string outputFileToken) =>
            BuildSeparateDocumentFolderPath(outputFolder, outputFileToken) + ".pdf";

        /// <summary>
        /// Builds the resolved Separate-PDFs document subfolder path <c>&lt;OutputFolder&gt;\&lt;Token&gt;</c>.
        /// </summary>
        public static string BuildSeparateDocumentFolderPath(string outputFolder, string outputFileToken)
        {
            string normalizedFolder = NormalizeFolderPath(outputFolder);
            string safeToken = SanitizeSegment(outputFileToken, FallbackDocumentToken);
            return Path.Combine(normalizedFolder, safeToken);
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

        /// <summary>
        /// Deterministically resolves collision-free output filename tokens across participating projects under
        /// <c>Active &gt; OpenStandalone &gt; Linked</c> priority, reserving all pre-existing and newly assigned
        /// tokens (<c> (Link)</c> or <c>_2</c>, <c>_3</c>, ...) so no two documents ever share a path token.
        /// </summary>
        public static IReadOnlyDictionary<string, MultiModelPdfResolvedToken> ResolveOutputTokens(
            IEnumerable<MultiModelPdfTokenCandidate>? candidates)
        {
            var resolvedByKey = new Dictionary<string, MultiModelPdfResolvedToken>(StringComparer.Ordinal);
            if (candidates == null)
            {
                return new ReadOnlyDictionary<string, MultiModelPdfResolvedToken>(resolvedByKey);
            }

            var indexed = new List<IndexedTokenCandidate>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (MultiModelPdfTokenCandidate candidate in candidates)
            {
                if (candidate == null || !seenKeys.Add(candidate.DocumentKey))
                {
                    continue;
                }

                string baseToken = SanitizeDocumentTitleToken(candidate.DocumentTitle);
                indexed.Add(new IndexedTokenCandidate(candidate, baseToken, index++));
            }

            if (indexed.Count == 0)
            {
                return new ReadOnlyDictionary<string, MultiModelPdfResolvedToken>(resolvedByKey);
            }

            List<IndexedTokenCandidate> ordered = indexed
                .OrderBy(item => GetCollisionPriority(item.Candidate.Origin))
                .ThenBy(item => item.OriginalIndex)
                .ToList();

            var reservedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var originsByBaseToken = new Dictionary<string, HashSet<MultiModelPdfDocumentOrigin>>(StringComparer.OrdinalIgnoreCase);
            var nextSuffixByBaseToken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var collisions = new List<IndexedTokenCandidate>();

            // Pass 1: Reserve each distinct base token for its highest-priority owner so pre-existing
            // "_2" or " (Link)" model titles keep their natural filename without false collisions.
            foreach (IndexedTokenCandidate item in ordered)
            {
                if (!reservedTokens.Contains(item.BaseToken))
                {
                    reservedTokens.Add(item.BaseToken);
                    originsByBaseToken[item.BaseToken] = new HashSet<MultiModelPdfDocumentOrigin> { item.Candidate.Origin };
                    resolvedByKey[item.Candidate.DocumentKey] = new MultiModelPdfResolvedToken(
                        item.Candidate.DocumentKey,
                        item.BaseToken,
                        item.BaseToken,
                        isCollisionDisambiguated: false);
                }
                else
                {
                    collisions.Add(item);
                }
            }

            // Pass 2: Assign deterministic non-colliding suffixes for remaining colliding titles.
            foreach (IndexedTokenCandidate item in collisions)
            {
                HashSet<MultiModelPdfDocumentOrigin> seenOrigins = originsByBaseToken[item.BaseToken];
                string finalToken;

                if (item.Candidate.Origin == MultiModelPdfDocumentOrigin.Linked &&
                    !seenOrigins.Contains(MultiModelPdfDocumentOrigin.Linked))
                {
                    seenOrigins.Add(MultiModelPdfDocumentOrigin.Linked);
                    string linkToken = item.BaseToken + " (Link)";
                    finalToken = !reservedTokens.Contains(linkToken)
                        ? linkToken
                        : AllocateNumberedToken(item.BaseToken, reservedTokens, nextSuffixByBaseToken);
                }
                else
                {
                    seenOrigins.Add(item.Candidate.Origin);
                    finalToken = AllocateNumberedToken(item.BaseToken, reservedTokens, nextSuffixByBaseToken);
                }

                reservedTokens.Add(finalToken);
                resolvedByKey[item.Candidate.DocumentKey] = new MultiModelPdfResolvedToken(
                    item.Candidate.DocumentKey,
                    item.BaseToken,
                    finalToken,
                    isCollisionDisambiguated: true);
            }

            return new ReadOnlyDictionary<string, MultiModelPdfResolvedToken>(resolvedByKey);
        }

        private static string AllocateNumberedToken(
            string baseToken,
            HashSet<string> reservedTokens,
            Dictionary<string, int> nextSuffixByBaseToken)
        {
            int suffix = nextSuffixByBaseToken.TryGetValue(baseToken, out int existing) ? existing : 2;
            string candidate = $"{baseToken}_{suffix}";
            while (reservedTokens.Contains(candidate))
            {
                suffix++;
                candidate = $"{baseToken}_{suffix}";
            }

            nextSuffixByBaseToken[baseToken] = suffix + 1;
            return candidate;
        }

        private static int GetCollisionPriority(MultiModelPdfDocumentOrigin origin) => origin switch
        {
            MultiModelPdfDocumentOrigin.Active => 0,
            MultiModelPdfDocumentOrigin.OpenStandalone => 1,
            _ => 2
        };

        private sealed class IndexedTokenCandidate
        {
            public IndexedTokenCandidate(MultiModelPdfTokenCandidate candidate, string baseToken, int originalIndex)
            {
                Candidate = candidate;
                BaseToken = baseToken;
                OriginalIndex = originalIndex;
            }

            public MultiModelPdfTokenCandidate Candidate { get; }
            public string BaseToken { get; }
            public int OriginalIndex { get; }
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
