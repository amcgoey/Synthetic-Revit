using System;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.StandardsManagement
{
    /// <summary>
    /// Test double implementation of ISummaryDisplayService recording invocations without displaying UI.
    /// </summary>
    public class FakeSummaryDisplayService : ISummaryDisplayService
    {
        /// <summary>
        /// Gets the number of times ShowSummary has been called.
        /// </summary>
        public int ShowCallCount { get; private set; }

        /// <summary>
        /// Gets the last view model object passed to ShowSummary.
        /// </summary>
        public object? LastViewModel { get; private set; }

        /// <summary>
        /// Records the invocation.
        /// </summary>
        public void ShowSummary(object viewModel, IntPtr parentWindowHandle)
        {
            ShowCallCount++;
            LastViewModel = viewModel;
        }
    }
}
