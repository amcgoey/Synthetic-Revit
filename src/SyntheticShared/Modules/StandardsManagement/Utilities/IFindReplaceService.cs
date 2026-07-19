using System.Collections.Generic;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.Utilities
{
    /// <summary>
    /// Contract for the Find & Replace execution service.
    /// </summary>
    public interface IFindReplaceService
    {
        /// <summary>
        /// Executes a find and replace operation on a collection of elements.
        /// </summary>
        /// <param name="elements">The elements to edit.</param>
        /// <param name="findText">The text to search for.</param>
        /// <param name="replaceText">The replacement text.</param>
        /// <param name="searchElementNames">True if element names should be searched.</param>
        /// <param name="searchParameterValues">True if editable parameter values should be searched.</param>
        /// <returns>A hashset containing the elements that were modified.</returns>
        HashSet<ElementModel> Execute(
            IEnumerable<ElementModel> elements,
            string findText,
            string replaceText,
            bool searchElementNames,
            bool searchParameterValues);
    }
}
