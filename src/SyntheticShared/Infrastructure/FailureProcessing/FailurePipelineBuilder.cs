using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Fluent builder for constructing composite failure processing pipelines.
    /// Enforces non-blanket warning suppression safety policy by default.
    /// </summary>
    public class FailurePipelineBuilder
    {
        private readonly List<IFailureRule> _rules = new List<IFailureRule>();
        private bool _allowBlanketSuppression = false;

        /// <summary>
        /// Registers a failure rule targeting a specific failure definition ID.
        /// </summary>
        /// <param name="failureId">The target failure definition ID.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when failureId is null.</exception>
        public FailurePipelineBuilder OnFailure(FailureDefinitionId failureId)
        {
            if (failureId == null) throw new ArgumentNullException(nameof(failureId));
            _rules.Add(new TargetIdFailureRule(failureId));
            return this;
        }

        /// <summary>
        /// Registers a failure rule targeting multiple failure definition IDs.
        /// </summary>
        /// <param name="failureIds">Collection of target failure definition IDs.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when failureIds is null.</exception>
        public FailurePipelineBuilder OnFailure(IEnumerable<FailureDefinitionId> failureIds)
        {
            if (failureIds == null) throw new ArgumentNullException(nameof(failureIds));
            _rules.Add(new TargetIdFailureRule(failureIds));
            return this;
        }

        /// <summary>
        /// Registers a custom failure rule in the pipeline.
        /// </summary>
        /// <param name="rule">The failure rule instance.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when rule is null.</exception>
        public FailurePipelineBuilder OnFailure(IFailureRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
            return this;
        }

        /// <summary>
        /// Registers a failure rule targeting failures matching a specific severity level.
        /// </summary>
        /// <param name="severity">The failure severity to target.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public FailurePipelineBuilder OnSeverity(FailureSeverity severity)
        {
            _rules.Add(new SeverityFailureRule(severity));
            return this;
        }

        /// <summary>
        /// Configures whether blanket suppression of unmatched warnings is permitted.
        /// Safety policy default is false.
        /// </summary>
        /// <param name="allow">True to permit blanket suppression; false otherwise.</param>
        /// <returns>This builder instance for fluent chaining.</returns>
        public FailurePipelineBuilder AllowBlanketSuppression(bool allow = true)
        {
            _allowBlanketSuppression = allow;
            return this;
        }

        /// <summary>
        /// Builds and returns the configured CompositeFailuresPreprocessor pipeline.
        /// </summary>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public CompositeFailuresPreprocessor Build()
        {
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor
            {
                AllowBlanketSuppression = _allowBlanketSuppression
            };

            foreach (IFailureRule rule in _rules)
            {
                preprocessor.AddRule(rule);
            }

            return preprocessor;
        }
    }
}
