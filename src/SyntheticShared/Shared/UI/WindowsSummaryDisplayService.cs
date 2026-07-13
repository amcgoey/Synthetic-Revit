using System;
using Synthetic.Modules.StandardsManagement.Views;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Production implementation of ISummaryDisplayService displaying a WPF summary dialog.
    /// </summary>
    public class WindowsSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Displays the WPF ImportSummaryWindow dialog.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
            var window = new ImportSummaryWindow(parentWindowHandle, viewModel);
            window.ShowDialog();
        }
    }

    /// <summary>
    /// No-op implementation of ISummaryDisplayService for headless testing contexts.
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
