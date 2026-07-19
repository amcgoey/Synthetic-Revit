using System;

namespace Synthetic.RevitDOM.Operations.Merge
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
