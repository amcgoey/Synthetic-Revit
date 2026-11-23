using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    internal interface IPolicyBackedFailureRule
    {
        IFailurePolicyRule PolicyRule { get; }
    }

    internal static class FailureRecordAdapter
    {
        public static FailureSeverityLevel ToSeverityLevel(FailureSeverity severity)
        {
            return severity switch
            {
                FailureSeverity.None => FailureSeverityLevel.None,
                FailureSeverity.Warning => FailureSeverityLevel.Warning,
                FailureSeverity.Error => FailureSeverityLevel.Error,
                FailureSeverity.DocumentCorruption => FailureSeverityLevel.DocumentCorruption,
                _ => FailureSeverityLevel.None
            };
        }

        public static FailureSeverity ToRevitSeverity(FailureSeverityLevel severityLevel)
        {
            return severityLevel switch
            {
                FailureSeverityLevel.None => FailureSeverity.None,
                FailureSeverityLevel.Warning => FailureSeverity.Warning,
                FailureSeverityLevel.Error => FailureSeverity.Error,
                FailureSeverityLevel.DocumentCorruption => FailureSeverity.DocumentCorruption,
                _ => FailureSeverity.None
            };
        }

        public static FailureRecord ToFailureRecord(FailureMessageAccessor? accessor, string? contextTag = null)
        {
            if (accessor == null)
            {
                return new FailureRecord
                {
                    ContextTag = contextTag ?? string.Empty
                };
            }

            FailureDefinitionId? defId = accessor.GetFailureDefinitionId();
            Guid guid = defId != null ? defId.Guid : Guid.Empty;
            FailureSeverityLevel severity = ToSeverityLevel(accessor.GetSeverity());
            string description = accessor.GetDescriptionText() ?? string.Empty;

            ICollection<ElementId>? elementIds = accessor.GetFailingElementIds();
            long[] failingIds;
            if (elementIds == null || elementIds.Count == 0)
            {
                failingIds = Array.Empty<long>();
            }
            else
            {
                failingIds = new long[elementIds.Count];
                int index = 0;
                foreach (ElementId id in elementIds)
                {
                    if (id == null)
                    {
                        failingIds[index++] = -1;
                        continue;
                    }
#if REVIT2023
                    failingIds[index++] = id.IntegerValue;
#else
                    failingIds[index++] = id.Value;
#endif
                }
            }

            return new FailureRecord(
                guid,
                severity,
                description,
                failingIds,
                FailureHandlingAction.PassedThrough,
                contextTag);
        }

        public static bool ApplyAction(
            FailuresAccessor failuresAccessor,
            FailureMessageAccessor failureMessage,
            FailureHandlingAction action)
        {
            if (action == FailureHandlingAction.Deleted)
            {
                failuresAccessor.DeleteWarning(failureMessage);
                return true;
            }

            if (action == FailureHandlingAction.Resolved)
            {
                failuresAccessor.ResolveFailure(failureMessage);
                return true;
            }

            return false;
        }
    }
}
