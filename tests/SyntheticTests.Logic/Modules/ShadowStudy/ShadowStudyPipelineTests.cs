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
        public void Execute_WhenKeepIndividualFramesIsTrue_SavesShadowFramesInExactSubfolderWithoutReferencePasses()
        {
            var adapter = new FakeShadowStudyAdapter();
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;
            view.Name = "Site/Plan";

            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 120,
                minAltitudeDegrees: 10.0,
                maxCompositeDarknessPercent: 80);

            var pipeline = new ShadowStudyPipeline(adapter);
            var summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { view },
                DateSchedules = new[] { schedule },
                OutputFolder = _outputDir,
                PixelWidth = 32,
                KeepIndividualFrames = true
            });

            string expectedComposite = Path.Combine(_outputDir, "Site_Plan - 260621 - Composite.png");
            Assert.IsTrue(File.Exists(expectedComposite));

            string expectedFramesSubfolder = Path.Combine(_outputDir, "Site_Plan - 260621");
            string legacyWrongFramesSubfolder = Path.Combine(_outputDir, "Site_Plan - 260621 - Frames");
            Assert.IsTrue(Directory.Exists(expectedFramesSubfolder), "Subfolder must be <ViewLabel> - <DateLabel> without ' - Frames'.");
            Assert.IsFalse(Directory.Exists(legacyWrongFramesSubfolder));

            string[] savedFiles = Directory.GetFiles(expectedFramesSubfolder, "*.png").OrderBy(p => p).ToArray();
            Assert.AreEqual(schedule.Frames.Count, savedFiles.Length, "Only timestamped Shadow Frames may be retained (no reference passes).");

            for (int i = 0; i < schedule.Frames.Count; i++)
            {
                string expectedFrameName = $"Site_Plan - 260621 - {schedule.Frames[i].FrameFileStem}.png";
                string expectedFramePath = Path.Combine(expectedFramesSubfolder, expectedFrameName);
                Assert.IsTrue(File.Exists(expectedFramePath), $"Expected frame file '{expectedFrameName}'.");
                Assert.Contains(expectedFramePath, summary.SavedFramePaths.ToArray());
            }

            Assert.IsFalse(Directory.EnumerateFiles(_outputDir, "*ref*", SearchOption.AllDirectories).Any(),
                "Reference passes (ShadowIntensity = 0) must never be copied into output.");
            Assert.IsFalse(Directory.Exists(adapter.ExportRequests[0].TempRootDirectory));
        }

        [Test]
        public void Execute_WhenKeepIndividualFramesIsFalse_LeavesOnlyCompositeAfterCleanup()
        {
            var adapter = new FakeShadowStudyAdapter();
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;
            view.Name = "Main Plan";

            var schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 120,
                minAltitudeDegrees: 10.0,
                maxCompositeDarknessPercent: 80);

            var pipeline = new ShadowStudyPipeline(adapter);
            var summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { view },
                DateSchedules = new[] { schedule },
                OutputFolder = _outputDir,
                PixelWidth = 32,
                KeepIndividualFrames = false
            });

            Assert.AreEqual(0, summary.SavedFramePaths.Count);
            Assert.AreEqual(0, Directory.GetDirectories(_outputDir).Length, "No frame subfolder should exist when KeepIndividualFrames is false.");
            string[] files = Directory.GetFiles(_outputDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(1, files.Length);
            Assert.AreEqual(Path.Combine(_outputDir, "Main Plan - 260621 - Composite.png"), files[0]);
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

        [Test]
        public void Execute_MultiDateWithPresetsAndZeroFrameSkip_FormatsPerDateLabelsAndFeedsCompletionNotices()
        {
            var adapter = new FakeShadowStudyAdapter
            {
                SiteInfo = new ShadowStudySiteInfo
                {
                    PlaceName = "Tromso",
                    LatitudeDegrees = 69.6492,
                    LongitudeDegrees = 18.9553,
                    TimeZoneHours = 1.0,
                    UsesDst = false
                }
            };
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;
            view.Name = "Site Plan";

            var summer = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry
                {
                    Date = new DateTime(2026, 6, 21),
                    AstronomicalEvent = AstronomicalEventKind.SummerSolstice,
                    PresetName = "Summer Solstice"
                },
                site,
                stepMinutes: 120);

            var custom = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry
                {
                    Date = new DateTime(2026, 7, 4),
                    AstronomicalEvent = AstronomicalEventKind.None
                },
                site,
                stepMinutes: 120);

            var winterZeroFrames = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry
                {
                    Date = new DateTime(2026, 12, 21),
                    AstronomicalEvent = AstronomicalEventKind.WinterSolstice,
                    PresetName = "Winter Solstice"
                },
                site,
                stepMinutes: 120);

            Assert.IsFalse(winterZeroFrames.HasValidFrames);

            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { view },
                DateSchedules = new[] { summer, custom, winterZeroFrames },
                OutputFolder = _outputDir,
                PixelWidth = 32,
                KeepIndividualFrames = false
            });

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(2, summary.GeneratedCount);
            Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "Site Plan - 260621 - Summer Solstice - Composite.png")));
            Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "Site Plan - 260704 - Composite.png")));

            CollectionAssert.AreEqual(new[] { "261221 - Winter Solstice" }, summary.SkippedDateLabels);
            Assert.AreEqual(1, summary.Warnings.Count);
            StringAssert.Contains("261221 - Winter Solstice", summary.Warnings[0]);

            var completionVm = ExportCompletionViewModel.ForBatch(
                summary.OutputFolder,
                summary.GeneratedCount,
                summary.PrimaryCompositePath,
                outputNounPlural: "composites",
                primaryActionLabel: "Open Composite",
                warningNotices: summary.Warnings);

            Assert.IsTrue(completionVm.HasWarnings);
            Assert.AreEqual(1, completionVm.WarningNotices.Count);
            StringAssert.Contains("261221 - Winter Solstice", completionVm.WarningNotices[0]);
            StringAssert.Contains("Generated 2 composites", completionVm.SummaryText);
        }

        [Test]
        public void Execute_LongAstronomicalDateLabelPath_ThrowsPathTooLongBeforeExport()
        {
            var adapter = new FakeShadowStudyAdapter();
            var site = adapter.GetSiteInfo();
            var view = adapter.GetActiveViewInfo()!;
            view.Name = "Very Long Architectural View Name For Shadow Study";

            var summer = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry
                {
                    Date = new DateTime(2026, 6, 21),
                    AstronomicalEvent = AstronomicalEventKind.SummerSolstice,
                    PresetName = "Summer Solstice"
                },
                site,
                stepMinutes: 120);

            string deepFolder = Path.Combine(_outputDir, new string('x', 150));
            var pipeline = new ShadowStudyPipeline(adapter);

            Assert.Throws<PathTooLongException>(() => pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { view },
                DateSchedules = new[] { summer },
                OutputFolder = deepFolder,
                KeepIndividualFrames = true
            }));

            Assert.AreEqual(0, adapter.ExportRequests.Count);
        }

        private sealed class ImmediateProgress : IProgress<ProgressState>
        {
            private readonly List<ProgressState> _sink;
            public ImmediateProgress(List<ProgressState> sink) => _sink = sink;
            public void Report(ProgressState value) => _sink.Add(value);
        }
    }
}
