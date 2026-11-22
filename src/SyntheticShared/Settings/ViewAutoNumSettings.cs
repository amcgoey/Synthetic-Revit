using Autodesk.Revit.DB;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//Aliases for Revit Classes
using RevitDoc = Autodesk.Revit.DB.Document;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared.RevitAPI;
namespace Synthetic.Settings
{
    /// <summary>
    /// Settings for Autonumbering views.
    /// </summary>
    public class ViewAutoNumSettings : ISettingModule
    {
        /// <summary>
        /// Gets the settings module key.
        /// </summary>
        [JsonIgnore]
        public string ModuleKey { get { return Name; } }

        internal string defaultViewAutoNumFamily { get { return "Titleblock Grid Location Marker"; } }
        internal string defaultViewAutoNumFamilyType { get { return "Location Marker INC Titleblock - CD"; } }
        internal string defaultViewAutoNumXGridName { get { return "Grid Size X Direction"; } }
        internal string defaultViewAutoNumYGridName { get { return "Grid Size Y Direction"; } }

        /// <summary>
        /// The setting configuration name.
        /// </summary>
        [JsonIgnore]
        public const string Name = "ViewAutoNumber";

        /// <summary>
        /// Gets or sets the family name used for view autonumbering.
        /// </summary>
        [JsonProperty("viewAutoNumFamily")]
        public string ViewAutoNumFamily { get; set; } = "Titleblock Grid Location Marker";

        /// <summary>
        /// Gets or sets the family type name used for view autonumbering.
        /// </summary>
        [JsonProperty("viewAutoNumFamilyType")]
        public string ViewAutoNumFamilyType { get; set; } = "Location Marker INC Titleblock - CD";

        /// <summary>
        /// Gets or sets the X grid spacing parameter name.
        /// </summary>
        [JsonProperty("viewAutoNumXGridName")]
        public string ViewAutoNumXGridName { get; set; } = "Grid Size X Direction";

        /// <summary>
        /// Gets or sets the Y grid spacing parameter name.
        /// </summary>
        [JsonProperty("viewAutoNumYGridName")]
        public string ViewAutoNumYGridName { get; set; } = "Grid Size Y Direction";

        /// <summary>
        /// Constructor
        /// </summary>
        public ViewAutoNumSettings() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="family">Name of family to serve as the origin</param>
        /// <param name="familytype">Name of the family type</param>
        /// <param name="xGridName">Name of the parameter to determine the X grid spacing</param>
        /// <param name="yGridName">Name of the parameter to determine the Y grid spacing</param>
        public ViewAutoNumSettings(string family, string familytype, string xGridName, string yGridName)
        {
            this.ViewAutoNumFamily = family;
            this.ViewAutoNumFamilyType = familytype;
            this.ViewAutoNumXGridName = xGridName;
            this.ViewAutoNumYGridName = yGridName;
        }

        /// <summary>
        /// Reset the ViewAutoNumSettings properties to the Defaults
        /// </summary>
        /// <returns>The ViewRenumberSettings to allow for chaining</returns>
        public ViewAutoNumSettings Defaults()
        {
            this.ViewAutoNumFamily = defaultViewAutoNumFamily;
            this.ViewAutoNumFamilyType = defaultViewAutoNumFamilyType;
            this.ViewAutoNumXGridName = defaultViewAutoNumXGridName;
            this.ViewAutoNumYGridName = defaultViewAutoNumYGridName;

            return this;
        }

        /// <summary>
        /// Checks if all the setting values exist and the family and type are loaded in the project.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        public bool IsValid (Document doc)
        {
            if (string.IsNullOrEmpty(this.ViewAutoNumFamily) ||
                string.IsNullOrEmpty(this.ViewAutoNumFamilyType) ||
                string.IsNullOrEmpty(this.ViewAutoNumXGridName) ||
                string.IsNullOrEmpty(this.ViewAutoNumYGridName))
            {
                return false;
            }

            try
            {
                var symbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Name.Equals(this.ViewAutoNumFamilyType, StringComparison.OrdinalIgnoreCase) &&
                                          fs.Family.Name.Equals(this.ViewAutoNumFamily, StringComparison.OrdinalIgnoreCase));
                return symbol != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
