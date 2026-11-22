using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Failure processing rule that matches target failure definition IDs.
    /// </summary>
    public class TargetIdFailureRule : IFailureRule
    {
        private readonly HashSet<FailureDefinitionId> _targetIds;

        /// <summary>
        /// Initializes a new instance targeting a single failure definition ID.
        /// </summary>
        /// <param name="targetId">The target failure definition ID.</param>
        /// <exception cref="ArgumentNullException">Thrown when targetId is null.</exception>
        public TargetIdFailureRule(FailureDefinitionId targetId)
            : this(new[] { targetId ?? throw new ArgumentNullException(nameof(targetId)) })
        {
        }

        /// <summary>
        /// Initializes a new instance targeting a set of failure definition IDs.
        /// </summary>
        /// <param name="targetIds">Collection of target failure definition IDs.</param>
        /// <exception cref="ArgumentNullException">Thrown when targetIds is null.</exception>
        public TargetIdFailureRule(IEnumerable<FailureDefinitionId> targetIds)
        {
            if (targetIds == null) throw new ArgumentNullException(nameof(targetIds));

            _targetIds = new HashSet<FailureDefinitionId>();
            foreach (FailureDefinitionId id in targetIds)
            {
                if (id != null)
                {
                    _targetIds.Add(id);
                }
            }
        }

        /// <inheritdoc />
        public bool Evaluates(FailureMessageAccessor failureMessage)
        {
            if (failureMessage == null) return false;
            FailureDefinitionId failureId = failureMessage.GetFailureDefinitionId();
            return failureId != null && _targetIds.Contains(failureId);
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
