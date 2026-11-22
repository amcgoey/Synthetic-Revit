using System;
using System.Reflection;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Synthetic.Infrastructure.FailureProcessing
{
    /// <summary>
    /// Factory providing pre-configured failure processing pipeline presets.
    /// </summary>
    public static class FailurePipelines
    {
        private static void AddTargetIdIfExists(Type failuresType, string propertyName, List<FailureDefinitionId> targetIds)
        {
            if (failuresType == null) return;
            PropertyInfo prop = failuresType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
            {
                FailureDefinitionId id = prop.GetValue(null) as FailureDefinitionId;
                if (id != null)
                {
                    targetIds.Add(id);
                }
            }
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for purge and element deletion operations.
        /// Targets BuiltInFailures.EditingFailures failure IDs for warning deletion.
        /// </summary>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor Purge()
        {
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor();
            List<FailureDefinitionId> targetIds = new List<FailureDefinitionId>();

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
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints()
        {
            return SuppressConstraints(new FailureProcessingReport());
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for group constraint warning suppression with a diagnostic report.
        /// </summary>
        /// <param name="report">The failure processing report to record tripped warnings.</param>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints(FailureProcessingReport report)
        {
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor(report);
            List<FailureDefinitionId> targetIds = new List<FailureDefinitionId>();

            Type builtInFailures = typeof(BuiltInFailures);
            Type groupFailuresType = builtInFailures.GetNestedType("GroupFailures");
            Type constraintFailuresType = builtInFailures.GetNestedType("ConstraintFailures");

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
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor SuppressConstraints(out FailureProcessingReport report)
        {
            report = new FailureProcessingReport();
            return SuppressConstraints(report);
        }

        /// <summary>
        /// Creates a failure preprocessor preset configured for blanket warning suppression.
        /// </summary>
        /// <returns>A configured CompositeFailuresPreprocessor instance.</returns>
        public static CompositeFailuresPreprocessor DeleteWarnings()
        {
            return new CompositeFailuresPreprocessor
            {
                AllowBlanketSuppression = true
            };
        }
    }
}
