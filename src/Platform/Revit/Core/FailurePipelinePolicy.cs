using System;
using System.Collections.Generic;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Pure in-process composite failure policy that evaluates <see cref="FailureRecord"/> items
    /// against an ordered rule list and records telemetry into a <see cref="FailureProcessingReport"/>.
    /// </summary>
    public class FailurePipelinePolicy
    {
        private readonly List<IFailurePolicyRule> _rules = new List<IFailurePolicyRule>();
        private string _contextTag = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether blanket suppression of unmatched warnings is permitted.
        /// Defaults to <c>false</c> to enforce non-blanket warning suppression safety policy.
        /// </summary>
        public bool AllowBlanketSuppression { get; set; } = false;

        /// <summary>
        /// Gets or sets contextual tagging metadata attached to evaluated <see cref="FailureRecord"/> items.
        /// </summary>
        public string ContextTag
        {
            get => _contextTag;
            set => _contextTag = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the diagnostic report capturing evaluated failure records.
        /// </summary>
        public FailureProcessingReport Report { get; set; }

        /// <summary>
        /// Gets the ordered rules registered in this policy.
        /// </summary>
        public IReadOnlyList<IFailurePolicyRule> Rules => _rules;

        /// <summary>
        /// Initializes a new instance of <see cref="FailurePipelinePolicy"/> with a default report.
        /// </summary>
        public FailurePipelinePolicy()
            : this(new FailureProcessingReport())
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="FailurePipelinePolicy"/> with the specified report.
        /// </summary>
        /// <param name="report">The report to populate during evaluation.</param>
        public FailurePipelinePolicy(FailureProcessingReport? report)
        {
            Report = report ?? new FailureProcessingReport();
        }

        /// <summary>
        /// Sets the contextual scope tag for subsequent batch evaluations.
        /// </summary>
        /// <param name="contextTag">The context tag string, or null to clear.</param>
        public void SetContext(string? contextTag)
        {
            _contextTag = contextTag ?? string.Empty;
        }

        /// <summary>
        /// Appends a failure policy rule to the evaluation pipeline.
        /// </summary>
        /// <param name="rule">The rule to add.</param>
        /// <returns>This policy instance for fluent chaining.</returns>
        public FailurePipelinePolicy AddRule(IFailurePolicyRule? rule)
        {
            if (rule != null)
            {
                _rules.Add(rule);
            }

            return this;
        }

        /// <summary>
        /// Evaluates fallback blanket-suppression policy when no explicit rule handled a failure.
        /// </summary>
        /// <param name="failure">The failure record being evaluated.</param>
        /// <returns><see cref="FailureHandlingAction.Deleted"/> if blanket suppression applies; otherwise <see cref="FailureHandlingAction.PassedThrough"/>.</returns>
        public FailureHandlingAction EvaluateFallback(FailureRecord? failure)
        {
            if (failure != null && AllowBlanketSuppression && failure.Severity == FailureSeverityLevel.Warning)
            {
                return FailureHandlingAction.Deleted;
            }

            return FailureHandlingAction.PassedThrough;
        }

        /// <summary>
        /// Evaluates a single failure record against registered rules and fallback policy,
        /// recording the resulting telemetry entry in <see cref="Report"/>.
        /// </summary>
        /// <param name="failure">The input failure record.</param>
        /// <returns>The evaluated <see cref="FailureRecord"/> with stamped action and context tag.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
        public FailureRecord Evaluate(FailureRecord failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));

            FailureHandlingAction actionTaken = FailureHandlingAction.PassedThrough;
            bool handled = false;

            foreach (IFailurePolicyRule rule in _rules)
            {
                if (rule.Matches(failure))
                {
                    FailureHandlingAction candidateAction = rule.EvaluateAction(failure);
                    if (candidateAction == FailureHandlingAction.Deleted || candidateAction == FailureHandlingAction.Resolved)
                    {
                        actionTaken = candidateAction;
                        handled = true;
                        break;
                    }
                }
            }

            if (!handled)
            {
                actionTaken = EvaluateFallback(failure);
            }

            var evaluated = new FailureRecord(
                failure.FailureDefinitionId,
                failure.Severity,
                failure.DescriptionText,
                failure.FailingElementIds,
                actionTaken,
                _contextTag);

            Report?.AddRecord(evaluated);
            return evaluated;
        }

        /// <summary>
        /// Evaluates a batch of failure records in sequence.
        /// </summary>
        /// <param name="failures">The failure records to evaluate.</param>
        /// <returns><c>true</c> if any failure was deleted or resolved (commit should proceed); otherwise <c>false</c>.</returns>
        public bool EvaluateAll(IEnumerable<FailureRecord>? failures)
        {
            if (failures == null)
            {
                return false;
            }

            bool handledAny = false;
            foreach (FailureRecord failure in failures)
            {
                if (failure == null)
                {
                    continue;
                }

                FailureRecord evaluated = Evaluate(failure);
                if (evaluated.ActionTaken == FailureHandlingAction.Deleted ||
                    evaluated.ActionTaken == FailureHandlingAction.Resolved)
                {
                    handledAny = true;
                }
            }

            return handledAny;
        }
    }
}
