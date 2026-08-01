using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.FailureProcessing;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.FailureProcessing
{
    [TestFixture]
    public class FailurePipelineBuilderTests
    {
        [Test]
        public void Build_WithDefaultSettings_ShouldEnforceAntiBlanketSuppressionSafetyPolicy()
        {
            // Arrange & Act
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder().Build();

            // Assert
            Assert.IsFalse(preprocessor.AllowBlanketSuppression, "AllowBlanketSuppression must default to false");
            Assert.AreEqual(0, preprocessor.Rules.Count);
        }

        [Test]
        public void Build_WithOnFailureSingleId_ShouldAddTargetIdFailureRule()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;

            // Act
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnFailure(targetId)
                .Build();

            // Assert
            Assert.AreEqual(1, preprocessor.Rules.Count);
            Assert.IsInstanceOf<TargetIdFailureRule>(preprocessor.Rules[0]);
        }

        [Test]
        public void Build_WithOnFailureMultipleIds_ShouldAddTargetIdFailureRule()
        {
            // Arrange
            List<FailureDefinitionId> targetIds = new List<FailureDefinitionId>
            {
                BuiltInFailures.EditingFailures.ElementsWillBeDeleted,
                BuiltInFailures.EditingFailures.ElementsDeleted
            };

            // Act
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnFailure(targetIds)
                .Build();

            // Assert
            Assert.AreEqual(1, preprocessor.Rules.Count);
            Assert.IsInstanceOf<TargetIdFailureRule>(preprocessor.Rules[0]);
        }

        [Test]
        public void Build_WithOnSeverity_ShouldAddSeverityFailureRule()
        {
            // Act
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnSeverity(FailureSeverity.Warning)
                .Build();

            // Assert
            Assert.AreEqual(1, preprocessor.Rules.Count);
            Assert.IsInstanceOf<SeverityFailureRule>(preprocessor.Rules[0]);
            var rule = preprocessor.Rules[0] as SeverityFailureRule;
            Assert.AreEqual(FailureSeverity.Warning, rule.TargetSeverity);
        }

        [Test]
        public void Build_WithAllowBlanketSuppressionTrue_ShouldEnableBlanketSuppression()
        {
            // Act
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .AllowBlanketSuppression(true)
                .Build();

            // Assert
            Assert.IsTrue(preprocessor.AllowBlanketSuppression);
        }

        [Test]
        public void PreprocessFailures_WithDefaultAllowBlanketSuppressionFalse_ShouldReturnContinueForUnmatchedWarning()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            FailureDefinitionId unmatchedId = BuiltInFailures.RoomFailures.RoomNotEnclosed;

            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnFailure(targetId)
                .Build(); // AllowBlanketSuppression defaults to false

            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(unmatchedId, FailureSeverity.Warning, "Unmatched room warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.Continue, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(0, deleted.Count, "Unmatched warning should not be deleted when AllowBlanketSuppression is false");
            Assert.AreEqual(1, preprocessor.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, preprocessor.Report.Records[0].ActionTaken);
        }

        [Test]
        public void PreprocessFailures_WithAllowBlanketSuppressionTrue_ShouldDeleteUnmatchedWarningAndProceedWithCommit()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            FailureDefinitionId unmatchedId = BuiltInFailures.RoomFailures.RoomNotEnclosed;

            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnFailure(targetId)
                .AllowBlanketSuppression(true)
                .Build();

            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(unmatchedId, FailureSeverity.Warning, "Unmatched warning with blanket suppression enabled");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.ProceedWithCommit, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(1, deleted.Count, "Unmatched warning should be blanket deleted when AllowBlanketSuppression is true");
            Assert.AreEqual(message, deleted[0]);
            Assert.AreEqual(1, preprocessor.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, preprocessor.Report.Records[0].ActionTaken);
        }

        [Test]
        public void PreprocessFailures_WithOnSeverityWarning_ShouldHandleWarningsMatchingSeverity()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnSeverity(FailureSeverity.Warning)
                .Build();

            FailureMessageAccessor warningMsg = MockFailureFactory.CreateFailureMessage(
                BuiltInFailures.RoomFailures.RoomNotEnclosed, FailureSeverity.Warning, "Room warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { warningMsg });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.ProceedWithCommit, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(1, deleted.Count);
            Assert.AreEqual(warningMsg, deleted[0]);
            Assert.AreEqual(1, preprocessor.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, preprocessor.Report.Records[0].ActionTaken);
        }

        [Test]
        public void FailureRecord_ConstructorAndProperties_ShouldPopulateCorrectly()
        {
            // Arrange
            FailureDefinitionId failureId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(failureId, FailureSeverity.Warning, "Test warning description");

            // Act
            FailureRecord record = new FailureRecord(message, FailureHandlingAction.Deleted, "ContextTest");

            // Assert
            Assert.IsNotNull(record);
            Assert.AreEqual(failureId, record.FailureDefinitionId);
            Assert.AreEqual(FailureSeverity.Warning, record.Severity);
            Assert.AreEqual("Test warning description", record.DescriptionText);
            Assert.AreEqual(FailureHandlingAction.Deleted, record.ActionTaken);
            Assert.AreEqual("ContextTest", record.ContextTag);
            Assert.IsNotNull(record.FailingElementIds);
            Assert.IsTrue(record.Timestamp <= DateTime.Now);
        }

        [Test]
        public void FailureProcessingReport_AddRecord_ShouldAccumulateRecordsAndSetWarningTripped()
        {
            // Arrange
            FailureProcessingReport report = new FailureProcessingReport();
            FailureRecord rec1 = new FailureRecord { ActionTaken = FailureHandlingAction.PassedThrough, DescriptionText = "Pass" };
            FailureRecord rec2 = new FailureRecord { ActionTaken = FailureHandlingAction.Deleted, DescriptionText = "Del" };
            FailureRecord rec3 = new FailureRecord { ActionTaken = FailureHandlingAction.Resolved, DescriptionText = "Res" };
            FailureRecord rec4 = new FailureRecord { ActionTaken = FailureHandlingAction.Continued, DescriptionText = "Cont" };

            // Act
            report.AddRecord(rec1);
            Assert.IsFalse(report.WarningTripped);
            Assert.AreEqual(1, report.Records.Count);

            report.AddRecord(rec2);
            report.AddRecord(rec3);
            report.AddRecord(rec4);

            // Assert
            Assert.IsTrue(report.WarningTripped);
            Assert.AreEqual(4, report.Records.Count);
            Assert.AreEqual(rec1, report.Records[0]);
            Assert.AreEqual(rec2, report.Records[1]);
            Assert.AreEqual(rec3, report.Records[2]);
            Assert.AreEqual(rec4, report.Records[3]);
        }

        [Test]
        public void PreprocessFailures_WithContextTag_ShouldPropagateContextTagToFailureRecord()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = new FailurePipelineBuilder()
                .OnSeverity(FailureSeverity.Warning)
                .Build();

            preprocessor.SetContext("Batch family processing: Column.rfa");

            FailureMessageAccessor warningMsg = MockFailureFactory.CreateFailureMessage(
                BuiltInFailures.RoomFailures.RoomNotEnclosed, FailureSeverity.Warning, "Room warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { warningMsg });

            // Act
            preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(1, preprocessor.Report.Records.Count);
            Assert.AreEqual("Batch family processing: Column.rfa", preprocessor.Report.Records[0].ContextTag);
        }
    }
}
