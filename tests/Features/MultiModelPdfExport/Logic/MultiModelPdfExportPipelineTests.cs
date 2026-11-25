using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [Test]
        public void Execute_SeparateMode_ExportsPerDocumentSubfolders_AggregatesMultipleSkippedWarnings_AndPopulatesCompletionViewModel()
        {
            var docA = new MultiModelPdfDocumentSnapshot(
                "doc-a",
                "TowerA.rvt",
                @"C:\BIM\TowerA.rvt",
                MultiModelPdfDocumentOrigin.Active,
                MultiModelPdfStorageKind.Workshared,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot(
                        "Permit Set",
                        2,
                        new[]
                        {
                            new MultiModelPdfSkippedViewInfo("A900", "UNPLACED SIGNAGE", "Placeholder sheet")
                        })
                });
            var docB = new MultiModelPdfDocumentSnapshot(
                "doc-b",
                "Retail.rvt",
                @"C:\BIM\Retail.rvt",
                MultiModelPdfDocumentOrigin.Linked,
                MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot(
                        "Permit Set",
                        3,
                        new[]
                        {
                            new MultiModelPdfSkippedViewInfo("Schedule", "Door Schedule", "Schedule — not printable"),
                            new MultiModelPdfSkippedViewInfo("FloorPlan", "Working Template", "View template")
                        })
                });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "TowerA.rvt",
                new[] { docA, docB },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var adapter = new FakeMultiModelPdfExportAdapter(snapshot);
            var pipeline = new MultiModelPdfExportPipeline(adapter);

            var jobs = new[]
            {
                CreateJob("doc-a", "TowerA.rvt", "Permit Set", "TowerA", MultiModelPdfFileMode.SeparatePerViewSheet),
                CreateJob("doc-b", "Retail.rvt", "Permit Set", "Retail", MultiModelPdfFileMode.SeparatePerViewSheet)
            };
            var request = new MultiModelPdfRunRequest(
                jobs,
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.SeparatePerViewSheet);

            MultiModelPdfRunSummary summary = pipeline.Execute(request);

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(2, summary.CompletedDocumentCount);
            Assert.AreEqual(0, summary.FailedDocumentCount);
            Assert.AreEqual(5, summary.GeneratedCount);
            Assert.AreEqual(5, summary.GeneratedFilePaths.Count);
            Assert.AreEqual(2, summary.DocumentOutcomes[0].GeneratedCount);
            Assert.AreEqual(3, summary.DocumentOutcomes[1].GeneratedCount);
            Assert.IsNotNull(summary.DocumentOutcomes[0].PrimaryFilePath);
            Assert.AreEqual(summary.DocumentOutcomes[0].PrimaryFilePath, summary.PrimaryFilePath);
            Assert.IsTrue(Directory.Exists(Path.Combine(_outputDir, "TowerA")));
            Assert.IsTrue(Directory.Exists(Path.Combine(_outputDir, "Retail")));
            Assert.IsTrue(summary.GeneratedFilePaths.All(File.Exists));

            Assert.AreEqual(2, summary.Warnings.Count);
            StringAssert.Contains("TowerA.rvt", summary.Warnings[0]);
            StringAssert.Contains("A900", summary.Warnings[0]);
            StringAssert.Contains("Retail.rvt", summary.Warnings[1]);
            StringAssert.Contains("Door Schedule", summary.Warnings[1]);

            var completionVm = ExportCompletionViewModel.ForBatch(
                summary.OutputFolder,
                summary.GeneratedCount,
                summary.PrimaryFilePath,
                outputNounPlural: "PDFs",
                primaryActionLabel: "Open First PDF",
                warningNotices: summary.CompletionNotices);

            Assert.IsTrue(completionVm.HasGeneratedOutput);
            Assert.IsTrue(completionVm.HasWarnings);
            Assert.AreEqual("Generated 5 PDFs in:", completionVm.SummaryText);
            Assert.AreEqual(2, completionVm.WarningNotices.Count);
            Assert.AreEqual(summary.PrimaryFilePath, completionVm.PrimaryFilePath);
        }

        [Test]
        public void Execute_SeparateMode_ThrowsPathTooLongWhenDocumentSubfolderExceedsSafeLimit()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            var pipeline = new MultiModelPdfExportPipeline(adapter);

            string longFolder = Path.Combine(@"C:\Exports", new string('a', 238));
            var job = new MultiModelPdfDocumentJob(
                documentKey: "doc-long",
                documentTitle: "LongModel.rvt",
                origin: MultiModelPdfDocumentOrigin.Active,
                storage: MultiModelPdfStorageKind.Local,
                printSetName: "CD Set",
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.SeparatePerViewSheet,
                outputFolder: longFolder,
                outputFileToken: "LongModel");

            var request = new MultiModelPdfRunRequest(
                new[] { job },
                longFolder,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.SeparatePerViewSheet);

            Assert.Throws<PathTooLongException>(() => pipeline.Execute(request));
        }

        [Test]
        public void Execute_MultiTabBatch_IsolatesMissingSetLockedPdfUnauthorizedAndExportFalseAcrossOpenTabs()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            adapter.OutcomesByDocumentKey["active-doc"] = MultiModelPdfDocumentOutcome.Failure(
                "active-doc",
                "Active.rvt",
                "Revit PDF export failed for 'Active.rvt' (Print Set 'CD Set').");
            adapter.OutcomesByDocumentKey["open-missing"] = MultiModelPdfDocumentOutcome.Failure(
                "open-missing",
                "MissingSet.rvt",
                "Print Set 'CD Set' was not found in 'MissingSet.rvt'.");
            adapter.ExceptionsByDocumentKey["open-locked"] = new UnauthorizedAccessException("Access denied writing target PDF.");

            var jobs = new[]
            {
                CreateJob("active-doc", "Active.rvt", "CD Set", "Active", origin: MultiModelPdfDocumentOrigin.Active),
                CreateJob("open-missing", "MissingSet.rvt", "CD Set", "MissingSet", origin: MultiModelPdfDocumentOrigin.OpenStandalone),
                CreateJob("open-locked", "Locked.rvt", "CD Set", "Locked", origin: MultiModelPdfDocumentOrigin.OpenStandalone),
                CreateJob("open-healthy-1", "SharedTitle.rvt", "CD Set", "SharedTitle", origin: MultiModelPdfDocumentOrigin.OpenStandalone),
                CreateJob("open-healthy-2", "SharedTitle.rvt", "CD Set", "SharedTitle_2", origin: MultiModelPdfDocumentOrigin.OpenStandalone)
            };

            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var request = new MultiModelPdfRunRequest(
                jobs,
                _outputDir,
                MultiModelPdfSetupOption.DefaultSetupId,
                MultiModelPdfFileMode.CombinedPerDocument);

            MultiModelPdfRunSummary summary = pipeline.Execute(request);

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(5, adapter.ExportedJobs.Count);
            Assert.AreEqual(2, summary.CompletedDocumentCount);
            Assert.AreEqual(3, summary.FailedDocumentCount);
            Assert.AreEqual(2, summary.GeneratedCount);
            Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "SharedTitle.pdf")));
            Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "SharedTitle_2.pdf")));
            Assert.AreEqual(Path.Combine(_outputDir, "SharedTitle.pdf"), summary.PrimaryFilePath);
            Assert.AreEqual(3, summary.Errors.Count);
            Assert.AreEqual(3, summary.CompletionNotices.Count);
        }

        private MultiModelPdfDocumentJob CreateJob(
            string key,
            string title,
            string printSetName,
            string token,
            MultiModelPdfFileMode fileMode = MultiModelPdfFileMode.CombinedPerDocument,
            MultiModelPdfDocumentOrigin origin = MultiModelPdfDocumentOrigin.Active)
        {
            return new MultiModelPdfDocumentJob(
                documentKey: key,
                documentTitle: title,
                origin: origin,
                storage: MultiModelPdfStorageKind.Workshared,
                printSetName: printSetName,
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: fileMode,
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
