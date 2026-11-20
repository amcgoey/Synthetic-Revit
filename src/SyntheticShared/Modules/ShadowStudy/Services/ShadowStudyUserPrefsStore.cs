using System;
using System.IO;
using Newtonsoft.Json;
using Synthetic.Modules.ShadowStudy.Models;

namespace Synthetic.Modules.ShadowStudy.Services
{
    /// <summary>
    /// Reads and writes machine-local <see cref="ShadowStudyUserPrefs"/> JSON without touching the Revit document or Undo stack.
    /// </summary>
    public class ShadowStudyUserPrefsStore
    {
        private const int MinPixelWidth = 64;
        private const int MaxPixelWidth = 15000;
        private const int DefaultPixelWidth = 3000;

        /// <summary>
        /// Gets the default preferences file path under <c>%APPDATA%\Synthetic\ShadowStudyUserPrefs.json</c>.
        /// </summary>
        public static string DefaultFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Synthetic",
            "ShadowStudyUserPrefs.json");

        /// <summary>
        /// Initializes a new instance of the <see cref="ShadowStudyUserPrefsStore"/> class.
        /// </summary>
        /// <param name="filePath">Optional custom JSON path for isolated testing; defaults to <see cref="DefaultFilePath"/>.</param>
        public ShadowStudyUserPrefsStore(string? filePath = null)
        {
            FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath : filePath!;
        }

        /// <summary>
        /// Gets the backing JSON file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Loads preferences from disk, returning defaults if the file is missing or unreadable.
        /// </summary>
        public ShadowStudyUserPrefs Load()
        {
            if (!File.Exists(FilePath))
            {
                return new ShadowStudyUserPrefs();
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new ShadowStudyUserPrefs();
                }

                ShadowStudyUserPrefs? prefs = JsonConvert.DeserializeObject<ShadowStudyUserPrefs>(json);
                if (prefs == null)
                {
                    return new ShadowStudyUserPrefs();
                }

                prefs.LastOutputFolder = prefs.LastOutputFolder ?? string.Empty;
                if (prefs.PixelWidth < MinPixelWidth || prefs.PixelWidth > MaxPixelWidth)
                {
                    prefs.PixelWidth = DefaultPixelWidth;
                }

                return prefs;
            }
            catch
            {
                return new ShadowStudyUserPrefs();
            }
        }

        /// <summary>
        /// Saves preferences to disk as indented JSON, creating the parent directory if needed.
        /// </summary>
        public void Save(ShadowStudyUserPrefs prefs)
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
    }
}
