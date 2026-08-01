using System;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Failure processing rule that matches failures by severity level.
    /// </summary>
    public class SeverityFailureRule : IFailureRule
    {
        private readonly FailureSeverity _targetSeverity;

        /// <summary>
        /// Initializes a new instance of SeverityFailureRule targeting a specific severity.
        /// </summary>
        /// <param name="targetSeverity">The target failure severity to match.</param>
        public SeverityFailureRule(FailureSeverity targetSeverity)
        {
            _targetSeverity = targetSeverity;
        }

        /// <summary>
        /// Gets the target severity of this rule.
        /// </summary>
        public FailureSeverity TargetSeverity => _targetSeverity;

        /// <inheritdoc />
        public bool Evaluates(FailureMessageAccessor failureMessage)
        {
            if (failureMessage == null) return false;
            return failureMessage.GetSeverity() == _targetSeverity;
        }

        /// <inheritdoc />
        public bool Execute(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage)
        {
            if (failuresAccessor == null || failureMessage == null) return false;
            if (!Evaluates(failureMessage)) return false;

            FailureSeverity severity = failureMessage.GetSeverity();
            if (severity == FailureSeverity.Warning)
            {
                failuresAccessor.DeleteWarning(failureMessage);
                return true;
            }

            if (severity == FailureSeverity.Error)
            {
                failuresAccessor.ResolveFailure(failureMessage);
                return true;
            }

            return false;
        }
    }
}
