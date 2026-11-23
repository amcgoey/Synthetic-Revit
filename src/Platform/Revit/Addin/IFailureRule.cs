using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Represents a Revit failure processing rule adapter that evaluates and handles transaction failures.
    /// </summary>
    public interface IFailureRule
    {
        /// <summary>
        /// Evaluates whether this rule applies to the specified failure message.
        /// </summary>
        /// <param name="failureMessage">The failure message accessor.</param>
        /// <returns><c>true</c> if the rule matches the failure; otherwise, <c>false</c>.</returns>
        bool Evaluates(FailureMessageAccessor failureMessage);

        /// <summary>
        /// Executes resolution or suppression action on the matched failure message.
        /// </summary>
        /// <param name="failuresAccessor">The failures accessor context.</param>
        /// <param name="failureMessage">The failure message accessor to handle.</param>
        /// <returns><c>true</c> if the failure was successfully handled; otherwise, <c>false</c>.</returns>
        bool Execute(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage);
    }
}
