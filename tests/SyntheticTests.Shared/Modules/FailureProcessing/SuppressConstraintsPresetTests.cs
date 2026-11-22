using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.FailureProcessing;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.FailureProcessing
{
    [TestFixture]
    public class SuppressConstraintsPresetTests
    {
        private FailureDefinitionId GetConstraintFailureId()
        {
            Type builtInFailures = typeof(BuiltInFailures);
            Type groupFailuresType = builtInFailures.GetNestedType("GroupFailures");

            if (groupFailuresType != null)
            {
                PropertyInfo groupConstraintsFailedProp = groupFailuresType.GetProperty("GroupConstraintsFailed", BindingFlags.Public | BindingFlags.Static);
                if (groupConstraintsFailedProp != null)
                {
                    FailureDefinitionId id = groupConstraintsFailedProp.GetValue(null) as FailureDefinitionId;
                    if (id != null) return id;
                }

                PropertyInfo cannotRemoveProp = groupFailuresType.GetProperty("CannotRemoveGroupMemberConstraintWarn", BindingFlags.Public | BindingFlags.Static);
                if (cannotRemoveProp != null)
                {
                    FailureDefinitionId id = cannotRemoveProp.GetValue(null) as FailureDefinitionId;
                    if (id != null) return id;
                }
            }

            return BuiltInFailures.RoomFailures.RoomNotEnclosed;
        }

        [Test]
        public void FailureProcessingReport_WarningTripped_DefaultsToFalse()
        {
            FailureProcessingReport report = new FailureProcessingReport();
            Assert.IsFalse(report.WarningTripped);
        }

        [Test]
        public void FailurePipelines_SuppressConstraints_DeletesGroupConstraintsWarningAndSetsWarningTripped()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = FailurePipelines.SuppressConstraints(out FailureProcessingReport report);
            FailureDefinitionId constraintId = GetConstraintFailureId();

            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(
                constraintId, FailureSeverity.Warning, "Group constraints failed warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.ProceedWithCommit, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(1, deleted.Count);
            Assert.AreEqual(message, deleted[0]);
            Assert.IsTrue(report.WarningTripped);
        }

        [Test]
        public void FailurePipelines_SuppressConstraints_IgnoresUnmatchedWarnings()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = FailurePipelines.SuppressConstraints(out FailureProcessingReport report);
            FailureDefinitionId roomWarningId = BuiltInFailures.RoomFailures.RoomNotEnclosed;

            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(
                roomWarningId, FailureSeverity.Warning, "Room not enclosed warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.Continue, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(0, deleted.Count);
            Assert.IsFalse(report.WarningTripped);
        }
    }
}
