namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Pure failure policy rule matching failures by <see cref="FailureSeverityLevel"/>.
    /// </summary>
    public class SeverityFailurePolicyRule : IFailurePolicyRule
    {
        private readonly FailureSeverityLevel _targetSeverity;

        /// <summary>
        /// Initializes a new instance targeting a specific failure severity level.
        /// </summary>
        /// <param name="targetSeverity">The target failure severity level.</param>
        public SeverityFailurePolicyRule(FailureSeverityLevel targetSeverity)
        {
            _targetSeverity = targetSeverity;
        }

        /// <summary>
        /// Gets the target severity level matched by this rule.
        /// </summary>
        public FailureSeverityLevel TargetSeverity => _targetSeverity;

        /// <inheritdoc />
        public bool Matches(FailureRecord? failure)
        {
            return failure != null && failure.Severity == _targetSeverity;
        }

        /// <inheritdoc />
        public FailureHandlingAction EvaluateAction(FailureRecord? failure)
        {
            if (failure == null || !Matches(failure))
            {
                return FailureHandlingAction.PassedThrough;
            }

            if (failure.Severity == FailureSeverityLevel.Warning)
            {
                return FailureHandlingAction.Deleted;
            }

            if (failure.Severity == FailureSeverityLevel.Error)
            {
                return FailureHandlingAction.Resolved;
            }

            return FailureHandlingAction.PassedThrough;
        }
    }
}
