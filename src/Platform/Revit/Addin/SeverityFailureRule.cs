using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Revit failure rule adapter matching failures by <see cref="FailureSeverity"/>,
    /// delegating evaluation logic to <see cref="SeverityFailurePolicyRule"/>.
    /// </summary>
    public class SeverityFailureRule : IFailureRule, IPolicyBackedFailureRule
    {
        private readonly SeverityFailurePolicyRule _policyRule;

        /// <summary>
        /// Initializes a new instance of <see cref="SeverityFailureRule"/> targeting a specific severity.
        /// </summary>
        /// <param name="targetSeverity">The target failure severity to match.</param>
        public SeverityFailureRule(FailureSeverity targetSeverity)
        {
            _policyRule = new SeverityFailurePolicyRule(FailureRecordAdapter.ToSeverityLevel(targetSeverity));
        }

        /// <summary>
        /// Gets the target severity of this rule.
        /// </summary>
        public FailureSeverity TargetSeverity => FailureRecordAdapter.ToRevitSeverity(_policyRule.TargetSeverity);

        /// <summary>
        /// Gets the underlying pure failure policy rule.
        /// </summary>
        public SeverityFailurePolicyRule PolicyRule => _policyRule;

        IFailurePolicyRule IPolicyBackedFailureRule.PolicyRule => _policyRule;

        /// <inheritdoc />
        public bool Evaluates(FailureMessageAccessor failureMessage)
        {
            if (failureMessage == null) return false;
            return _policyRule.Matches(FailureRecordAdapter.ToFailureRecord(failureMessage));
        }

        /// <inheritdoc />
        public bool Execute(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage)
        {
            if (failuresAccessor == null || failureMessage == null) return false;

            FailureRecord record = FailureRecordAdapter.ToFailureRecord(failureMessage);
            FailureHandlingAction action = _policyRule.EvaluateAction(record);
            return FailureRecordAdapter.ApplyAction(failuresAccessor, failureMessage, action);
        }
    }
}
