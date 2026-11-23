using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;

namespace Synthetic.Platform.Revit
{
    /// <summary>
    /// Factory providing pre-configured Revit failure processing pipeline presets.
    /// </summary>
    public static class FailurePipelines
    {
        private static void AddTargetIdIfExists(Type? failuresType, string propertyName, List<FailureDefinitionId> targetIds)
        {
            if (failuresType == null) return;
            PropertyInfo? prop = failuresType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
            {
                if (prop.GetValue(null) is FailureDefinitionId id)
                {
                    targetIds.Add(id);
                }
            }
        }

        /// <summary>
        /// Creates a non-mutating diagnostic failure preprocessor that observes and records all failures
        /// without deleting or resolving any.
        /// </summary>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor ObserveOnly()
        {
            return new CompositeFailuresPreprocessor(FailurePipelinePolicies.ObserveOnly());
        }

        /// <summary>
        /// Creates a non-mutating diagnostic failure preprocessor that observes and records all failures
        /// into the specified <see cref="FailureProcessingReport"/>.
        /// </summary>
        /// <param name="report">The diagnostic report to populate.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor ObserveOnly(FailureProcessingReport report)
        {
            return new CompositeFailuresPreprocessor(FailurePipelinePolicies.ObserveOnly(report));
        }

        /// <summary>
        /// Creates a non-mutating diagnostic failure preprocessor and exposes its diagnostic report.
        /// </summary>
        /// <param name="report">Output failure processing report.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor ObserveOnly(out FailureProcessingReport report)
        {
            report = new FailureProcessingReport();
            return ObserveOnly(report);
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for purge and element deletion operations.
        /// Targets <c>BuiltInFailures.EditingFailures</c> failure IDs for warning deletion.
        /// </summary>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor Purge()
        {
            var preprocessor = new CompositeFailuresPreprocessor();
            var targetIds = new List<FailureDefinitionId>();

            if (BuiltInFailures.EditingFailures.ElementsWillBeDeleted != null)
            {
                targetIds.Add(BuiltInFailures.EditingFailures.ElementsWillBeDeleted);
            }

            if (BuiltInFailures.EditingFailures.ElementsDeleted != null)
            {
                targetIds.Add(BuiltInFailures.EditingFailures.ElementsDeleted);
            }

            if (targetIds.Count > 0)
            {
                preprocessor.AddRule(new TargetIdFailureRule(targetIds));
            }

            return preprocessor;
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for group constraint warning suppression.
        /// Targets group constraint failure definition IDs.
        /// </summary>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints()
        {
            return SuppressConstraints(new FailureProcessingReport());
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for group constraint warning suppression with a diagnostic report.
        /// </summary>
        /// <param name="report">The failure processing report to record tripped warnings.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints(FailureProcessingReport report)
        {
            var preprocessor = new CompositeFailuresPreprocessor(report);
            var targetIds = new List<FailureDefinitionId>();

            Type builtInFailures = typeof(BuiltInFailures);
            Type? groupFailuresType = builtInFailures.GetNestedType("GroupFailures");
            Type? constraintFailuresType = builtInFailures.GetNestedType("ConstraintFailures");

            AddTargetIdIfExists(groupFailuresType, "GroupConstraintsFailed", targetIds);
            AddTargetIdIfExists(groupFailuresType, "CannotRemoveGroupMemberConstraintWarn", targetIds);
            AddTargetIdIfExists(groupFailuresType, "CannotRemoveGroupMemberConstraint", targetIds);
            AddTargetIdIfExists(groupFailuresType, "RemoveGroupSketchConstraintParent", targetIds);
            AddTargetIdIfExists(constraintFailuresType, "UndeletedConstraintsInGroup", targetIds);

            if (targetIds.Count > 0)
            {
                preprocessor.AddRule(new TargetIdFailureRule(targetIds));
            }

            return preprocessor;
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for group constraint warning suppression with an out diagnostic report.
        /// </summary>
        /// <param name="report">Output failure processing report.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints(out FailureProcessingReport report)
        {
            report = new FailureProcessingReport();
            return SuppressConstraints(report);
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for blanket warning suppression with full telemetry recording.
        /// </summary>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor DeleteAllWarningsRecorded()
        {
            return new CompositeFailuresPreprocessor(FailurePipelinePolicies.DeleteAllWarningsRecorded());
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for blanket warning suppression recording into the specified report.
        /// </summary>
        /// <param name="report">The failure processing report to populate.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor DeleteAllWarningsRecorded(FailureProcessingReport report)
        {
            return new CompositeFailuresPreprocessor(FailurePipelinePolicies.DeleteAllWarningsRecorded(report));
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for blanket warning suppression and exposes its diagnostic report.
        /// </summary>
        /// <param name="report">Output failure processing report.</param>
        /// <returns>A configured <see cref="CompositeFailuresPreprocessor"/> instance.</returns>
        public static CompositeFailuresPreprocessor DeleteAllWarningsRecorded(out FailureProcessingReport report)
        {
            report = new FailureProcessingReport();
            return DeleteAllWarningsRecorded(report);
        }
    }
}
