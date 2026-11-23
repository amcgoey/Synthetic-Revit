namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Platform-neutral failure severity levels mirroring Revit failure severities without Revit API dependencies.
    /// </summary>
    public enum FailureSeverityLevel
    {
        /// <summary>
        /// No severity specified.
        /// </summary>
        None = 0,

        /// <summary>
        /// Non-blocking warning that can be deleted or ignored.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Error that blocks commit unless resolved.
        /// </summary>
        Error = 2,

        /// <summary>
        /// Critical document corruption failure.
        /// </summary>
        DocumentCorruption = 3
    }
}
