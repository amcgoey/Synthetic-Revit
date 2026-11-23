using System.Collections.Generic;

namespace Synthetic.Features.SharedCoordinates.Models
{
    /// <summary>
    /// Encapsulates the parameters required to reposition out-of-sync linked model instances.
    /// </summary>
    public class RepositionLinksRequest
    {
        /// <summary>
        /// Gets or sets the list of selected RevitLinkType identifiers whose instances will be repositioned.
        /// </summary>
        public IReadOnlyList<string> SelectedTypeIds { get; set; } = new List<string>();
    }
}
