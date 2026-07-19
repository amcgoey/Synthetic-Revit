using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Modules.StandardsManagement.ViewModels;
namespace Synthetic.Modules.StandardsManagement.Models
{
    /// <summary>
    /// Represents an entry in the import log, tracking actions taken on Revit elements.
    /// </summary>
    public class ImportLogItem
    {
        /// <summary>
        /// Gets or sets the action performed (e.g. Created, Updated, Renamed, Failed, Canceled).
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the class name of the Revit element.
        /// </summary>
        public string Class { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the element.
        /// </summary>
        public string ElementName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets any details or error messages associated with the action.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
