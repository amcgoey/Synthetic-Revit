using System;

namespace Synthetic.Features.StandardsManagement
{
    /// <summary>
    /// No-op implementation of <see cref="ISummaryDisplayService"/> for headless testing contexts.
    /// </summary>
    public class NoOpSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Does nothing.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
        }
    }
}
