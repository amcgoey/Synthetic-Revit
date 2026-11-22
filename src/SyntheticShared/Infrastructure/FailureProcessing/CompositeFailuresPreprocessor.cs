using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private string _contextTag = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether blanket suppression of unmatched warnings is allowed.
        /// Defaults to false to enforce anti-blanket warning suppression safety policy.
        /// </summary>
        public bool AllowBlanketSuppression { get; set; } = false;

        /// <summary>
        /// Gets or sets contextual tagging metadata for batch operations.
        /// </summary>
        public string ContextTag
        {
            get => _contextTag;
            set => _contextTag = value ?? string.Empty;
        }

        /// <summary>
        /// Sets a contextual scope tag for batch loops.
        /// </summary>
        /// <param name="contextTag">The context tag string.</param>
        public void SetContext(string contextTag)
        {
            _contextTag = contextTag ?? string.Empty;
        }

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
                bool ruleMatched = false;

                foreach (IFailureRule rule in _rules)
                {
                    if (rule.Evaluates(failure))
                    {
                        FailureSeverity severity = failure.GetSeverity();
                        if (rule.Execute(failuresAccessor, failure))
                        {
                            handledAny = true;
                            ruleMatched = true;

                            FailureHandlingAction action = severity == FailureSeverity.Warning
                                ? FailureHandlingAction.Deleted
                                : (severity == FailureSeverity.Error ? FailureHandlingAction.Resolved : FailureHandlingAction.Continued);

                            RecordAndLogFailure(failure, action);
                            break;
                        }
                    }
                }

                if (!ruleMatched)
                {
                    if (AllowBlanketSuppression && failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        handledAny = true;
                        RecordAndLogFailure(failure, FailureHandlingAction.Deleted);
                    }
                    else
                    {
                        RecordAndLogFailure(failure, FailureHandlingAction.PassedThrough);
                    }
                }
            }

            return handledAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }

        private void RecordAndLogFailure(FailureMessageAccessor failure, FailureHandlingAction actionTaken)
        {
            var record = new FailureRecord(failure, actionTaken, _contextTag);
            Report?.AddRecord(record);
            LogTelemetry(record);
        }

        private static void LogTelemetry(FailureRecord record)
        {
            if (record == null) return;

            string logMessage = $"[FailureTelemetry] ID: {record.FailureDefinitionId} | Severity: {record.Severity} | Action: {record.ActionTaken} | Context: {record.ContextTag} | Desc: {record.DescriptionText}";
            Debug.WriteLine(logMessage);
        }
    }
}
