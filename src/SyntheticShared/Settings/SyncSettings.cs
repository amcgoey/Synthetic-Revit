using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for project configuration synchronization.
    /// </summary>
    public class SyncSettings : ISettingModule
    {
        /// <summary>
        /// The setting configuration name.
        /// </summary>
        [JsonIgnore]
        public const string Name = "SyncSettings";

        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey => Name;

        /// <summary>
        /// Gets or sets the path to the linked external settings JSON file.
        /// </summary>
        public string LinkedFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncSettings"/> class.
        /// </summary>
        public SyncSettings() { }

        /// <summary>
        /// Validates the settings module within the document context.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid(Document doc)
        {
            return string.IsNullOrEmpty(LinkedFilePath) || File.Exists(LinkedFilePath);
        }
    }
}
