using System;
using System.Text.RegularExpressions;

namespace Synthetic.RevitDOM.Utilities
{
    /// <summary>
    /// Utility methods for naming and name manipulation.
    /// </summary>
    public static class NamingUtils
    {
        /// <summary>
        /// Retrieves the base name from a name string by stripping optional separators and trailing numbers.
        /// </summary>
        /// <param name="name">The name to process.</param>
        /// <returns>The base name string without trailing numbers or separators.</returns>
        public static string GetBaseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            // Match base name and strip optional separator (space, underscore, hyphen, dot, hash) followed by trailing numbers
            var match = Regex.Match(name, @"^(.*?)(?:[\s_#\-\.]+)?\d+$");
            return match.Success ? match.Groups[1].Value.Trim() : name.Trim();
        }
    }
}
