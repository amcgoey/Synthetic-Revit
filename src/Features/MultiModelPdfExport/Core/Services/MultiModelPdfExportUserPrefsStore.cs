using System;
using System.IO;
using Newtonsoft.Json;
using Synthetic.Features.MultiModelPdfExport.Models;

namespace Synthetic.Features.MultiModelPdfExport.Services
{
    /// <summary>
    /// Reads and writes machine-local <see cref="MultiModelPdfExportUserPrefs"/> JSON without
    /// mutating any Revit document or Undo history.
    /// </summary>
    public sealed class MultiModelPdfExportUserPrefsStore
    {
        /// <summary>
        /// Gets the default preferences file path under <c>%APPDATA%\Synthetic\MultiModelPdfExportUserPrefs.json</c>.
        /// </summary>
        public static string DefaultFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Synthetic",
            "MultiModelPdfExportUserPrefs.json");

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiModelPdfExportUserPrefsStore"/> class.
        /// </summary>
        /// <param name="filePath">Optional custom JSON path for isolated testing; defaults to <see cref="DefaultFilePath"/>.</param>
        public MultiModelPdfExportUserPrefsStore(string? filePath = null)
        {
            FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath!;
        }

        /// <summary>
        /// Gets the backing JSON file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Loads preferences from disk, returning defaults if the file is missing, blank, or corrupt.
        /// </summary>
        public MultiModelPdfExportUserPrefs Load()
        {
            if (!File.Exists(FilePath))
            {
                return new MultiModelPdfExportUserPrefs();
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new MultiModelPdfExportUserPrefs();
                }

                MultiModelPdfExportUserPrefs? prefs = JsonConvert.DeserializeObject<MultiModelPdfExportUserPrefs>(json);
                if (prefs == null)
                {
                    return new MultiModelPdfExportUserPrefs();
                }

                prefs.LastOutputFolder = prefs.LastOutputFolder ?? string.Empty;
                prefs.PdfSetupName = prefs.PdfSetupName?.Trim() ?? string.Empty;
                prefs.LastBulkPrintSetName = prefs.LastBulkPrintSetName?.Trim() ?? string.Empty;
                if (!Enum.IsDefined(typeof(MultiModelPdfFileMode), prefs.FileMode))
                {
                    prefs.FileMode = MultiModelPdfFileMode.CombinedPerDocument;
                }

                return prefs;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is System.Security.SecurityException ||
                ex is JsonException)
            {
                // Corrupt or unreadable local preferences must fall back cleanly to defaults.
                System.Diagnostics.Debug.WriteLine(
                    $"[MultiModelPdfExport] Failed to load user preferences from '{FilePath}': {ex.Message}");
                return new MultiModelPdfExportUserPrefs();
            }
        }

        /// <summary>
        /// Saves preferences to disk as indented JSON, creating the parent directory if needed.
        /// </summary>
        public void Save(MultiModelPdfExportUserPrefs prefs)
        {
            if (prefs == null)
            {
                throw new ArgumentNullException(nameof(prefs));
            }

            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonConvert.SerializeObject(prefs, Formatting.Indented);
            File.WriteAllText(FilePath, json);
        }

        /// <summary>
        /// Attempts to save preferences to disk, returning <c>false</c> when I/O, path,
        /// permission, or serialization failures occur.
        /// </summary>
        public bool TrySave(MultiModelPdfExportUserPrefs prefs)
        {
            if (prefs == null)
            {
                throw new ArgumentNullException(nameof(prefs));
            }

            try
            {
                Save(prefs);
                return true;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is System.Security.SecurityException ||
                ex is JsonException)
            {
                // Local preferences persistence failure must never block export execution.
                System.Diagnostics.Debug.WriteLine(
                    $"[MultiModelPdfExport] Failed to save user preferences to '{FilePath}': {ex.Message}");
                return false;
            }
        }
    }
}
