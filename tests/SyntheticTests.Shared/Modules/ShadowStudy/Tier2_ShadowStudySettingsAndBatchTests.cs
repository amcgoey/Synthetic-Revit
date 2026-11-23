using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Color = Autodesk.Revit.DB.Color;
using View = Autodesk.Revit.DB.View;
using Synthetic.Features.ShadowStudy.Adapters;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;
using Synthetic.Features.ShadowStudy.Settings;
using Synthetic.Features.ShadowStudy.ViewModels;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #155:
    /// <see cref="ShadowStudySettings"/> document Extensible Storage round-trip across cache clears,
    /// Undo-visible Save Preset vs. non-mutating Export, and combined multi-view x multi-date execution
    /// inside a single rolled-back TransactionGroup across Hidden Line and preserved shaded visual styles.
    /// </summary>
    [TestFixture]
    public class Tier2_ShadowStudySettingsAndBatchTests
    {
        private UIApplication? _uiapp;
        private string _tempDir = string.Empty;
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

        [OneTimeSetUp]
        public void OneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            ProgressCoordinator.SuppressUI = true;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "Tier2Shadow155_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            SettingsManager.ClearCache();
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }

        [Test]
        public void ShadowStudySettings_RoundTripsThroughDataStorageAcrossCacheClear_WithDeepCloneIsolation()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);

                // Initial load with no DataStorage returns baseline defaults
                ShadowStudySettings initial = adapter.LoadShadowStudySettings();
                Assert.AreEqual(1, initial.SchemaVersion);
                Assert.AreEqual(60, initial.DefaultTimeIncrementMinutes);
                Assert.AreEqual(5.0, initial.DefaultMinSunAltitudeDegrees);
                Assert.AreEqual(80, initial.DefaultMaxCompositeDarknessPercent);
                Assert.IsFalse(initial.DefaultPreserveVisualStyle);
                Assert.AreEqual(0, initial.UserPresets.Count);

                // Save studio defaults + user preset with astronomical + custom dates
                var toSave = new ShadowStudySettings
                {
                    SchemaVersion = 1,
                    DefaultTimeIncrementMinutes = 30,
                    DefaultMinSunAltitudeDegrees = 8.5,
                    DefaultMaxCompositeDarknessPercent = 72,
                    DefaultPreserveVisualStyle = true,
                    UserPresets = new List<ShadowStudyPresetModel>
                    {
                        new ShadowStudyPresetModel
                        {
                            Name = "Studio Mixed Study",
                            TimeIncrementMinutes = 15,
                            MinSunAltitudeDegrees = 10.0,
                            MaxCompositeDarknessPercent = 65,
                            PreserveVisualStyle = true,
                            Dates = new List<ShadowStudyPresetDateSpec>
                            {
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.SummerSolstice
                                },
                                new ShadowStudyPresetDateSpec
                                {
                                    AstronomicalEvent = AstronomicalEventKind.None,
                                    CustomDate = new DateTime(2026, 7, 4),
                                    CustomLabel = "Site Open"
                                }
                            }
                        }
                    }
                };

                adapter.SaveShadowStudySettings(toSave);

                // Mutate local object after Save to verify Save deep-cloned into cache/storage
                toSave.UserPresets[0].Name = "Mutated After Save";

                // Clear cache to force genuine Extensible Storage DataStorage read
                SettingsManager.ClearCache(doc);

                ShadowStudySettings reloaded = adapter.LoadShadowStudySettings();
                Assert.AreEqual(30, reloaded.DefaultTimeIncrementMinutes);
                Assert.AreEqual(8.5, reloaded.DefaultMinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(72, reloaded.DefaultMaxCompositeDarknessPercent);
                Assert.IsTrue(reloaded.DefaultPreserveVisualStyle);
                Assert.AreEqual(1, reloaded.UserPresets.Count);
                Assert.AreEqual("Studio Mixed Study", reloaded.UserPresets[0].Name);
                Assert.AreEqual(15, reloaded.UserPresets[0].TimeIncrementMinutes);
                Assert.AreEqual(10.0, reloaded.UserPresets[0].MinSunAltitudeDegrees, 1e-6);
                Assert.AreEqual(65, reloaded.UserPresets[0].MaxCompositeDarknessPercent);
                Assert.IsTrue(reloaded.UserPresets[0].PreserveVisualStyle);
                Assert.AreEqual(2, reloaded.UserPresets[0].Dates.Count);
                Assert.AreEqual(AstronomicalEventKind.SummerSolstice, reloaded.UserPresets[0].Dates[0].AstronomicalEvent);
                Assert.AreEqual(AstronomicalEventKind.None, reloaded.UserPresets[0].Dates[1].AstronomicalEvent);
                Assert.AreEqual(new DateTime(2026, 7, 4), reloaded.UserPresets[0].Dates[1].CustomDate);
                Assert.AreEqual("Site Open", reloaded.UserPresets[0].Dates[1].CustomLabel);

                // Mutate reloaded clone and verify cache remains untouched on next load
                reloaded.DefaultTimeIncrementMinutes = 120;
                reloaded.UserPresets[0].Name = "Mutated Reloaded Clone";
                ShadowStudySettings secondLoad = adapter.LoadShadowStudySettings();
                Assert.AreEqual(30, secondLoad.DefaultTimeIncrementMinutes);
                Assert.AreEqual("Studio Mixed Study", secondLoad.UserPresets[0].Name);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SavePreset_CommitsUndoVisibleDataStorageTransaction_WhileExport_LeavesDocumentAndUndoUntouched()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                BatchSuite suite = SetupBatchSuite(doc);

                string cleanRvtPath = Path.Combine(_tempDir, $"CleanPresetAndExport_{_uiapp.Application.VersionNumber}.rvt");
                doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified, "Saved temp document must start clean.");

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp, suite.FloorPlan);

                // 1. Export without saving a preset -> zero document or Undo mutations
                string prefsPath = Path.Combine(_tempDir, "LocalPrefs.json");
                var prefsStore = new ShadowStudyUserPrefsStore(prefsPath);
                string exportOutDir = Path.Combine(_tempDir, "export_only");
                var exportVm = new ShadowStudyViewModel(
                    adapter,
                    new NoOpFolderDialogService(),
                    new DateTime(2026, 6, 21),
                    userPrefsStore: prefsStore,
                    textInputPromptService: new FixedPromptService(null))
                {
                    OutputFolder = exportOutDir,
                    PixelWidth = 320,
                    TimeStepMinutes = 240,
                    ExportIndividualFrames = false,
                    PreserveVisualStyle = true
                };

                bool? exportClosed = null;
                exportVm.RequestClose += r => exportClosed = r;
                exportVm.ExportCommand.Execute(null);
                Assert.AreEqual(true, exportClosed);

                ShadowStudyRunRequest request = exportVm.BuildRunRequest();
                DateSolarSchedule fullSchedule = request.DateSchedules[0];
                if (fullSchedule.Frames.Count > 1)
                {
                    request.DateSchedules = new[]
                    {
                        new DateSolarSchedule
                        {
                            DateEntry = fullSchedule.DateEntry,
                            PeakSolarAltitudeDegrees = fullSchedule.PeakSolarAltitudeDegrees,
                            ShadowIntensity = fullSchedule.ShadowIntensity,
                            Frames = new[] { fullSchedule.Frames[fullSchedule.Frames.Count / 2] }
                        }
                    };
                }

                var pipeline = new ShadowStudyPipeline(adapter);
                ShadowStudyRunSummary exportSummary = pipeline.Execute(request);
                Assert.IsFalse(exportSummary.WasCancelled);
                Assert.AreEqual(1, exportSummary.GeneratedCount);

                Assert.IsFalse(doc.IsModified, "Exporting Shadow Study must never dirty the document or create Undo history.");
                Assert.IsNull(FindShadowStudyDataStorage(doc), "Exporting must not create ShadowStudy DataStorage.");

                // 2. Explicit Save Preset As -> commits Undo-visible transaction and writes DataStorage
                var promptService = new FixedPromptService("Firm Solstice Preset");
                var saveVm = new ShadowStudyViewModel(
                    adapter,
                    new NoOpFolderDialogService(),
                    userPrefsStore: prefsStore,
                    textInputPromptService: promptService);

                saveVm.TimeStepMinutes = 30;
                saveVm.PreserveVisualStyle = true;
                saveVm.SavePresetAsCommand.Execute(null);

                Assert.IsTrue(doc.IsModified, "Explicit Save Preset must commit an Undo-visible transaction.");
                DataStorage? storage = FindShadowStudyDataStorage(doc);
                Assert.IsNotNull(storage, "Explicit Save Preset must persist Synthetic_ShadowStudy DataStorage.");

                SettingsManager.ClearCache(doc);
                ShadowStudySettings persisted = adapter.LoadShadowStudySettings();
                Assert.AreEqual(1, persisted.UserPresets.Count);
                Assert.AreEqual("Firm Solstice Preset", persisted.UserPresets[0].Name);
                Assert.AreEqual(30, persisted.UserPresets[0].TimeIncrementMinutes);
                Assert.IsTrue(persisted.UserPresets[0].PreserveVisualStyle);
                Assert.AreEqual(3, persisted.UserPresets[0].Dates.Count);
                Assert.AreEqual(AstronomicalEventKind.SummerSolstice, persisted.UserPresets[0].Dates[0].AstronomicalEvent);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Pipeline_CombinedMultiViewAndMultiDate_HiddenLineAndShaded_ExecutesInSingleRolledBackTransactionGroup()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                BatchSuite suite = SetupBatchSuite(doc);

                string cleanRvtPath = Path.Combine(_tempDir, $"CleanCombinedBatch_{_uiapp.Application.VersionNumber}.rvt");
                doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudySiteInfo site = adapter.GetSiteInfo();
                IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();

                ShadowStudyViewItem planItem = eligible.First(v => v.Id == suite.FloorPlan.UniqueId);
                ShadowStudyViewItem shaded3dItem = eligible.First(v => v.Id == suite.Shaded3D.UniqueId);

                ShadowStudyDateEntry summerEntry = BuiltInShadowStudyPresets.ExpandPreset(
                    BuiltInShadowStudyPresets.SummerSolstice,
                    2026,
                    site).Single();

                ShadowStudyDateEntry winterEntry = BuiltInShadowStudyPresets.ExpandPreset(
                    BuiltInShadowStudyPresets.WinterSolstice,
                    2026,
                    site).Single();

                DateSolarSchedule summerSchedule = SolarScheduleCalculator.BuildSchedule(
                    summerEntry,
                    site,
                    stepMinutes: 240,
                    minAltitudeDegrees: 15.0,
                    maxCompositeDarknessPercent: 80);

                DateSolarSchedule winterSchedule = SolarScheduleCalculator.BuildSchedule(
                    winterEntry,
                    site,
                    stepMinutes: 240,
                    minAltitudeDegrees: 10.0,
                    maxCompositeDarknessPercent: 80);

                Assert.IsTrue(summerSchedule.HasValidFrames);
                Assert.IsTrue(winterSchedule.HasValidFrames);

                int elementsBefore = new FilteredElementCollector(doc).GetElementCount();
                ElementId planTemplateBefore = suite.FloorPlan.ViewTemplateId;
                DisplayStyle planStyleBefore = suite.FloorPlan.DisplayStyle;
                DisplayStyle shadedStyleBefore = suite.Shaded3D.DisplayStyle;
                int shadedIntensityBefore = suite.Shaded3D.ShadowIntensity;

                string outputDir = Path.Combine(_tempDir, "combined_output");
                var pipeline = new ShadowStudyPipeline(adapter);
                ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
                {
                    Views = new[] { planItem, shaded3dItem },
                    DateSchedules = new[] { summerSchedule, winterSchedule },
                    OutputFolder = outputDir,
                    PixelWidth = 320,
                    ForceHiddenLine = false,
                    ForceWhiteBackground = true,
                    KeepIndividualFrames = true
                });

                Assert.IsFalse(summary.WasCancelled);
                Assert.AreEqual(4, summary.GeneratedCount, "2 views x 2 dates must produce 4 composites.");

                var expectedPairs = new[]
                {
                    (View: planItem, Schedule: summerSchedule),
                    (View: planItem, Schedule: winterSchedule),
                    (View: shaded3dItem, Schedule: summerSchedule),
                    (View: shaded3dItem, Schedule: winterSchedule)
                };

                foreach (var pair in expectedPairs)
                {
                    string dateLabel = pair.Schedule.DateEntry.DateLabel;
                    string compositePath = Path.Combine(
                        outputDir,
                        ShadowStudyPathHelper.BuildCompositeFileName(pair.View.ViewLabel, dateLabel));
                    Assert.IsTrue(File.Exists(compositePath), $"Missing composite '{compositePath}'.");

                    string framesFolder = Path.Combine(
                        outputDir,
                        ShadowStudyPathHelper.BuildFrameFolderName(pair.View.ViewLabel, dateLabel));
                    Assert.IsTrue(Directory.Exists(framesFolder), $"Missing frames folder '{framesFolder}'.");

                    string[] framePngs = Directory.GetFiles(framesFolder, "*.png");
                    Assert.AreEqual(pair.Schedule.Frames.Count, framePngs.Length);

                    foreach (SolarFrameSlot slot in pair.Schedule.Frames)
                    {
                        string expectedFrame = Path.Combine(
                            framesFolder,
                            ShadowStudyPathHelper.BuildFrameFileName(pair.View.ViewLabel, dateLabel, slot.ProjectLocalTime));
                        Assert.IsTrue(File.Exists(expectedFrame), $"Missing Shadow Frame '{expectedFrame}'.");
                    }
                }

                // Shaded 3D composite retains non-grayscale colors
                string shadedSummerPath = Path.Combine(
                    outputDir,
                    ShadowStudyPathHelper.BuildCompositeFileName(shaded3dItem.ViewLabel, summerEntry.DateLabel));
                Assert.IsTrue(ContainsNonGrayscaleColor(shadedSummerPath), "Preserved Shaded 3D composite must retain color pixels.");

                // Zero reference passes (ShadowIntensity = 0) leaked into output
                Assert.IsFalse(Directory.EnumerateFiles(outputDir, "*ref*", SearchOption.AllDirectories).Any());
                Assert.IsFalse(Directory.EnumerateFiles(outputDir, "r_*", SearchOption.AllDirectories).Any());

                // Single rolled-back TransactionGroup guarantees zero permanent document / view / Undo mutations
                Assert.IsFalse(doc.IsModified, "Combined multi-view x multi-date export must leave document and Undo stack completely clean.");
                Assert.AreEqual(elementsBefore, new FilteredElementCollector(doc).GetElementCount());
                Assert.AreEqual(planTemplateBefore, suite.FloorPlan.ViewTemplateId);
                Assert.AreEqual(planStyleBefore, suite.FloorPlan.DisplayStyle);
                Assert.AreEqual(shadedStyleBefore, suite.Shaded3D.DisplayStyle);
                Assert.AreEqual(shadedIntensityBefore, suite.Shaded3D.ShadowIntensity);
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static DataStorage? FindShadowStudyDataStorage(Document doc)
        {
            string targetName = "Synthetic_" + ShadowStudySettings.Name;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => string.Equals(ds.Name, targetName, StringComparison.OrdinalIgnoreCase));
        }

        private static Document OpenDetachedTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        private static BatchSuite SetupBatchSuite(Document doc)
        {
            using (var tx = new Transaction(doc, "Setup Ticket 155 Batch Suite"))
            {
                tx.Start();

                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .First();

                if (!new FilteredElementCollector(doc).OfClass(typeof(Wall)).Any())
                {
                    WallType wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                    Wall.Create(doc, Line.CreateBound(new XYZ(20, 0, 0), new XYZ(20, 15, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                }

                ViewPlan floorPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                if (floorPlan.ViewTemplateId != ElementId.InvalidElementId)
                {
                    floorPlan.ViewTemplateId = ElementId.InvalidElementId;
                }
                floorPlan.CropBoxActive = true;
                floorPlan.DisplayStyle = DisplayStyle.HLR;

                ViewFamilyType threeDType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .First(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                View3D shaded3D = View3D.CreateIsometric(doc, threeDType.Id);
                shaded3D.Name = "Batch155 Shaded 3D";
                if (shaded3D.ViewTemplateId != ElementId.InvalidElementId)
                {
                    shaded3D.ViewTemplateId = ElementId.InvalidElementId;
                }
                shaded3D.CropBoxActive = true;
                shaded3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
                shaded3D.DetailLevel = ViewDetailLevel.Fine;
                shaded3D.SetBackground(ViewDisplayBackground.CreateGradient(
                    new Color(40, 110, 200),
                    new Color(170, 200, 235),
                    new Color(150, 125, 95)));

                FillPatternElement? solidFill = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                if (solidFill != null)
                {
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                    ogs.SetSurfaceForegroundPatternColor(new Color(210, 115, 70));
                    shaded3D.SetCategoryOverrides(new ElementId(BuiltInCategory.OST_Walls), ogs);
                }

                tx.Commit();

                return new BatchSuite
                {
                    FloorPlan = floorPlan,
                    Shaded3D = shaded3D
                };
            }
        }

        private static bool ContainsNonGrayscaleColor(string pngPath)
        {
            using (var fs = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                byte[] pixels = new byte[width * height * 4];
                converted.CopyPixels(pixels, width * 4, 0);

                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    if (Math.Abs(b - r) > 10 || Math.Abs(g - r) > 10)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class BatchSuite
        {
            public ViewPlan FloorPlan { get; set; } = null!;
            public View3D Shaded3D { get; set; } = null!;
        }

        private sealed class NoOpFolderDialogService : IFolderDialogService
        {
            public string? SelectFolder(string description, string? initialDirectory = null) => null;
        }

        private sealed class FixedPromptService : ITextInputPromptService
        {
            private readonly string? _result;

            public FixedPromptService(string? result)
            {
                _result = result;
            }

            public string? PromptText(
                string title,
                string promptMessage,
                string? initialValue = null,
                Func<string, string?>? validator = null)
            {
                return _result;
            }
        }
    }
}
