using System;
using System.Collections.Generic;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Primitive-only Data Transfer Object capturing failure information and telemetry without Revit API dependencies.
    /// </summary>
    public class FailureRecord
    {
        /// <summary>
        /// Gets or sets the unique failure definition GUID.
        /// </summary>
        public Guid FailureDefinitionId { get; set; }

        /// <summary>
        /// Gets or sets the failure severity level.
        /// </summary>
        public FailureSeverityLevel Severity { get; set; } = FailureSeverityLevel.Warning;

        /// <summary>
        /// Gets or sets the descriptive message text of the failure.
        /// </summary>
        public string DescriptionText { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the failing element IDs associated with the failure.
        /// </summary>
        public IReadOnlyList<long> FailingElementIds { get; set; } = Array.Empty<long>();

        /// <summary>
        /// Gets or sets the action taken during failure preprocessing.
        /// </summary>
        public FailureHandlingAction ActionTaken { get; set; } = FailureHandlingAction.PassedThrough;

        /// <summary>
        /// Gets or sets the timestamp when the failure record was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Gets or sets contextual tagging metadata for batch operations.
        /// </summary>
        public string ContextTag { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="FailureRecord"/> class.
        /// </summary>
        public FailureRecord()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FailureRecord"/> class with primitive failure metadata.
        /// </summary>
        /// <param name="failureDefinitionId">The unique failure definition GUID.</param>
        /// <param name="severity">The failure severity level.</param>
        /// <param name="descriptionText">The descriptive message text.</param>
        /// <param name="failingElementIds">Optional failing element IDs.</param>
        /// <param name="actionTaken">The action taken when evaluating the failure.</param>
        /// <param name="contextTag">Optional contextual tag describing the operation scope.</param>
        public FailureRecord(
            Guid failureDefinitionId,
            FailureSeverityLevel severity,
            string? descriptionText,
            IReadOnlyList<long>? failingElementIds = null,
            FailureHandlingAction actionTaken = FailureHandlingAction.PassedThrough,
            string? contextTag = null)
        {
            FailureDefinitionId = failureDefinitionId;
            Severity = severity;
            DescriptionText = descriptionText ?? string.Empty;
            FailingElementIds = failingElementIds ?? Array.Empty<long>();
            ActionTaken = actionTaken;
            ContextTag = contextTag ?? string.Empty;
            Timestamp = DateTime.Now;
        }
    }
}
