using System;
using NUnit.Framework;
using Synthetic.Platform.Revit;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [UnitTest]
    public class TargetIdFailureRuleTests
    {
        private static readonly Guid PurgeWarningId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid PurgeDeletedId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid UnrelatedWarningId = new Guid("33333333-3333-3333-3333-333333333333");

        [Test]
        public void Matches_ReturnsTrue_WhenFailureDefinitionIdMatchesTarget()
        {
            var rule = new TargetIdFailurePolicyRule(PurgeWarningId);
            var record = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Element deletion warning");

            Assert.IsTrue(rule.Matches(record));
        }

        [Test]
        public void Matches_ReturnsFalse_WhenFailureDefinitionIdDoesNotMatchTarget()
        {
            var rule = new TargetIdFailurePolicyRule(PurgeWarningId);
            var record = new FailureRecord(UnrelatedWarningId, FailureSeverityLevel.Warning, "Unrelated warning");

            Assert.IsFalse(rule.Matches(record));
        }

        [Test]
        public void EvaluateAction_ReturnsDeleted_WhenWarningMatchesTargetId()
        {
            var rule = new TargetIdFailurePolicyRule(PurgeWarningId);
            var record = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Element will be deleted");

            FailureHandlingAction action = rule.EvaluateAction(record);

            Assert.AreEqual(FailureHandlingAction.Deleted, action);
        }

        [Test]
        public void EvaluateAction_ReturnsResolved_WhenErrorMatchesTargetId()
        {
            var rule = new TargetIdFailurePolicyRule(PurgeWarningId);
            var record = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Error, "Purge error");

            FailureHandlingAction action = rule.EvaluateAction(record);

            Assert.AreEqual(FailureHandlingAction.Resolved, action);
        }

        [Test]
        public void FailurePipelinePolicy_ReturnsTrue_WhenRuleHandlesFailure()
        {
            var policy = new FailurePipelinePolicy();
            policy.AddRule(new TargetIdFailurePolicyRule(PurgeWarningId));

            var record = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Warning message");

            bool shouldProceed = policy.EvaluateAll(new[] { record });

            Assert.IsTrue(shouldProceed);
            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, policy.Report.Records[0].ActionTaken);
        }

        [Test]
        public void FailurePipelinePolicy_ReturnsFalse_WhenNoRulesMatchFailure()
        {
            var policy = new FailurePipelinePolicy();
            policy.AddRule(new TargetIdFailurePolicyRule(PurgeWarningId));

            var record = new FailureRecord(UnrelatedWarningId, FailureSeverityLevel.Warning, "Unmatched warning");

            bool shouldProceed = policy.EvaluateAll(new[] { record });

            Assert.IsFalse(shouldProceed);
            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, policy.Report.Records[0].ActionTaken);
        }

        [Test]
        public void FailurePipelinePolicies_ForTargetIds_HandlesPurgeFailuresAndPassesThroughUnrelated()
        {
            FailurePipelinePolicy purgePolicy = FailurePipelinePolicies.ForTargetIds(new[] { PurgeWarningId, PurgeDeletedId });

            var purgeMsg = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Elements will be deleted");
            var deleteMsg = new FailureRecord(PurgeDeletedId, FailureSeverityLevel.Warning, "Elements deleted");
            var roomMsg = new FailureRecord(UnrelatedWarningId, FailureSeverityLevel.Warning, "Room warning");

            bool shouldProceed = purgePolicy.EvaluateAll(new[] { purgeMsg, deleteMsg, roomMsg });

            Assert.IsTrue(shouldProceed);
            Assert.AreEqual(3, purgePolicy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, purgePolicy.Report.Records[0].ActionTaken);
            Assert.AreEqual(FailureHandlingAction.Deleted, purgePolicy.Report.Records[1].ActionTaken);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, purgePolicy.Report.Records[2].ActionTaken);
        }

        [Test]
        public void FailurePipelinePolicies_ObserveOnly_PassesThroughAllWarningsWithoutSuppression()
        {
            FailurePipelinePolicy observePolicy = FailurePipelinePolicies.ObserveOnly();

            Assert.IsFalse(observePolicy.AllowBlanketSuppression);

            var warning1 = new FailureRecord(UnrelatedWarningId, FailureSeverityLevel.Warning, "Room warning");
            var warning2 = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Elements warning");

            bool shouldProceed = observePolicy.EvaluateAll(new[] { warning1, warning2 });

            Assert.IsFalse(shouldProceed);
            Assert.IsFalse(observePolicy.Report.WarningTripped);
            Assert.AreEqual(2, observePolicy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, observePolicy.Report.Records[0].ActionTaken);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, observePolicy.Report.Records[1].ActionTaken);
        }

        [Test]
        public void FailurePipelinePolicies_DeleteAllWarningsRecorded_SuppressesAllWarningsAndRecordsTelemetry()
        {
            FailurePipelinePolicy policy = FailurePipelinePolicies.DeleteAllWarningsRecorded();

            Assert.IsTrue(policy.AllowBlanketSuppression);

            var warning1 = new FailureRecord(UnrelatedWarningId, FailureSeverityLevel.Warning, "Room warning");
            var warning2 = new FailureRecord(PurgeWarningId, FailureSeverityLevel.Warning, "Elements warning");

            bool shouldProceed = policy.EvaluateAll(new[] { warning1, warning2 });

            Assert.IsTrue(shouldProceed);
            Assert.IsTrue(policy.Report.WarningTripped);
            Assert.AreEqual(2, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, policy.Report.Records[0].ActionTaken);
            Assert.AreEqual(FailureHandlingAction.Deleted, policy.Report.Records[1].ActionTaken);
        }
    }
}
