using System;
using System.Globalization;
using System.Reflection;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Reads and validates compile-time <c>RevitVersion</c> assembly metadata emitted onto payload assemblies.
    /// </summary>
    public static class RevitVersionMetadata
    {
        public const string MetadataKey = "RevitVersion";

        /// <summary>
        /// Validates and parses a four-digit Revit release year (e.g. <c>"2026"</c>).
        /// </summary>
        public static bool TryParseRevitYear(string? rawValue, out int year)
        {
            year = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            string trimmed = rawValue!.Trim();
            if (trimmed.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                return false;
            }

            if (parsed < 2000 || parsed > 9999)
            {
                return false;
            }

            year = parsed;
            return true;
        }

        /// <summary>
        /// Reads the compile-time <c>RevitVersion</c> assembly metadata as a four-digit integer year,
        /// throwing <see cref="InvalidOperationException"/> when missing or unparseable.
        /// </summary>
        public static int ReadRevitYear(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            AssemblyMetadataAttribute? metadata = null;
            foreach (AssemblyMetadataAttribute attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attr.Key, MetadataKey, StringComparison.Ordinal))
                {
                    metadata = attr;
                    break;
                }
            }

            string assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unknown>";
            if (metadata == null)
            {
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' is missing required [AssemblyMetadata(\"{MetadataKey}\", \"<Year>\")] attribute.");
            }

            if (!TryParseRevitYear(metadata.Value, out int year))
            {
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' has unparseable '{MetadataKey}' metadata value '{metadata.Value ?? "<null>"}'. Expected a four-digit Revit release year.");
            }

            return year;
        }

        /// <summary>
        /// Reads the compile-time <c>RevitVersion</c> assembly metadata as a normalized four-digit year string,
        /// throwing <see cref="InvalidOperationException"/> when missing or unparseable.
        /// </summary>
        public static string ReadRevitYearString(Assembly assembly)
        {
            return ReadRevitYear(assembly).ToString(CultureInfo.InvariantCulture);
        }
    }
}
