using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Shared.UI;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowStudyPipelineTests
    {
        private string _outputDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), "ShadowPipelineTests_" + Guid.NewGuid().ToString("N"));
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
        public void Execute_SingleViewSingleDate_GeneratesCompositeAndCleansTempDirectory()
        {
            var adapter = new FakeShadowStudyAdapter();
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;
            view.Name = "Level 1: Plan";

            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site);

            var pipeline = new ShadowStudyPipeline(adapter);
            var progressUpdates = new List<ProgressState>();
            var summary = pipeline.Execute(
                new ShadowStudyRunRequest
                {
                    Views = new[] { view },
                    DateSchedules = new[] { schedule },
                    OutputFolder = _outputDir,
                    PixelWidth = 32
                },
                new ImmediateProgress(progressUpdates));

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(1, summary.GeneratedCount);
            string expectedPath = Path.Combine(_outputDir, "Level 1_ Plan - 260621 - Composite.png");
            Assert.AreEqual(expectedPath, summary.PrimaryCompositePath);
            Assert.IsTrue(File.Exists(expectedPath));
            Assert.IsNotEmpty(progressUpdates);

            // Temp directory created for adapter must have been deleted
            Assert.AreEqual(1, adapter.ExportRequests.Count);
            Assert.IsFalse(Directory.Exists(adapter.ExportRequests[0].TempRootDirectory));
        }

        [Test]
        public void Execute_SkipsZeroFrameDatesAndRecordsWarning()
        {
            var adapter = new FakeShadowStudyAdapter();
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;

            var validSchedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site);
            var emptySchedule = new DateSolarSchedule
            {
                DateEntry = new ShadowStudyDateEntry { Date = new DateTime(2026, 12, 21) },
                Frames = Array.Empty<SolarFrameSlot>(),
                PeakSolarAltitudeDegrees = 2.5
            };

            var pipeline = new ShadowStudyPipeline(adapter);
            var summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { view },
                DateSchedules = new[] { emptySchedule, validSchedule },
                OutputFolder = _outputDir
            });

            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(1, summary.SkippedDateLabels.Count);
            Assert.AreEqual("261221", summary.SkippedDateLabels[0]);
            Assert.IsNotEmpty(summary.Warnings);
            Assert.AreEqual(1, adapter.ExportRequests[0].DateSchedules.Count);
        }

        [Test]
        public void Execute_WhenCancelledMidRun_ReturnsWasCancelledAndCleansTempDirectory()
        {
            using var cts = new CancellationTokenSource();
            var adapter = new FakeShadowStudyAdapter
            {
                CancelAfterFrameCount = 2,
                CancelSourceToTrigger = cts
            };
            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                adapter.GetSiteInfo());

            var pipeline = new ShadowStudyPipeline(adapter);
            var summary = pipeline.Execute(
                new ShadowStudyRunRequest
                {
                    Views = new[] { adapter.GetActiveViewInfo()! },
                    DateSchedules = new[] { schedule },
                    OutputFolder = _outputDir
                },
                cancellationToken: cts.Token);

            Assert.IsTrue(summary.WasCancelled);
            Assert.AreEqual(0, summary.GeneratedCount);
            Assert.AreEqual(0, Directory.GetFiles(_outputDir, "*.png").Length);
            Assert.IsFalse(Directory.Exists(adapter.ExportRequests[0].TempRootDirectory));
        }

        [Test]
        public void Execute_WhenAdapterThrows_StillCleansTempDirectoryInFinally()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                ExceptionToThrow = new InvalidOperationException("Simulated Revit export failure")
            };
            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                adapter.GetSiteInfo());

            var pipeline = new ShadowStudyPipeline(adapter);
            Assert.Throws<InvalidOperationException>(() => pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { adapter.GetActiveViewInfo()! },
                DateSchedules = new[] { schedule },
                OutputFolder = _outputDir
            }));

            Assert.IsFalse(Directory.Exists(adapter.ExportRequests[0].TempRootDirectory));
        }

        private sealed class ImmediateProgress : IProgress<ProgressState>
        {
            private readonly List<ProgressState> _sink;
            public ImmediateProgress(List<ProgressState> sink) => _sink = sink;
            public void Report(ProgressState value) => _sink.Add(value);
        }
    }
}
