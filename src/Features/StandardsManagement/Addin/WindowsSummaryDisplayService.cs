using System;
using Synthetic.Modules.StandardsManagement.Views;

namespace Synthetic.Features.StandardsManagement
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
}
