using System.Collections.Generic;

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

        /// <summary>
        /// Gets the list of recorded failure telemetry records.
        /// </summary>
        public List<FailureRecord> Records { get; } = new List<FailureRecord>();

        /// <summary>
        /// Adds a failure record to the report.
        /// </summary>
        /// <param name="record">The failure record to append.</param>
        public void AddRecord(FailureRecord record)
        {
            if (record != null)
            {
                Records.Add(record);
                if (record.ActionTaken == FailureHandlingAction.Deleted)
                {
                    WarningTripped = true;
                }
            }
        }
    }
}
