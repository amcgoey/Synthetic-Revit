using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Thin Revit <see cref="IFailuresPreprocessor"/> adapter delegating rule policy, blanket-suppression guards,
    /// context tagging, and telemetry recording to <see cref="FailurePipelinePolicy"/>.
    /// </summary>
    public class CompositeFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly FailurePipelinePolicy _policy;
        private readonly List<IFailureRule> _rules = new List<IFailureRule>();

        /// <summary>
        /// Gets the underlying pure failure evaluation policy.
        /// </summary>
        public FailurePipelinePolicy Policy => _policy;

        /// <summary>
        /// Gets or sets a value indicating whether blanket suppression of unmatched warnings is allowed.
        /// Defaults to <c>false</c> to enforce anti-blanket warning suppression safety policy.
        /// </summary>
        public bool AllowBlanketSuppression
        {
            get => _policy.AllowBlanketSuppression;
            set => _policy.AllowBlanketSuppression = value;
        }

        /// <summary>
        /// Gets or sets contextual tagging metadata for batch operations.
        /// </summary>
        public string ContextTag
        {
            get => _policy.ContextTag;
            set => _policy.ContextTag = value;
        }

        /// <summary>
        /// Sets a contextual scope tag for batch loops.
        /// </summary>
        /// <param name="contextTag">The context tag string.</param>
        public void SetContext(string? contextTag)
        {
            _policy.SetContext(contextTag);
        }

        /// <summary>
        /// Gets or sets the diagnostic report for this preprocessor instance.
        /// </summary>
        public FailureProcessingReport Report
        {
            get => _policy.Report;
            set => _policy.Report = value ?? new FailureProcessingReport();
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CompositeFailuresPreprocessor"/> with a default report.
        /// </summary>
        public CompositeFailuresPreprocessor()
            : this(new FailurePipelinePolicy())
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CompositeFailuresPreprocessor"/> with the specified report.
        /// </summary>
        /// <param name="report">The diagnostic failure processing report to record telemetry.</param>
        public CompositeFailuresPreprocessor(FailureProcessingReport? report)
            : this(new FailurePipelinePolicy(report))
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="CompositeFailuresPreprocessor"/> backed by an existing policy.
        /// </summary>
        /// <param name="policy">The pure failure pipeline policy.</param>
        public CompositeFailuresPreprocessor(FailurePipelinePolicy? policy)
        {
            _policy = policy ?? new FailurePipelinePolicy();
            foreach (IFailurePolicyRule policyRule in _policy.Rules)
            {
                _rules.Add(new PolicyRuleAdapter(policyRule));
            }
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
        public CompositeFailuresPreprocessor AddRule(IFailureRule? rule)
        {
            if (rule != null)
            {
                _rules.Add(rule);
                if (rule is IPolicyBackedFailureRule backed)
                {
                    _policy.AddRule(backed.PolicyRule);
                }
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
                FailureRecord record = FailureRecordAdapter.ToFailureRecord(failure, _policy.ContextTag);
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

                            record.ActionTaken = severity == FailureSeverity.Warning
                                ? FailureHandlingAction.Deleted
                                : (severity == FailureSeverity.Error ? FailureHandlingAction.Resolved : FailureHandlingAction.Continued);

                            RecordAndLogFailure(record);
                            break;
                        }
                    }
                }

                if (!ruleMatched)
                {
                    FailureHandlingAction fallbackAction = _policy.EvaluateFallback(record);
                    if (fallbackAction == FailureHandlingAction.Deleted)
                    {
                        failuresAccessor.DeleteWarning(failure);
                        handledAny = true;
                    }

                    record.ActionTaken = fallbackAction;
                    RecordAndLogFailure(record);
                }
            }

            return handledAny ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
        }

        private void RecordAndLogFailure(FailureRecord record)
        {
            Report?.AddRecord(record);
            LogTelemetry(record);
        }

        private static void LogTelemetry(FailureRecord? record)
        {
            if (record == null) return;

            string logMessage = $"[FailureTelemetry] ID: {record.FailureDefinitionId} | Severity: {record.Severity} | Action: {record.ActionTaken} | Context: {record.ContextTag} | Desc: {record.DescriptionText}";
            Debug.WriteLine(logMessage);
        }

        private sealed class PolicyRuleAdapter : IFailureRule
        {
            private readonly IFailurePolicyRule _policyRule;

            public PolicyRuleAdapter(IFailurePolicyRule policyRule)
            {
                _policyRule = policyRule ?? throw new ArgumentNullException(nameof(policyRule));
            }

            public bool Evaluates(FailureMessageAccessor failureMessage)
            {
                if (failureMessage == null) return false;
                return _policyRule.Matches(FailureRecordAdapter.ToFailureRecord(failureMessage));
            }

            public bool Execute(FailuresAccessor failuresAccessor, FailureMessageAccessor failureMessage)
            {
                if (failuresAccessor == null || failureMessage == null) return false;
                FailureRecord record = FailureRecordAdapter.ToFailureRecord(failureMessage);
                FailureHandlingAction action = _policyRule.EvaluateAction(record);
                return FailureRecordAdapter.ApplyAction(failuresAccessor, failureMessage, action);
            }
        }
    }
}
