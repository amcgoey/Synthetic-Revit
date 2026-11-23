using System;
using NUnit.Framework;
using Synthetic.Platform.Revit;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [UnitTest]
    public class SuppressConstraintsPresetTests
    {
        private static readonly Guid GroupConstraintsFailedId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid RoomNotEnclosedId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        [Test]
        public void FailureProcessingReport_WarningTripped_DefaultsToFalse()
        {
            var report = new FailureProcessingReport();
            Assert.IsFalse(report.WarningTripped);
        }

        [Test]
        public void FailurePipelinePolicy_TargetConstraintRule_DeletesConstraintWarningAndSetsWarningTripped()
        {
            var report = new FailureProcessingReport();
            FailurePipelinePolicy policy = FailurePipelinePolicies.ForTargetIds(new[] { GroupConstraintsFailedId }, report);

            var constraintWarning = new FailureRecord(
                GroupConstraintsFailedId,
                FailureSeverityLevel.Warning,
                "Group constraints failed warning");

            bool shouldProceed = policy.EvaluateAll(new[] { constraintWarning });

            Assert.IsTrue(shouldProceed);
            Assert.IsTrue(report.WarningTripped);
            Assert.AreEqual(1, report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, report.Records[0].ActionTaken);
        }

        [Test]
        public void FailurePipelinePolicy_TargetConstraintRule_IgnoresUnmatchedWarnings()
        {
            var report = new FailureProcessingReport();
            FailurePipelinePolicy policy = FailurePipelinePolicies.ForTargetIds(new[] { GroupConstraintsFailedId }, report);

            var roomWarning = new FailureRecord(
                RoomNotEnclosedId,
                FailureSeverityLevel.Warning,
                "Room not enclosed warning");

            bool shouldProceed = policy.EvaluateAll(new[] { roomWarning });

            Assert.IsFalse(shouldProceed);
            Assert.IsFalse(report.WarningTripped);
            Assert.AreEqual(1, report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, report.Records[0].ActionTaken);
        }
    }
}
