using System;
using Synthetic.Modules.RevitDOM;

namespace Synthetic.Modules.StandardsManagement.ViewModels
{
    /// <summary>
    /// TreeView leaf node wrapping a specific ElementModel POCO representation.
    /// </summary>
    public class StandardElementModel : SourceTreeItemViewModel
    {
        /// <summary>
        /// Gets the underlying ElementModel.
        /// </summary>
        public ElementModel Element { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StandardElementModel"/> class.
        /// </summary>
        /// <param name="element">The element model to wrap.</param>
        public StandardElementModel(ElementModel element)
        {
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Name = element.Name ?? string.Empty;
        }
    }
}
