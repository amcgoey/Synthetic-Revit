using Newtonsoft.Json;
using System;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Modules.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for project-specific material paths.
    /// </summary>
    public class ProjectMaterialSettings : ISettingModule
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
        public const string Name = "ProjectMaterials";

        /// <summary>
        /// Gets or sets the default relative path.
        /// </summary>
        public string DefaultRelativePath { get; set; } = ".\\Materials\\";

        /// <summary>
        /// Gets or sets the override folder path.
        /// </summary>
        public string OverrideFolderPath { get; set; } = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectMaterialSettings"/> class.
        /// </summary>
        public ProjectMaterialSettings() { }

        /// <summary>
        /// Populates default values.
        /// </summary>
        /// <returns>This settings instance.</returns>
        public ProjectMaterialSettings Defaults()
        {
            DefaultRelativePath = ".\\Materials\\";
            OverrideFolderPath = "";
            return this;
        }

        /// <summary>
        /// Resolves the absolute directory path of the materials.
        /// </summary>
        /// <param name="doc">The active Revit document.</param>
        /// <returns>The resolved absolute folder path, or null.</returns>
        public string? GetResolvedPath(Document doc)
        {
            // The Override Check (Highest Priority)
            if (!string.IsNullOrEmpty(OverrideFolderPath))
            {
                return OverrideFolderPath;
            }

            // The Null Check
            if (doc == null || !doc.IsValidObject)
            {
                return null;
            }

            // The Cloud Intercept (Crucial)
            if (doc.IsModelInCloud)
            {
                return null;
            }

            // The Unsaved File Check
            if (string.IsNullOrEmpty(doc.PathName))
            {
                return null;
            }

            string? basePath = null;

            // The Workshared Check
            if (doc.IsWorkshared)
            {
                try
                {
                    ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();
                    if (centralModelPath != null)
                    {
                        basePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralModelPath);
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            // The Local Fallback
            if (string.IsNullOrEmpty(basePath))
            {
                basePath = doc.PathName;
            }

            if (string.IsNullOrEmpty(basePath))
            {
                return null;
            }

            // The Path Construction
            try
            {
                string? dir = Path.GetDirectoryName(basePath);
                if (string.IsNullOrEmpty(dir)) return null;

                string relative = DefaultRelativePath ?? ".\\Materials\\";
                return Path.GetFullPath(Path.Combine(dir, relative));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Validates that the resolved folder path exists.
        /// </summary>
        public bool IsValid(Document doc)
        {
            string? path = GetResolvedPath(doc);
            return !string.IsNullOrEmpty(path) && Directory.Exists(path);
        }
    }
}
