using System;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.RevitAPI;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace Synthetic.Modules.MergeDuplicates.Models
{
    /// <summary>
    /// Indicates the recommended resolution action for a duplicate type mapping.
    /// </summary>
    public enum RecommendedAction
    {
        /// <summary>
        /// Merge the duplicate types together.
        /// </summary>
        Merge,

        /// <summary>
        /// Migrate the duplicate type to the primary type.
        /// </summary>
        Migrate,

        /// <summary>
        /// Exclude this duplicate type from actions.
        /// </summary>
        Exclude
    }
}
