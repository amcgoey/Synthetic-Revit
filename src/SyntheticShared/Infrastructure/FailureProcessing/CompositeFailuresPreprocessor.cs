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
        /// Gets or sets the diagnostic report for this preprocessor instance.
        /// </summary>
        public FailureProcessingReport Report { get; set; }

        /// <summary>
        /// Initializes a new instance of the CompositeFailuresPreprocessor class with a default FailureProcessingReport.
        /// </summary>
        public CompositeFailuresPreprocessor()
            : this(new FailureProcessingReport())
        {
        }

        /// <summary>
        /// Initializes a new instance of the CompositeFailuresPreprocessor class with the specified FailureProcessingReport.
        /// </summary>
        /// <param name="report">The diagnostic failure processing report to record telemetry.</param>
        public CompositeFailuresPreprocessor(FailureProcessingReport report)
        {
            Report = report ?? new FailureProcessingReport();
        }

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
                foreach (IFailureRule rule in _rules)
                {
                    if (rule.Evaluates(failure))
                    {
                        FailureSeverity severity = failure.GetSeverity();
                        if (rule.Execute(failuresAccessor, failure))
                        {
                            handledAny = true;
                            if (severity == FailureSeverity.Warning && Report != null)
                            {
                                Report.WarningTripped = true;
                            }
                            break;
                        }
                    }
                }
            }

            return handledAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }
    }
}
