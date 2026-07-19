using System;
using System.IO;

namespace Synthetic.RevitDOM.Operations.Standards
{
    /// <summary>
    /// Pure C# utility for calculating standard JSON save paths based on document state.
    /// </summary>
    public static class PathResolutionUtility
    {
        /// <summary>
        /// Calculates the default standards save path based on primitive document properties.
        /// </summary>
        /// <param name="documentTitle">The active document's title.</param>
        /// <param name="isModelInCloud">Whether the model is cloud-based.</param>
        /// <param name="isWorkshared">Whether the model is workshared.</param>
        /// <param name="centralModelPathString">The string representing the central model path, if workshared.</param>
        /// <param name="localPathName">The string representing the local path name.</param>
        /// <param name="fallbackDirectory">An optional fallback directory (e.g. Revit local file save location) to use if no other path can be resolved.</param>
        /// <returns>A safe, logical default file path for saving standards.</returns>
        public static string GetDefaultSavePath(
            string documentTitle,
            bool isModelInCloud,
            bool isWorkshared,
            string? centralModelPathString,
            string? localPathName,
            string? fallbackDirectory = null)
        {
            string? directory = null;

            if (isModelInCloud)
            {
                directory = !string.IsNullOrEmpty(fallbackDirectory) && Directory.Exists(fallbackDirectory)
                    ? fallbackDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (isWorkshared && !string.IsNullOrEmpty(centralModelPathString))
            {
                try
                {
                    directory = Path.GetDirectoryName(centralModelPathString);
                }
                catch (ArgumentException)
                {
                    // Fallback on invalid path
                }
            }

            if (string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(localPathName))
            {
                try
                {
                    directory = Path.GetDirectoryName(localPathName);
                }
                catch (ArgumentException)
                {
                    // Fallback on invalid path
                }
            }

            if (string.IsNullOrEmpty(directory))
            {
                directory = !string.IsNullOrEmpty(fallbackDirectory) && Directory.Exists(fallbackDirectory)
                    ? fallbackDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            // Sanitize document title for use as a filename
            string cleanTitle = documentTitle;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                cleanTitle = cleanTitle.Replace(c, '_');
            }

            // Remove .rvt extension if present
            if (cleanTitle.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - 4);
            }

            return Path.Combine(directory, $"{cleanTitle} Standards.json");
        }
    }
}
