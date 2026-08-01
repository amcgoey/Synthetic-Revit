using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Composite failures preprocessor that executes a chain of failure rules in sequence.
    /// Enforces non-blanket warning suppression safety policy by returning Continue when no rules handle a failure.
    /// </summary>
    public class CompositeFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly List<IFailureRule> _rules = new List<IFailureRule>();

        /// <summary>
        /// Gets or sets a value indicating whether blanket suppression of unmatched warnings is allowed.
        /// Defaults to false to enforce anti-blanket warning suppression safety policy.
        /// </summary>
        public bool AllowBlanketSuppression { get; set; } = false;

        /// <summary>
        /// Gets the collection of rules registered in this preprocessor.
        /// </summary>
        public IReadOnlyList<IFailureRule> Rules => _rules;

        /// <summary>
        /// Adds a failure rule to the composite pipeline.
        /// </summary>
        /// <param name="rule">The failure rule to add.</param>
        /// <returns>This composite failures preprocessor instance for fluent chaining.</returns>
        public CompositeFailuresPreprocessor AddRule(IFailureRule rule)
        {
            if (rule != null)
            {
                _rules.Add(rule);
            }
            return this;
        }

        /// <inheritdoc />
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            if (failuresAccessor == null) return FailureProcessingResult.Continue;

            IList<FailureMessageAccessor> failureMessages = failuresAccessor.GetFailureMessages();
            if (failureMessages == null || failureMessages.Count == 0)
            {
                return FailureProcessingResult.Continue;
            }

            bool handledAny = false;

            foreach (FailureMessageAccessor failure in failureMessages)
            {
                bool ruleMatched = false;

                foreach (IFailureRule rule in _rules)
                {
                    if (rule.Evaluates(failure))
                    {
                        if (rule.Execute(failuresAccessor, failure))
                        {
                            handledAny = true;
                            ruleMatched = true;
                            break;
                        }
                    }
                }

                if (!ruleMatched && AllowBlanketSuppression)
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        handledAny = true;
                    }
                }
            }

            return handledAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }
}
