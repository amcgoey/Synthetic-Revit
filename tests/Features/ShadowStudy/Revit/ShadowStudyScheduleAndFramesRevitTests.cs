using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using View = Autodesk.Revit.DB.View;
using Synthetic.Features.ShadowStudy.Adapters;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;
using Synthetic.Features.ShadowStudy.ViewModels;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #150 (schedule controls, custom pixel width,
    /// exact Shadow Frame subfolder retention, local prefs isolation, and zero document/Undo mutations).
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class ShadowStudyScheduleAndFramesRevitTests : RevitIsolatedDocumentFixture
    {
        [Test]
        public void ViewModelAndPipeline_CustomPixelWidthAndKeepFrames_ExportsExactSubfolderWithZeroUndoMutations()
        {
            Document doc = OpenAndSaveCleanTempDocument(out View activePlan);

            Assert.IsFalse(doc.IsModified, "Saved temp document must start clean (IsModified == false).");

            int elementsBefore = new FilteredElementCollector(doc).GetElementCount();
            ElementId templateBefore = activePlan.ViewTemplateId;
            DisplayStyle styleBefore = activePlan.DisplayStyle;
            int shadowIntensityBefore = activePlan.ShadowIntensity;
            bool cropBefore = activePlan.CropBoxActive;

            string outputDir = GetTempFilePath("output_keep_frames");
            string prefsPath = Path.Combine(CurrentTestTempDirectory, "prefs", "ShadowStudyUserPrefs.json");
            var prefsStore = new ShadowStudyUserPrefsStore(prefsPath);

            var realAdapter = new RevitShadowStudyAdapter(doc, null, UiApp);
            ShadowStudyViewItem activePlanItem = realAdapter.GetEligibleViews().First(v => v.Id == activePlan.UniqueId);
            IShadowStudyAdapter adapter = new ActiveViewAdapter(realAdapter, activePlanItem);
            var folderDialog = new StubFolderDialogService();
            var vm = new ShadowStudyViewModel(adapter, folderDialog, new DateTime(2026, 6, 21), prefsStore);

            Assert.IsTrue(vm.ExportIndividualFrames);
            Assert.AreEqual(3000, vm.PixelWidth);

            vm.OutputFolder = outputDir;
            vm.TimeStepMinutes = 120;
            vm.MinSunAltitudeText = "10";
            vm.MaxCompositeDarknessText = "75";
            vm.PixelWidthText = "512";

            Assert.IsFalse(vm.HasValidationErrors);
            Assert.IsTrue(vm.CanExport);

            bool confirmed = false;
            vm.RequestClose += result => confirmed = result;
            vm.ExportCommand.Execute(null);
            Assert.IsTrue(confirmed);

            ShadowStudyUserPrefs savedPrefs = prefsStore.Load();
            Assert.AreEqual(outputDir, savedPrefs.LastOutputFolder);
            Assert.AreEqual(512, savedPrefs.PixelWidth);
            Assert.IsTrue(savedPrefs.ExportIndividualFrames);

            ShadowStudyRunRequest request = vm.BuildRunRequest();
            Assert.AreEqual(512, request.PixelWidth);
            Assert.IsTrue(request.KeepIndividualFrames);

            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(request);

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(1, summary.GeneratedCount);

            // Verify zero document or Undo mutations
            Assert.IsFalse(doc.IsModified, "Shadow Study Export must never modify the Revit document or dirty Undo history.");
            Assert.AreEqual(elementsBefore, new FilteredElementCollector(doc).GetElementCount());
            Assert.AreEqual(templateBefore, activePlan.ViewTemplateId);
            Assert.AreEqual(styleBefore, activePlan.DisplayStyle);
            Assert.AreEqual(shadowIntensityBefore, activePlan.ShadowIntensity);
            Assert.AreEqual(cropBefore, activePlan.CropBoxActive);

            // Verify composite dimensions
            string compositePath = summary.PrimaryCompositePath!;
            Assert.IsTrue(File.Exists(compositePath));
            GetPngDimensions(compositePath, out int compWidth, out int compHeight);
            Assert.AreEqual(512, compWidth);
            Assert.Greater(compHeight, 0);

            // Verify exact frame subfolder and <ViewLabel> - <DateLabel> - <HHmm>.png naming
            ShadowStudyViewItem viewItem = request.Views[0];
            DateSolarSchedule schedule = request.DateSchedules[0];
            string expectedSubfolder = Path.Combine(
                outputDir,
                ShadowStudyPathHelper.BuildFrameFolderName(viewItem.ViewLabel, schedule.DateEntry.DateLabel));
            string wrongLegacySubfolder = expectedSubfolder + " - Frames";

            Assert.IsTrue(Directory.Exists(expectedSubfolder), "Expected <ViewLabel> - <DateLabel> subfolder.");
            Assert.IsFalse(Directory.Exists(wrongLegacySubfolder), "Legacy ' - Frames' subfolder must not be created.");

            string[] frameFiles = Directory.GetFiles(expectedSubfolder, "*.png");
            Assert.AreEqual(schedule.Frames.Count, frameFiles.Length);
            Assert.IsFalse(Directory.EnumerateFiles(outputDir, "*ref*", SearchOption.AllDirectories).Any());

            foreach (SolarFrameSlot slot in schedule.Frames)
            {
                string expectedFramePath = Path.Combine(
                    expectedSubfolder,
                    ShadowStudyPathHelper.BuildFrameFileName(viewItem.ViewLabel, schedule.DateEntry.DateLabel, slot.ProjectLocalTime));
                Assert.IsTrue(File.Exists(expectedFramePath), $"Missing Shadow Frame: {expectedFramePath}");
                GetPngDimensions(expectedFramePath, out int fw, out int fh);
                Assert.AreEqual(512, fw);
                Assert.AreEqual(compHeight, fh);
            }
        }

        [Test]
        public void Pipeline_WhenKeepIndividualFramesIsFalse_LeavesOnlyCompositeAndZeroDocumentMutations()
        {
            Document doc = OpenAndSaveCleanTempDocument(out View activePlan);

            Assert.IsFalse(doc.IsModified);

            string outputDir = GetTempFilePath("output_composite_only");
            var adapter = new RevitShadowStudyAdapter(doc, null, UiApp);
            ShadowStudyViewItem viewItem = adapter.GetEligibleViews().First(v => v.Id == activePlan.UniqueId);
            ShadowStudySiteInfo site = adapter.GetSiteInfo();
            DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 120,
                minAltitudeDegrees: 10.0,
                maxCompositeDarknessPercent: 80);

            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { viewItem },
                DateSchedules = new[] { schedule },
                OutputFolder = outputDir,
                PixelWidth = 360,
                KeepIndividualFrames = false
            });

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(0, summary.SavedFramePaths.Count);
            Assert.AreEqual(0, Directory.GetDirectories(outputDir).Length);

            string[] allOutputFiles = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(1, allOutputFiles.Length);
            Assert.AreEqual(summary.PrimaryCompositePath, allOutputFiles[0]);

            GetPngDimensions(allOutputFiles[0], out int width, out int height);
            Assert.AreEqual(360, width);
            Assert.Greater(height, 0);

            Assert.IsFalse(doc.IsModified, "Document must remain unmodified when KeepIndividualFrames is false.");
        }

        private Document OpenAndSaveCleanTempDocument(out View activePlan)
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            using (var tx = new Transaction(doc, "Prepare Clean Test View"))
            {
                tx.Start();
                ViewPlan? plan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                if (plan == null)
                {
                    throw new InvalidOperationException("No eligible FloorPlan found in test template.");
                }

                plan.CropBoxActive = true;
                activePlan = plan;
                tx.Commit();
            }

            string cleanRvtPath = GetTempFilePath($"CleanShadowStudy_{App.VersionNumber}.rvt");
            doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
            return doc;
        }

        private static void GetPngDimensions(string path, out int width, out int height)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }
        }

        private sealed class StubFolderDialogService : Synthetic.Platform.UI.IFolderDialogService
        {
            public string? SelectFolder(string title, string? initialPath = null) => null;
        }

        private sealed class ActiveViewAdapter : IShadowStudyAdapter
        {
            private readonly IShadowStudyAdapter _inner;
            private readonly ShadowStudyViewItem _activeView;

            public ActiveViewAdapter(IShadowStudyAdapter inner, ShadowStudyViewItem activeView)
            {
                _inner = inner;
                _activeView = activeView;
            }

            public ShadowStudySiteInfo GetSiteInfo() => _inner.GetSiteInfo();
            public ShadowStudyViewItem? GetActiveViewInfo() => _activeView;
            public IReadOnlyList<ShadowStudyViewItem> GetEligibleViews() => _inner.GetEligibleViews();
            public IReadOnlyList<ShadowStudyPrintSetItem> GetPrintSets() => _inner.GetPrintSets();

            public ShadowStudyPrintSetItem SavePrintSet(
                string printSetName,
                IReadOnlyList<string> selectedShadowViewIds,
                bool saveAsNew,
                string? sourcePrintSetId = null)
                => _inner.SavePrintSet(printSetName, selectedShadowViewIds, saveAsNew, sourcePrintSetId);

            public Synthetic.Features.ShadowStudy.Settings.ShadowStudySettings LoadShadowStudySettings()
                => _inner.LoadShadowStudySettings();

            public void SaveShadowStudySettings(Synthetic.Features.ShadowStudy.Settings.ShadowStudySettings settings)
                => _inner.SaveShadowStudySettings(settings);

            public RawFrameExportResult ExportRawFrames(
                RawFrameExportRequest request,
                IProgress<Synthetic.Platform.UI.ProgressState>? progress = null,
                System.Threading.CancellationToken cancellationToken = default)
                => _inner.ExportRawFrames(request, progress, cancellationToken);
        }
    }
}
