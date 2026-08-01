namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Specifies the action taken by a failure preprocessor or rule when evaluating a Revit failure.
    /// </summary>
    public enum FailureHandlingAction
    {
        /// <summary>
        /// Warning was deleted or suppressed.
        /// </summary>
        Deleted,

        /// <summary>
        /// Failure or error was resolved.
        /// </summary>
        Resolved,

        /// <summary>
        /// Failure was logged and execution continued.
        /// </summary>
        Continued,

        /// <summary>
        /// Failure was not handled by rules and passed through to Revit.
        /// </summary>
        PassedThrough
    }
}
