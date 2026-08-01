using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Infrastructure.FailureProcessing;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.FailureProcessing
{
    [TestFixture]
    public class TargetIdFailureRuleTests
    {
        [Test]
        public void Evaluates_ShouldReturnTrue_WhenFailureDefinitionIdMatchesTarget()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            TargetIdFailureRule rule = new TargetIdFailureRule(targetId);
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(targetId, FailureSeverity.Warning, "Element deletion warning");

            // Act
            bool result = rule.Evaluates(message);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void Evaluates_ShouldReturnFalse_WhenFailureDefinitionIdDoesNotMatchTarget()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            FailureDefinitionId unrelatedId = BuiltInFailures.RoomFailures.RoomNotEnclosed;
            TargetIdFailureRule rule = new TargetIdFailureRule(targetId);
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(unrelatedId, FailureSeverity.Warning, "Unrelated warning");

            // Act
            bool result = rule.Evaluates(message);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void Execute_ShouldDeleteWarning_WhenWarningMatchesTargetId()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            TargetIdFailureRule rule = new TargetIdFailureRule(targetId);
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(targetId, FailureSeverity.Warning, "Element will be deleted");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            bool handled = rule.Execute(accessor, message);

            // Assert
            Assert.IsTrue(handled);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(1, deleted.Count);
            Assert.AreEqual(message, deleted[0]);
        }

        [Test]
        public void Execute_ShouldResolveFailure_WhenErrorMatchesTargetId()
        {
            // Arrange
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            TargetIdFailureRule rule = new TargetIdFailureRule(targetId);
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(targetId, FailureSeverity.Error, "Purge error");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            bool handled = rule.Execute(accessor, message);

            // Assert
            Assert.IsTrue(handled);
            var resolved = MockFailureFactory.GetResolvedFailures(accessor);
            Assert.AreEqual(1, resolved.Count);
            Assert.AreEqual(message, resolved[0]);
        }

        [Test]
        public void CompositeFailuresPreprocessor_ShouldReturnProceedWithCommit_WhenRuleHandlesFailure()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor();
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            preprocessor.AddRule(new TargetIdFailureRule(targetId));

            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(targetId, FailureSeverity.Warning, "Warning message");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.ProceedWithCommit, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(1, deleted.Count);
        }

        [Test]
        public void CompositeFailuresPreprocessor_ShouldReturnContinue_WhenNoRulesMatchFailure()
        {
            // Arrange
            CompositeFailuresPreprocessor preprocessor = new CompositeFailuresPreprocessor();
            FailureDefinitionId targetId = BuiltInFailures.EditingFailures.ElementsWillBeDeleted;
            preprocessor.AddRule(new TargetIdFailureRule(targetId));

            FailureDefinitionId unmatchedId = BuiltInFailures.RoomFailures.RoomNotEnclosed;
            FailureMessageAccessor message = MockFailureFactory.CreateFailureMessage(unmatchedId, FailureSeverity.Warning, "Unmatched warning");
            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { message });

            // Act
            FailureProcessingResult result = preprocessor.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.Continue, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(0, deleted.Count);
        }

        [Test]
        public void FailurePipelines_Purge_ShouldHandlePurgeFailures()
        {
            // Arrange
            CompositeFailuresPreprocessor purgePipeline = FailurePipelines.Purge();

            FailureMessageAccessor purgeMsg = MockFailureFactory.CreateFailureMessage(
                BuiltInFailures.EditingFailures.ElementsWillBeDeleted, FailureSeverity.Warning, "Elements will be deleted");
            FailureMessageAccessor deleteMsg = MockFailureFactory.CreateFailureMessage(
                BuiltInFailures.EditingFailures.ElementsDeleted, FailureSeverity.Warning, "Elements deleted");
            FailureMessageAccessor roomMsg = MockFailureFactory.CreateFailureMessage(
                BuiltInFailures.RoomFailures.RoomNotEnclosed, FailureSeverity.Warning, "Room warning");

            FailuresAccessor accessor = MockFailureFactory.CreateFailuresAccessor(new[] { purgeMsg, deleteMsg, roomMsg });

            // Act
            FailureProcessingResult result = purgePipeline.PreprocessFailures(accessor);

            // Assert
            Assert.AreEqual(FailureProcessingResult.ProceedWithCommit, result);
            var deleted = MockFailureFactory.GetDeletedWarnings(accessor);
            Assert.AreEqual(2, deleted.Count);
            CollectionAssert.Contains(deleted, purgeMsg);
            CollectionAssert.Contains(deleted, deleteMsg);
        }
    }
}
