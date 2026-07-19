using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Infrastructure.IO;

//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;
using Autodesk.Revit.DB;

using Synthetic.Core;
using Synthetic.Settings;
using Synthetic.Modules.Worksets.Utilities;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Settings
{
    /// <summary>
    /// Configuration settings for worksets, including excel mapping files and group configurations.
    /// </summary>
    public class WorksetSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey { get { return Name; } }

        internal string defaultWorksetFile { get { return "SyntheticWorksets.xlsx"; } }
        internal string defaultWorksetGroup { get { return "Worksets"; } }

        /// <summary>
        /// Name of the WorksetSettings type
        /// </summary>
        [JsonIgnore]
        public const string Name = "Worksets";

        /// <summary>
        /// Gets or sets the workset configuration Excel filename.
        /// </summary>
        public string? WorksetFile { get; set; }

        /// <summary>
        /// Gets or sets the path to the workset Excel file.
        /// </summary>
        public string? WorksetPath { get; set; }

        /// <summary>
        /// Gets or sets the workset group name.
        /// </summary>
        public string? WorksetGroup { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksetSettings"/> class.
        /// </summary>
        public WorksetSettings () { }

        /// <summary>
        /// Constructor using individual parameters to create
        /// </summary>
        /// <param name="file">File name as a string.</param>
        /// <param name="path">Path to file as a string.  If null, app will use the assembly path instead.</param>
        /// <param name="group">Name of workset group as a string.</param>
        public WorksetSettings (string? file, string? path = null, string? group = null)
        {
            this.WorksetFile = file;
            this.WorksetPath = path;
            this.WorksetGroup = group;
        }

        /// <summary>
        /// App default WorksetSettings object
        /// </summary>
        /// <returns>WorksetSettings object with app default settings.</returns>
        public WorksetSettings Defaults ()
        {
            this.WorksetFile = defaultWorksetFile;
            this.WorksetPath = null;
            this.WorksetGroup = defaultWorksetGroup;

            return this;
        }

        /// <summary>
        /// If the WorksetPath setting is a path, return the path, otherwise return the addin's path.
        /// </summary>
        /// <returns>Path as a string</returns>
        public string PathOrDefault()
        {
            string? path = this.WorksetPath;
            return !string.IsNullOrEmpty(path) ? path! : Config.addinPath;
        }

        /// <summary>
        /// Takes the WorksetPath and WorksetFile to create a path to the file.  If the WorksetPath is empty, uses the addin's path.
        /// </summary>
        /// <returns>Full path to the file as a string</returns>
        public string? FullPath()
        {
            return !string.IsNullOrEmpty(this.WorksetFile) ? Path.Combine(this.PathOrDefault(), this.WorksetFile) : null;
        }

        /// <summary>
        /// Validates the settings module within the document context.
        /// </summary>
        /// <param name="doc">The Revit Document.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid (Document doc)
        {
            return this.FileExists() && !string.IsNullOrEmpty(this.WorksetGroup);
        }

        /// <summary>
        /// Checks if the settings are empty (i.e. no WorksetFile is defined).
        /// </summary>
        /// <returns>True if WorksetFile is null or empty.</returns>
        public bool IsSettingsEmpty()
        {
            return string.IsNullOrEmpty(this.WorksetFile);
        }

        /// <summary>
        /// Checks if the Workset File exists at the path
        /// </summary>
        /// <returns>True if the Workset File exists at the path, false if the Settings aren't valid or the file doesn't exist at the path.</returns>
        public bool FileExists ()
        {
            string? path = this.FullPath();
            return path != null && File.Exists(path);
        }

        /// <summary>
        /// Loads workset configurations from the Excel file specified in the settings.
        /// </summary>
        /// <returns>A WorksetUtil utility class containing loaded workset configurations.</returns>
        public WorksetUtil? WorksetsByExcel ()
        {
            WorksetUtil? worksetUtil = null;
            if (this.FileExists())
            {
                string? path = this.FullPath();
                string? worksetGroup = this.WorksetGroup;

                if (path != null) 
                {
                    Excel excel = new Excel(path, worksetGroup);
                    excel.ReadExcel();

                    if (excel.cells != null)
                    {
                        worksetUtil = new WorksetUtil(excel.cells);
                    }
                }
            }
            return worksetUtil;
        }
    }
}
