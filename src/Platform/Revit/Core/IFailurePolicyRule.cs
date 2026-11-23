namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Pure in-process failure rule descriptor evaluated against primitive <see cref="FailureRecord"/> instances.
    /// </summary>
    public interface IFailurePolicyRule
    {
        /// <summary>
        /// Determines whether this rule matches the specified failure record.
        /// </summary>
        /// <param name="failure">The failure record to evaluate.</param>
        /// <returns><c>true</c> if the rule matches; otherwise <c>false</c>.</returns>
        bool Matches(FailureRecord? failure);

        /// <summary>
        /// Computes the handling action for a matched failure record.
        /// </summary>
        /// <param name="failure">The failure record to evaluate.</param>
        /// <returns>The resolved <see cref="FailureHandlingAction"/>.</returns>
        FailureHandlingAction EvaluateAction(FailureRecord? failure);
    }
}
