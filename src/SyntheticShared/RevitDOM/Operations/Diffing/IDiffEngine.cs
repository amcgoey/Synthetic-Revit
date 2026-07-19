using System;
using System.Collections.Generic;
using System.Threading;
using Synthetic.Modules.MergeDuplicates.Models;

namespace Synthetic.RevitDOM.Operations.Diffing
{
    /// <summary>
    /// Generic interface defining the contract for comparing a source data set against a target model/database.
    /// </summary>
    /// <typeparam name="TSource">The type of the source data (e.g. POCO models).</typeparam>
    /// <typeparam name="TTarget">The type of the target database or document (e.g. Revit Document).</typeparam>
    public interface IDiffEngine<in TSource, in TTarget>
    {
        /// <summary>
        /// Compares the source data against the target and returns duplicate and conflict clusters.
        /// </summary>
        /// <param name="source">The incoming source data.</param>
        /// <param name="target">The target database or document context.</param>
        /// <param name="progress">Optional progress reporting delegate.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A collection of duplicate clusters containing diff and conflict information.</returns>
        IEnumerable<DuplicateClusterModel> Compare(
            TSource source,
            TTarget target,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
