using System;
using System.Collections.Generic;
using NUnit.Framework;
using Synthetic.Platform.Revit;

namespace SyntheticTests.Platform.Revit
{
    [TestFixture]
    [UnitTest]
    public class FailurePipelineBuilderTests
    {
        [Test]
        public void Build_WithDefaultSettings_EnforcesAntiBlanketSuppressionSafetyPolicy()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder().Build();

            Assert.IsFalse(policy.AllowBlanketSuppression, "AllowBlanketSuppression must default to false.");
            Assert.AreEqual(0, policy.Rules.Count);
        }

        [Test]
        public void Build_WithOnFailureSingleId_AddsTargetIdFailurePolicyRule()
        {
            Guid targetId = new Guid("11111111-1111-1111-1111-111111111111");

            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnFailure(targetId)
                .Build();

            Assert.AreEqual(1, policy.Rules.Count);
            Assert.IsInstanceOf<TargetIdFailurePolicyRule>(policy.Rules[0]);
        }

        [Test]
        public void Build_WithOnFailureMultipleIds_AddsTargetIdFailurePolicyRule()
        {
            var targetIds = new List<Guid>
            {
                new Guid("11111111-1111-1111-1111-111111111111"),
                new Guid("22222222-2222-2222-2222-222222222222")
            };

            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnFailure(targetIds)
                .Build();

            Assert.AreEqual(1, policy.Rules.Count);
            Assert.IsInstanceOf<TargetIdFailurePolicyRule>(policy.Rules[0]);
            var rule = (TargetIdFailurePolicyRule)policy.Rules[0];
            Assert.AreEqual(2, rule.TargetIds.Count);
        }

        [Test]
        public void Build_WithOnSeverity_AddsSeverityFailurePolicyRule()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnSeverity(FailureSeverityLevel.Warning)
                .Build();

            Assert.AreEqual(1, policy.Rules.Count);
            Assert.IsInstanceOf<SeverityFailurePolicyRule>(policy.Rules[0]);
            var rule = (SeverityFailurePolicyRule)policy.Rules[0];
            Assert.AreEqual(FailureSeverityLevel.Warning, rule.TargetSeverity);
        }

        [Test]
        public void Build_WithAllowBlanketSuppressionTrue_EnablesBlanketSuppression()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .AllowBlanketSuppression(true)
                .Build();

            Assert.IsTrue(policy.AllowBlanketSuppression);
        }

        [Test]
        public void EvaluateAll_WithDefaultAllowBlanketSuppressionFalse_PassesThroughUnmatchedWarning()
        {
            Guid targetId = new Guid("11111111-1111-1111-1111-111111111111");
            Guid unmatchedId = new Guid("33333333-3333-3333-3333-333333333333");

            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnFailure(targetId)
                .Build();

            var warning = new FailureRecord(unmatchedId, FailureSeverityLevel.Warning, "Unmatched room warning");

            bool shouldProceed = policy.EvaluateAll(new[] { warning });

            Assert.IsFalse(shouldProceed);
            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.PassedThrough, policy.Report.Records[0].ActionTaken);
            Assert.IsFalse(policy.Report.WarningTripped);
        }

        [Test]
        public void EvaluateAll_WithAllowBlanketSuppressionTrue_DeletesUnmatchedWarningAndProceeds()
        {
            Guid targetId = new Guid("11111111-1111-1111-1111-111111111111");
            Guid unmatchedId = new Guid("33333333-3333-3333-3333-333333333333");

            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnFailure(targetId)
                .AllowBlanketSuppression(true)
                .Build();

            var warning = new FailureRecord(unmatchedId, FailureSeverityLevel.Warning, "Unmatched warning with blanket suppression");

            bool shouldProceed = policy.EvaluateAll(new[] { warning });

            Assert.IsTrue(shouldProceed);
            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, policy.Report.Records[0].ActionTaken);
            Assert.IsTrue(policy.Report.WarningTripped);
        }

        [Test]
        public void EvaluateAll_WithOnSeverityWarning_DeletesWarningsMatchingSeverity()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnSeverity(FailureSeverityLevel.Warning)
                .Build();

            var warning = new FailureRecord(
                new Guid("33333333-3333-3333-3333-333333333333"),
                FailureSeverityLevel.Warning,
                "Room warning");

            bool shouldProceed = policy.EvaluateAll(new[] { warning });

            Assert.IsTrue(shouldProceed);
            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual(FailureHandlingAction.Deleted, policy.Report.Records[0].ActionTaken);
        }

        [Test]
        public void FailureRecord_ConstructorAndProperties_PopulatePrimitiveFieldsCorrectly()
        {
            Guid failureId = new Guid("44444444-4444-4444-4444-444444444444");
            var failingIds = new long[] { 101L, 202L };

            var record = new FailureRecord(
                failureId,
                FailureSeverityLevel.Warning,
                "Test warning description",
                failingIds,
                FailureHandlingAction.Deleted,
                "ContextTest");

            Assert.AreEqual(failureId, record.FailureDefinitionId);
            Assert.AreEqual(FailureSeverityLevel.Warning, record.Severity);
            Assert.AreEqual("Test warning description", record.DescriptionText);
            Assert.AreEqual(2, record.FailingElementIds.Count);
            Assert.AreEqual(101L, record.FailingElementIds[0]);
            Assert.AreEqual(202L, record.FailingElementIds[1]);
            Assert.AreEqual(FailureHandlingAction.Deleted, record.ActionTaken);
            Assert.AreEqual("ContextTest", record.ContextTag);
            Assert.IsTrue(record.Timestamp <= DateTime.Now);
        }

        [Test]
        public void FailureProcessingReport_AddRecord_AccumulatesRecordsAndSetsWarningTripped()
        {
            var report = new FailureProcessingReport();
            var rec1 = new FailureRecord { ActionTaken = FailureHandlingAction.PassedThrough, DescriptionText = "Pass" };
            var rec2 = new FailureRecord { ActionTaken = FailureHandlingAction.Deleted, DescriptionText = "Del" };
            var rec3 = new FailureRecord { ActionTaken = FailureHandlingAction.Resolved, DescriptionText = "Res" };
            var rec4 = new FailureRecord { ActionTaken = FailureHandlingAction.Continued, DescriptionText = "Cont" };

            report.AddRecord(rec1);
            Assert.IsFalse(report.WarningTripped);
            Assert.AreEqual(1, report.Records.Count);

            report.AddRecord(rec2);
            report.AddRecord(rec3);
            report.AddRecord(rec4);

            Assert.IsTrue(report.WarningTripped);
            Assert.AreEqual(4, report.Records.Count);
            Assert.AreSame(rec1, report.Records[0]);
            Assert.AreSame(rec2, report.Records[1]);
            Assert.AreSame(rec3, report.Records[2]);
            Assert.AreSame(rec4, report.Records[3]);
        }

        [Test]
        public void SetContext_NullOrNonNull_SetsContextTagProperty()
        {
            var policy = new FailurePipelinePolicy();

            policy.SetContext("Scope: Batch Purge");
            Assert.AreEqual("Scope: Batch Purge", policy.ContextTag);

            policy.SetContext(null);
            Assert.AreEqual(string.Empty, policy.ContextTag);
        }

        [Test]
        public void EvaluateAll_WithContextTag_PropagatesContextTagToFailureRecord()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnSeverity(FailureSeverityLevel.Warning)
                .Build();

            policy.SetContext("Batch family processing: Column.rfa");

            var warning = new FailureRecord(
                new Guid("33333333-3333-3333-3333-333333333333"),
                FailureSeverityLevel.Warning,
                "Room warning");

            policy.EvaluateAll(new[] { warning });

            Assert.AreEqual(1, policy.Report.Records.Count);
            Assert.AreEqual("Batch family processing: Column.rfa", policy.Report.Records[0].ContextTag);
        }

        [Test]
        public void SetContext_AcrossBatchLoopIterations_PropagatesContextTagToFailureRecords()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnSeverity(FailureSeverityLevel.Warning)
                .Build();

            string[] items = new[] { "Family: Door.rfa", "Family: Window.rfa", "Family: Desk.rfa" };

            for (int i = 0; i < items.Length; i++)
            {
                string tag = items[i];
                policy.SetContext(tag);

                var warning = new FailureRecord(
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    FailureSeverityLevel.Warning,
                    $"Warning for {tag}");

                policy.EvaluateAll(new[] { warning });
            }

            Assert.AreEqual(3, policy.Report.Records.Count);
            Assert.AreEqual("Family: Door.rfa", policy.Report.Records[0].ContextTag);
            Assert.AreEqual("Family: Window.rfa", policy.Report.Records[1].ContextTag);
            Assert.AreEqual("Family: Desk.rfa", policy.Report.Records[2].ContextTag);
        }

        [Test]
        public void SetContext_MultipleFailuresInBatchIteration_TagsAllGeneratedRecords()
        {
            FailurePipelinePolicy policy = new FailurePipelinePolicyBuilder()
                .OnSeverity(FailureSeverityLevel.Warning)
                .Build();

            policy.SetContext("Purging Element Batch 42");

            var msg1 = new FailureRecord(
                new Guid("33333333-3333-3333-3333-333333333333"),
                FailureSeverityLevel.Warning,
                "Warning 1");
            var msg2 = new FailureRecord(
                new Guid("11111111-1111-1111-1111-111111111111"),
                FailureSeverityLevel.Warning,
                "Warning 2");

            policy.EvaluateAll(new[] { msg1, msg2 });

            Assert.AreEqual(2, policy.Report.Records.Count);
            Assert.AreEqual("Purging Element Batch 42", policy.Report.Records[0].ContextTag);
            Assert.AreEqual("Purging Element Batch 42", policy.Report.Records[1].ContextTag);
        }
    }
}
