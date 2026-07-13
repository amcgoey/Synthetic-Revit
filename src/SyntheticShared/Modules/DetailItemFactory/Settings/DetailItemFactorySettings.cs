using Newtonsoft.Json;
using Synthetic.Modules.DetailItemFactory.Commands;
using Synthetic.Modules.DetailItemFactory.Handlers;
using Synthetic.Modules.DetailItemFactory.ViewModels;
using Synthetic.Modules.DetailItemFactory.Views;
using Synthetic.Modules.DetailItemFactory.Settings;

using System;
using System.IO;
using Autodesk.Revit.DB;

using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.DetailItemFactory.Settings
{
    /// <summary>
    /// Configuration settings for the Detail Item Factory module.
    /// This module is serialized and saved inside the Revit Document's Extensible Storage schema.
    /// </summary>
    public class DetailItemFactorySettings : ISettingModule
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
        public const string Name = "DetailItemFactorySettings";

        /// <summary>
        /// Gets or sets the default output folder path.
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Gets or sets the default target subcategory name.
        /// </summary>
        public string TargetSubcategory { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DetailItemFactorySettings"/> class.
        /// </summary>
        public DetailItemFactorySettings()
        {
            OutputPath = string.Empty;
            TargetSubcategory = "Detail Items";
        }

        /// <summary>
        /// App default settings.
        /// </summary>
        /// <returns>DetailItemFactorySettings object with default configurations.</returns>
        public DetailItemFactorySettings Defaults()
        {
            OutputPath = string.Empty;
            TargetSubcategory = "Detail Items";
            return this;
        }

        /// <summary>
        /// Validates settings module.
        /// </summary>
        public bool IsValid(Document doc)
        {
            if (!string.IsNullOrEmpty(OutputPath) && !Directory.Exists(OutputPath))
            {
                return false;
            }
            return true;
        }
    }
}
