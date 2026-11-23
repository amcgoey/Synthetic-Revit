using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Revit failure rule adapter matching target <see cref="FailureDefinitionId"/>s,
    /// delegating evaluation logic to <see cref="TargetIdFailurePolicyRule"/>.
    /// </summary>
    public class TargetIdFailureRule : IFailureRule, IPolicyBackedFailureRule
    {
        private readonly TargetIdFailurePolicyRule _policyRule;

        /// <summary>
        /// Initializes a new instance targeting a single failure definition ID.
        /// </summary>
        /// <param name="targetId">The target failure definition ID.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetId"/> is null.</exception>
        public TargetIdFailureRule(FailureDefinitionId targetId)
            : this(new[] { targetId ?? throw new ArgumentNullException(nameof(targetId)) })
        {
        }

        /// <summary>
        /// Initializes a new instance targeting a set of failure definition IDs.
        /// </summary>
        /// <param name="targetIds">Collection of target failure definition IDs.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetIds"/> is null.</exception>
        public TargetIdFailureRule(IEnumerable<FailureDefinitionId> targetIds)
        {
            if (targetIds == null) throw new ArgumentNullException(nameof(targetIds));

            var guids = new List<Guid>();
            foreach (FailureDefinitionId id in targetIds)
            {
                if (id != null && id.Guid != Guid.Empty)
                {
                    guids.Add(id.Guid);
                }
            }

            _policyRule = new TargetIdFailurePolicyRule(guids);
        }

        /// <summary>
        /// Gets the underlying pure failure policy rule.
        /// </summary>
        public TargetIdFailurePolicyRule PolicyRule => _policyRule;

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
