using System;
using System.Threading;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Platform.UI;

namespace Synthetic.Features.DetailItemFactory.Adapters
{
    /// <summary>
    /// Single-seam Revit port isolating Detail Item Factory session harvesting and synchronous batch execution.
    /// </summary>
    public interface IDetailItemFactoryAdapter
    {
        /// <summary>
        /// Harvests active view classification, guarded default output folder, settings, subcategories,
        /// and eligible selected elements into an immutable Core snapshot.
        /// </summary>
        DetailItemSessionSnapshot CaptureSessionSnapshot();

        /// <summary>
        /// Executes the planned Detail Item batch synchronously on Revit's main thread and returns a typed outcome.
        /// </summary>
        DetailItemBatchOutcome ExecuteBatch(
            DetailItemBatchPlan plan,
            IProgress<ProgressState>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
