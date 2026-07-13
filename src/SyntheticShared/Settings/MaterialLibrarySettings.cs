using Newtonsoft.Json;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for the material library path.
    /// </summary>
    public class MaterialLibrarySettings : ISettingModule
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
        public const string Name = "MaterialLibrary";

        /// <summary>
        /// Gets or sets the folder path to the material library.
        /// </summary>
        public string LibraryFolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialLibrarySettings"/> class.
        /// </summary>
        public MaterialLibrarySettings() { }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public MaterialLibrarySettings Defaults()
        {
            LibraryFolderPath = "C:\\Materials\\";
            return this;
        }

        /// <summary>
        /// Validates that the folder path exists.
        /// </summary>
        public bool IsValid(Document doc)
        {
            return !string.IsNullOrEmpty(LibraryFolderPath) && Directory.Exists(LibraryFolderPath);
        }
    }
}
