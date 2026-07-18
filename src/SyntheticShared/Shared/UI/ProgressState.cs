using System;

namespace Synthetic.Shared.UI
{
    /// <summary>
    /// Represents the progress state of a modeless background operation.
    /// </summary>
    public class ProgressState
    {
        /// <summary>
        /// Gets or sets the main task description message.
        /// </summary>
        public string TaskDescription { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the current item being processed.
        /// </summary>
        public string CurrentItemName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the progress index.
        /// </summary>
        public int ProgressIndex { get; set; }

        /// <summary>
        /// Gets or sets the maximum bounds.
        /// </summary>
        public int MaximumBounds { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the task is completed.
        /// </summary>
        public bool IsCompleted { get; set; }
    }
}
