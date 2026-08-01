using System;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Structured diagnostic report capturing failure processing results and telemetry.
    /// </summary>
    public class FailureProcessingReport
    {
        /// <summary>
        /// Gets or sets a value indicating whether any failure warnings were tripped and suppressed.
        /// </summary>
        public bool WarningTripped { get; set; } = false;
    }
}
