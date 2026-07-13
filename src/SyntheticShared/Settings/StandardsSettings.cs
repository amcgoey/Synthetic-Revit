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
    /// Configuration settings for the standards workflow, storing the path to the standards JSON file.
    /// This module is serialized and saved inside the Revit Document's Extensible Storage schema
    /// to persist the firmwide or project-specific standards path across user sessions.
    /// </summary>
    public class StandardsSettings : ISettingModule
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
        public const string Name = "Standards";

        /// <summary>
        /// Gets or sets the path to the standards JSON file.
        /// </summary>
        public string StandardsFilePath { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardsSettings"/> class.
        /// </summary>
        public StandardsSettings()
        {
            StandardsFilePath = string.Empty;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            if (string.IsNullOrEmpty(StandardsFilePath)) return false;
            try
            {
                return Path.GetExtension(StandardsFilePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
