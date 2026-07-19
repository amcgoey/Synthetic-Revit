using Autodesk.Revit.DB;

using Synthetic.Settings;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
namespace Synthetic.Settings
{
    /// <summary>
    /// Represents a settings module that can be serialized/deserialized and validated.
    /// </summary>
    public interface ISettingModule
    {
        /// <summary>
        /// The unique key identifying the settings module.
        /// </summary>
        string ModuleKey { get; }

        /// <summary>
        /// Validates the settings module settings within the context of the active document.
        /// </summary>
        /// <param name="doc">The Revit Document context.</param>
        /// <returns>True if settings are valid; otherwise, false.</returns>
        bool IsValid(Document doc);
    }
}
