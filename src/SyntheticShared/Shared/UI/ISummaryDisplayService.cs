using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Service interface for displaying process/execution summaries.
    /// </summary>
    public interface ISummaryDisplayService
    {
        /// <summary>
        /// Displays the summary for a given view model.
        /// </summary>
        /// <param name="viewModel">The view model containing the summary data.</param>
        /// <param name="parentWindowHandle">The parent window handle.</param>
        void ShowSummary(object viewModel, IntPtr parentWindowHandle);
    }
}
