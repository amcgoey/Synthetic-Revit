using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Represents a failure processing rule that evaluates and handles Revit transaction failures.
    /// </summary>
    public interface IFailureRule
    {
        /// <summary>
        /// Evaluates whether this rule applies to the specified failure message.
        /// </summary>
        /// <param name="failureMessage">The failure message accessor.</param>
        /// <returns>True if the rule matches the failure; otherwise, false.</returns>
        bool Evaluates(FailureMessageAccessor failureMessage);

        /// <summary>
        /// Executes resolution or suppression action on the matched failure message.
        /// </summary>
        /// <param name="failuresAccessor">The failures accessor context.</param>
        /// <param name="failureMessage">The failure message accessor to handle.</param>
        /// <returns>True if the failure was successfully handled; otherwise, false.</returns>
        bool Execute(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage);
    }
}
