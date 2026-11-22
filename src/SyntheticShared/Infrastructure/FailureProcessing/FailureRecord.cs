using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Data Transfer Object capturing detailed Revit failure information and telemetry.
    /// </summary>
    public class FailureRecord
    {
        /// <summary>
        /// Gets or sets the unique failure definition ID.
        /// </summary>
        public FailureDefinitionId FailureDefinitionId { get; set; }

        /// <summary>
        /// Gets or sets the failure severity level.
        /// </summary>
        public FailureSeverity Severity { get; set; } = FailureSeverity.Warning;

        /// <summary>
        /// Gets or sets the descriptive message text of the failure.
        /// </summary>
        public string DescriptionText { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of failing element IDs associated with the failure.
        /// </summary>
        public List<ElementId> FailingElementIds { get; set; } = new List<ElementId>();

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
        /// Initializes a new instance of the <see cref="FailureRecord"/> class from a Revit failure message accessor.
        /// </summary>
        /// <param name="accessor">The failure message accessor.</param>
        /// <param name="actionTaken">The action taken when handling the failure.</param>
        /// <param name="contextTag">Optional contextual tag describing the operation scope.</param>
        public FailureRecord(FailureMessageAccessor accessor, FailureHandlingAction actionTaken, string contextTag = "")
        {
            if (accessor != null)
            {
                FailureDefinitionId = accessor.GetFailureDefinitionId();
                Severity = accessor.GetSeverity();
                DescriptionText = accessor.GetDescriptionText() ?? string.Empty;
                ICollection<ElementId> elementIds = accessor.GetFailingElementIds();
                FailingElementIds = elementIds != null ? new List<ElementId>(elementIds) : new List<ElementId>();
            }
            ActionTaken = actionTaken;
            ContextTag = contextTag ?? string.Empty;
            Timestamp = DateTime.Now;
        }
    }
}
