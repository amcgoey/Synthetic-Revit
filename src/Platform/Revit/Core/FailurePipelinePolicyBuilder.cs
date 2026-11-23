using System;
using System.Collections.Generic;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Fluent builder for constructing pure in-process <see cref="FailurePipelinePolicy"/> instances.
    /// Enforces non-blanket warning suppression safety policy by default.
    /// </summary>
    public class FailurePipelinePolicyBuilder
    {
        private readonly List<IFailurePolicyRule> _rules = new List<IFailurePolicyRule>();
        private bool _allowBlanketSuppression = false;

        /// <summary>
        /// Registers a rule targeting a specific failure definition GUID.
        /// </summary>
        /// <param name="failureId">The target failure definition GUID.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public FailurePipelinePolicyBuilder OnFailure(Guid failureId)
        {
            _rules.Add(new TargetIdFailurePolicyRule(failureId));
            return this;
        }

        /// <summary>
        /// Registers a rule targeting multiple failure definition GUIDs.
        /// </summary>
        /// <param name="failureIds">Collection of target failure definition GUIDs.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="failureIds"/> is null.</exception>
        public FailurePipelinePolicyBuilder OnFailure(IEnumerable<Guid> failureIds)
        {
            if (failureIds == null) throw new ArgumentNullException(nameof(failureIds));
            _rules.Add(new TargetIdFailurePolicyRule(failureIds));
            return this;
        }

        /// <summary>
        /// Registers a custom pure failure policy rule.
        /// </summary>
        /// <param name="rule">The rule instance.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
        public FailurePipelinePolicyBuilder OnFailure(IFailurePolicyRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
            return this;
        }

        /// <summary>
        /// Registers a rule targeting failures matching a specific severity level.
        /// </summary>
        /// <param name="severity">The failure severity level to target.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public FailurePipelinePolicyBuilder OnSeverity(FailureSeverityLevel severity)
        {
            _rules.Add(new SeverityFailurePolicyRule(severity));
            return this;
        }

        /// <summary>
        /// Configures whether blanket suppression of unmatched warnings is permitted.
        /// Safety policy defaults to <c>false</c>.
        /// </summary>
        /// <param name="allow"><c>true</c> to permit blanket suppression; <c>false</c> otherwise.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public FailurePipelinePolicyBuilder AllowBlanketSuppression(bool allow = true)
        {
            _allowBlanketSuppression = allow;
            return this;
        }

        /// <summary>
        /// Builds and returns the configured <see cref="FailurePipelinePolicy"/>.
        /// </summary>
        /// <returns>A configured <see cref="FailurePipelinePolicy"/> instance.</returns>
        public FailurePipelinePolicy Build()
        {
            var policy = new FailurePipelinePolicy
            {
                AllowBlanketSuppression = _allowBlanketSuppression
            };

            foreach (IFailurePolicyRule rule in _rules)
            {
                policy.AddRule(rule);
            }

            return policy;
        }
    }
}
