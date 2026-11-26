using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Synthetic.Platform.Settings;

namespace Synthetic.Features.DetailItemFactory.Settings
{
    /// <summary>
    /// Configuration settings for the Detail Item Factory module.
    /// This module is serialized and saved inside the Revit Document's Extensible Storage schema.
    /// </summary>
    public class DetailItemFactorySettings : ISettingModule
    {
        /// <summary>
        /// The default target subcategory representing the root Detail Items category.
        /// </summary>
        [JsonIgnore]
        public const string DefaultTargetSubcategory = "Detail Items";

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "DetailItemFactorySettings";

        private static readonly HashSet<char> InvalidSegmentChars = BuildInvalidSegmentCharSet();

        private static readonly HashSet<string> DosReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the default target subcategory name.
        /// </summary>
        public string TargetSubcategory { get; set; } = DefaultTargetSubcategory;

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettings"/> class.
        /// </summary>
        public DetailItemFactorySettings()
        {
            OutputPath = string.Empty;
            TargetSubcategory = DefaultTargetSubcategory;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            OutputPath ??= string.Empty;
            if (string.IsNullOrWhiteSpace(TargetSubcategory))
            {
                TargetSubcategory = DefaultTargetSubcategory;
            }
        }

        /// <summary>
        /// App default settings.
        /// </summary>
        /// <returns>DetailItemFactorySettings object with default configurations.</returns>
        public DetailItemFactorySettings Defaults()
        {
            OutputPath = string.Empty;
            TargetSubcategory = DefaultTargetSubcategory;
            return this;
        }

        /// <summary>
        /// Returns an isolated deep copy of this settings object.
        /// </summary>
        public DetailItemFactorySettings DeepClone()
        {
            return new DetailItemFactorySettings
            {
                OutputPath = OutputPath ?? string.Empty,
                TargetSubcategory = TargetSubcategory ?? string.Empty
            };
        }

        /// <summary>
        /// Validates settings module using pure path-syntax checks without touching the filesystem.
        /// </summary>
        public bool IsValid()
        {
            if (OutputPath == null || OutputPath.Length == 0)
            {
                return true;
            }

            return IsValidOutputPathSyntax(OutputPath);
        }

        /// <summary>
        /// Validates that <paramref name="path"/> is syntactically well-formed as a rooted drive or UNC folder path.
        /// </summary>
        public static bool IsValidOutputPathSyntax(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string candidate = path!.Trim();
            string remainder;
            if (candidate.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string uncBody = candidate.Substring(2);
                int firstSep = uncBody.IndexOfAny(new[] { '\\', '/' });
                if (firstSep <= 0)
                {
                    return false;
                }

                string server = uncBody.Substring(0, firstSep);
                string afterServer = uncBody.Substring(firstSep + 1);
                int secondSep = afterServer.IndexOfAny(new[] { '\\', '/' });
                string share = secondSep < 0 ? afterServer : afterServer.Substring(0, secondSep);
                remainder = secondSep < 0 ? string.Empty : afterServer.Substring(secondSep + 1);

                if (!IsValidPathSegment(server, checkReservedName: false) ||
                    !IsValidPathSegment(share, checkReservedName: true))
                {
                    return false;
                }
            }
            else if (candidate.Length >= 3 &&
                     char.IsLetter(candidate[0]) &&
                     candidate[1] == ':' &&
                     (candidate[2] == '\\' || candidate[2] == '/'))
            {
                remainder = candidate.Substring(3);
            }
            else
            {
                return false;
            }

            if (!string.IsNullOrEmpty(remainder))
            {
                string[] segments = remainder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    if (!IsValidPathSegment(segment, checkReservedName: true))
                    {
                        return false;
                    }
                }
            }

            try
            {
                if (!Path.IsPathRooted(candidate))
                {
                    return false;
                }

                _ = Path.GetFullPath(candidate);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private static bool IsValidPathSegment(string segment, bool checkReservedName)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
            {
                return false;
            }

            foreach (char ch in segment)
            {
                if (ch < 32 || InvalidSegmentChars.Contains(ch))
                {
                    return false;
                }
            }

            if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }

            if (checkReservedName)
            {
                int dotIndex = segment.IndexOf('.');
                string stem = dotIndex >= 0 ? segment.Substring(0, dotIndex) : segment;
                if (DosReservedNames.Contains(stem))
                {
                    return false;
                }
            }

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
