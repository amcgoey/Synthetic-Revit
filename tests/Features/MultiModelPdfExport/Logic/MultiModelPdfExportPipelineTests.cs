using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Platform.UI;
using SyntheticTests;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    [TestFixture]
    [UnitTest]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class MultiModelPdfExportPipelineTests
    {
        private string _outputDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), "MultiModelPdfPipelineTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_outputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_outputDir))
            {
                Directory.Delete(_outputDir, true);
            }
        }

        [Test]
        public void Execute_SingleDocumentJob_ExportsPdfAndReportsProgress()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var progressUpdates = new List<ProgressState>();

            var job = CreateJob("active-host", "HostModel.rvt", "CD Set", "HostModel");
            var request = new MultiModelPdfRunRequest(
                new[] { job },
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request, new ImmediateProgress(progressUpdates));

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(1, summary.CompletedDocumentCount);
            Assert.AreEqual(0, summary.FailedDocumentCount);
            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(Path.Combine(_outputDir, "HostModel.pdf"), summary.PrimaryFilePath);
            Assert.IsTrue(File.Exists(summary.PrimaryFilePath!));
            Assert.IsEmpty(summary.Warnings);
            Assert.IsEmpty(summary.Errors);
            Assert.IsNotEmpty(progressUpdates);
        }

        [Test]
        public void Execute_IsolatesFailedOutcomeAndUnexpectedExceptionAcrossJobs()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            adapter.OutcomesByDocumentKey["doc-1"] = MultiModelPdfDocumentOutcome.Failure(
                "doc-1",
                "Broken.rvt",
                "Print Set 'CD Set' was missing.");
            adapter.ExceptionsByDocumentKey["doc-2"] = new IOException("Target PDF is locked by another process.");

            var jobs = new[]
            {
                CreateJob("doc-1", "Broken.rvt", "CD Set", "Broken"),
                CreateJob("doc-2", "Locked.rvt", "CD Set", "Locked"),
                CreateJob("doc-3", "Healthy.rvt", "CD Set", "Healthy")
            };

            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var request = new MultiModelPdfRunRequest(
                jobs,
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request);

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(3, adapter.ExportedJobs.Count, "Pipeline must continue remaining jobs after failures.");
            Assert.AreEqual(1, summary.CompletedDocumentCount);
            Assert.AreEqual(2, summary.FailedDocumentCount);
            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(Path.Combine(_outputDir, "Healthy.pdf"), summary.PrimaryFilePath);
            Assert.AreEqual(2, summary.Errors.Count);
            StringAssert.Contains("missing", summary.Errors[0]);
            StringAssert.Contains("locked", summary.Errors[1]);
            Assert.AreEqual(2, summary.CompletionNotices.Count);
        }

        [Test]
        public void Execute_CancelledBeforeFirstDocument_ReturnsZeroCompletedAndWasCancelled()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            var pipeline = new MultiModelPdfExportPipeline(adapter);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var request = new MultiModelPdfRunRequest(
                new[] { CreateJob("doc-1", "Host.rvt", "CD Set", "Host") },
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request, cancellationToken: cts.Token);

            Assert.IsTrue(summary.WasCancelled);
            Assert.AreEqual(0, summary.CompletedDocumentCount);
            Assert.AreEqual(0, summary.GeneratedCount);
            Assert.IsNull(summary.PrimaryFilePath);
            Assert.IsEmpty(adapter.ExportedJobs);
        }

        [Test]
        public void Execute_CancelledBetweenDocuments_PreservesPartialOutputsAndAddsCancellationNotice()
        {
            using var cts = new CancellationTokenSource();
            var adapter = new FakeMultiModelPdfExportAdapter
            {
                CancelAfterExportCount = 1,
                CancelSourceToTrigger = cts
            };

            var jobs = new[]
            {
                CreateJob("doc-1", "First.rvt", "CD Set", "First"),
                CreateJob("doc-2", "Second.rvt", "CD Set", "Second")
            };

            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var request = new MultiModelPdfRunRequest(
                jobs,
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request, cancellationToken: cts.Token);

            Assert.IsTrue(summary.WasCancelled);
            Assert.AreEqual(1, adapter.ExportedJobs.Count);
            Assert.AreEqual(1, summary.CompletedDocumentCount);
            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(Path.Combine(_outputDir, "First.pdf"), summary.PrimaryFilePath);
            Assert.IsNotEmpty(summary.Warnings);
            StringAssert.Contains("cancelled", summary.Warnings[0].ToLowerInvariant());
        }

        [Test]
        public void Execute_AggregatesSkippedAndWarningNoticesFromOutcomes()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            string expectedPdf = Path.Combine(_outputDir, "HostModel.pdf");
            adapter.OutcomesByDocumentKey["active-host"] = MultiModelPdfDocumentOutcome.Success(
                "active-host",
                "HostModel.rvt",
                new[] { expectedPdf },
                new[] { "HostModel.rvt (Permit Set): skipped 1 placeholder sheet." });

            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var request = new MultiModelPdfRunRequest(
                new[] { CreateJob("active-host", "HostModel.rvt", "Permit Set", "HostModel") },
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request);

            Assert.AreEqual(1, summary.Warnings.Count);
            StringAssert.Contains("skipped 1 placeholder sheet", summary.Warnings[0]);
            Assert.AreEqual(1, summary.CompletionNotices.Count);
        }

        private MultiModelPdfDocumentJob CreateJob(
            string key,
            string title,
            string printSetName,
            string token)
        {
            return new MultiModelPdfDocumentJob(
                documentKey: key,
                documentTitle: title,
                origin: MultiModelPdfDocumentOrigin.Active,
                storage: MultiModelPdfStorageKind.Workshared,
                printSetName: printSetName,
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                outputFolder: _outputDir,
                outputFileToken: token);
        }

        private sealed class ImmediateProgress : IProgress<ProgressState>
        {
            private readonly IList<ProgressState> _sink;

            public ImmediateProgress(IList<ProgressState> sink) => _sink = sink;

            public void Report(ProgressState value) => _sink.Add(value);
        }
    }
}
