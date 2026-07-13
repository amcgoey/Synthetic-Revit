using Newtonsoft.Json;
using System.IO;
using System.Collections.Generic;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for file utility operations, including archive and alternate paths.
    /// </summary>
    public class FileUtilitySettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// The name of this settings module key.
        /// </summary>
        [JsonIgnore]
        public const string Name = "FileUtility";

        /// <summary>
        /// Gets or sets the list of archive directories.
        /// </summary>
        public List<string> ArchiveDirectories { get; set; }

        /// <summary>
        /// Gets or sets the dictionary of alternate paths and their replacements.
        /// </summary>
        public Dictionary<string, List<string>> AlternatePaths { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileUtilitySettings"/> class.
        /// </summary>
        public FileUtilitySettings()
        {
            ArchiveDirectories = new List<string>();
            AlternatePaths = new Dictionary<string, List<string>>();
        }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public FileUtilitySettings Defaults()
        {
            ArchiveDirectories = new List<string>();
            AlternatePaths = new Dictionary<string, List<string>>();
            return this;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            return true;
        }
    }
}
