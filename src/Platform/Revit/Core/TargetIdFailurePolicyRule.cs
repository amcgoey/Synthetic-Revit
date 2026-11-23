using System;
using System.Collections.Generic;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Pure failure policy rule matching failures by target failure definition GUIDs.
    /// </summary>
    public class TargetIdFailurePolicyRule : IFailurePolicyRule
    {
        private readonly HashSet<Guid> _targetIds;

        /// <summary>
        /// Initializes a new instance targeting a single failure definition GUID.
        /// </summary>
        /// <param name="targetId">The target failure definition GUID.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="targetId"/> is <see cref="Guid.Empty"/>.</exception>
        public TargetIdFailurePolicyRule(Guid targetId)
            : this(new[] { targetId == Guid.Empty ? throw new ArgumentException("Target failure definition ID cannot be empty.", nameof(targetId)) : targetId })
        {
        }

        /// <summary>
        /// Initializes a new instance targeting a collection of failure definition GUIDs.
        /// </summary>
        /// <param name="targetIds">The target failure definition GUIDs.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="targetIds"/> is null.</exception>
        public TargetIdFailurePolicyRule(IEnumerable<Guid> targetIds)
        {
            if (targetIds == null) throw new ArgumentNullException(nameof(targetIds));

            _targetIds = new HashSet<Guid>();
            foreach (Guid id in targetIds)
            {
                if (id != Guid.Empty)
                {
                    _targetIds.Add(id);
                }
            }
        }

        /// <summary>
        /// Gets the set of target failure definition GUIDs matched by this rule.
        /// </summary>
        public IReadOnlyCollection<Guid> TargetIds => _targetIds;

        /// <inheritdoc />
        public bool Matches(FailureRecord? failure)
        {
            return failure != null && failure.FailureDefinitionId != Guid.Empty && _targetIds.Contains(failure.FailureDefinitionId);
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
