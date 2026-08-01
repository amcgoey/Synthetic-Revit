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
        }
    }
}
