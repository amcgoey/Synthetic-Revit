using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Transform = Autodesk.Revit.DB.Transform;
using View = Autodesk.Revit.DB.View;
using Synthetic.Features.ShadowStudy.Adapters;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;
using Synthetic.Platform.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #149 (Shadow Study tracer bullet).
    /// Runs inside real Revit 2023–2026 via ricaun.RevitTest.
    /// </summary>
    [TestFixture]
    public class Tier2_ShadowStudyTracerBulletTests
    {
        private UIApplication? _uiapp;
        private string _tempOutputDir = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [SetUp]
        public void SetUp()
        {
            _tempOutputDir = Path.Combine(
                Path.GetTempPath(),
                "Tier2ShadowStudy_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempOutputDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempOutputDir))
            {
                try
                {
                    Directory.Delete(_tempOutputDir, true);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }

        [Test]
        public void Adapter_AllowlistAndSiteInfo_FiltersViewsAndConvertsSiteRadians()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                EnsureNonGraphicalTestViews(doc);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();

                Assert.IsNotEmpty(eligible, "Detached template must contain eligible graphical views.");
                Assert.IsFalse(eligible.Any(v => v.ViewType == "DrawingSheet"), "DrawingSheet must be excluded.");
                Assert.IsFalse(eligible.Any(v => v.ViewType == "Schedule"), "Schedule must be excluded.");
                Assert.IsFalse(eligible.Any(v => v.ViewType == "DraftingView"), "DraftingView must be excluded.");
                Assert.IsFalse(eligible.Any(v => v.ViewType == "Legend"), "Legend must be excluded.");
                Assert.IsFalse(eligible.Any(v => v.ViewType == "Walkthrough"), "Walkthrough must be excluded.");

                ShadowStudySiteInfo site = adapter.GetSiteInfo();
                double expectedLat = doc.SiteLocation.Latitude * (180.0 / Math.PI);
                double expectedLon = doc.SiteLocation.Longitude * (180.0 / Math.PI);

                Assert.AreEqual(expectedLat, site.LatitudeDegrees, 1e-6);
                Assert.AreEqual(expectedLon, site.LongitudeDegrees, 1e-6);
                Assert.AreEqual(doc.SiteLocation.TimeZone, site.TimeZoneHours, 1e-6);
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Pipeline_MultiViewTypesWithControllingTemplate_ExportsConsistentPngsAndRestoresModel()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                List<View> targetViews = PrepareDiverseGraphicalViewsWithMassingAndTemplate(doc);
                Assert.GreaterOrEqual(targetViews.Count, 4, "Expected FloorPlan, Ortho 3D, Perspective 3D, Section, and Elevation.");

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudySiteInfo site = adapter.GetSiteInfo();
                DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                    new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                    site,
                    stepMinutes: 120,
                    minAltitudeDegrees: 10.0,
                    maxCompositeDarknessPercent: 80);

                Assert.IsTrue(schedule.HasValidFrames, "Summer solstice schedule must produce valid frames.");

                IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();
                var selectedItems = targetViews
                    .Select(v => eligible.First(item => item.Id == v.UniqueId))
                    .ToList();

                int elementCountBefore = CountAllElements(doc);
                List<ViewSnapshot> snapshotsBefore = targetViews.Select(CaptureSnapshot).ToList();

                var pipeline = new ShadowStudyPipeline(adapter);
                ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
                {
                    Views = selectedItems,
                    DateSchedules = new[] { schedule },
                    OutputFolder = _tempOutputDir,
                    PixelWidth = 400,
                    ForceHiddenLine = true,
                    ForceWhiteBackground = true,
                    KeepIndividualFrames = true
                });

                Assert.IsFalse(summary.WasCancelled);
                Assert.AreEqual(selectedItems.Count, summary.GeneratedCount);

                foreach (string compositePath in summary.CompositePaths)
                {
                    Assert.IsTrue(File.Exists(compositePath), $"Missing composite PNG: {compositePath}");
                    GetPngDimensions(compositePath, out int w, out int h);
                    Assert.AreEqual(400, w, $"Expected 400px horizontal width for {compositePath}");
                    Assert.Greater(h, 0);
                }

                // Verify frame dimensions match composite dimensions per view
                foreach (var viewItem in selectedItems)
                {
                    string compositePath = summary.CompositePaths.First(p => Path.GetFileName(p).StartsWith(viewItem.ViewLabel + " - ", StringComparison.OrdinalIgnoreCase));
                    GetPngDimensions(compositePath, out int compW, out int compH);

                    string framesDir = Path.Combine(
                        _tempOutputDir,
                        ShadowStudyPathHelper.BuildFrameFolderName(viewItem.ViewLabel, schedule.DateEntry.DateLabel));
                    Assert.IsTrue(Directory.Exists(framesDir));
                    string[] framePngs = Directory.GetFiles(framesDir, "*.png");
                    Assert.AreEqual(schedule.Frames.Count, framePngs.Length);

                    foreach (SolarFrameSlot slot in schedule.Frames)
                    {
                        string expectedFramePath = Path.Combine(
                            framesDir,
                            ShadowStudyPathHelper.BuildFrameFileName(viewItem.ViewLabel, schedule.DateEntry.DateLabel, slot.ProjectLocalTime));
                        Assert.IsTrue(File.Exists(expectedFramePath), $"Missing expected frame PNG: {expectedFramePath}");
                    }

                    foreach (string framePng in framePngs)
                    {
                        GetPngDimensions(framePng, out int fw, out int fh);
                        Assert.AreEqual(compW, fw, $"Frame width mismatch in {framePng}");
                        Assert.AreEqual(compH, fh, $"Frame height mismatch in {framePng}");
                    }

                    // If Cast Shadows is active on the view, verify shadow/dark pixels exist
                    View revitView = (View)doc.GetElement(viewItem.Id);
                    Parameter? shadowParam = revitView.get_Parameter(BuiltInParameter.GRAPHIC_DISPLAY_OPTIONS_SHADOWS);
                    if (shadowParam != null && shadowParam.AsInteger() == 1)
                    {
                        Assert.IsTrue(ContainsNonWhitePixels(compositePath), $"Expected shadow/model pixels in {compositePath}");
                    }
                }

                // Verify 100% restoration of element counts, view properties, View Templates, Crop/Scope Boxes
                Assert.AreEqual(elementCountBefore, CountAllElements(doc), "Document element count must remain unchanged.");
                for (int i = 0; i < targetViews.Count; i++)
                {
                    AssertSnapshotUnchanged(snapshotsBefore[i], targetViews[i]);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Pipeline_OnCancellationAndMidBatchException_RestoresDocumentAndViewsCompletely()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                List<View> targetViews = PrepareDiverseGraphicalViewsWithMassingAndTemplate(doc);
                View testView = targetViews[0];

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudySiteInfo site = adapter.GetSiteInfo();
                DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                    new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                    site,
                    stepMinutes: 120,
                    minAltitudeDegrees: 10.0,
                    maxCompositeDarknessPercent: 80);

                ShadowStudyViewItem viewItem = adapter.GetEligibleViews().First(v => v.Id == testView.UniqueId);
                int elementCountBefore = CountAllElements(doc);
                ViewSnapshot snapshotBefore = CaptureSnapshot(testView);

                // 1. Mid-run cancellation after first progress report
                using (var cts = new CancellationTokenSource())
                {
                    var progress = new CallbackProgress(state =>
                    {
                        if (state.ProgressIndex >= 1)
                        {
                            cts.Cancel();
                        }
                    });

                    var pipeline = new ShadowStudyPipeline(adapter);
                    ShadowStudyRunSummary cancelledSummary = pipeline.Execute(
                        new ShadowStudyRunRequest
                        {
                            Views = new[] { viewItem },
                            DateSchedules = new[] { schedule },
                            OutputFolder = _tempOutputDir,
                            PixelWidth = 300
                        },
                        progress,
                        cts.Token);

                    Assert.IsTrue(cancelledSummary.WasCancelled);
                    Assert.AreEqual(0, cancelledSummary.GeneratedCount);
                    Assert.AreEqual(0, Directory.GetFiles(_tempOutputDir, "*.png").Length);
                }

                Assert.AreEqual(elementCountBefore, CountAllElements(doc));
                AssertSnapshotUnchanged(snapshotBefore, testView);

                // 2. Mid-batch exception after first valid view is prepared/mutated inside outer TransactionGroup
                var missingSecondView = new ShadowStudyViewItem
                {
                    Id = "non-existent-unique-id",
                    Name = "Missing",
                    ViewType = "FloorPlan"
                };

                var pipelineForError = new ShadowStudyPipeline(adapter);
                Assert.Throws<InvalidOperationException>(() => pipelineForError.Execute(
                    new ShadowStudyRunRequest
                    {
                        Views = new[] { viewItem, missingSecondView },
                        DateSchedules = new[] { schedule },
                        OutputFolder = _tempOutputDir,
                        PixelWidth = 300
                    }));

                Assert.AreEqual(elementCountBefore, CountAllElements(doc));
                AssertSnapshotUnchanged(snapshotBefore, testView);
            }
            finally
            {
                doc.Close(false);
            }
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

        private static void EnsureNonGraphicalTestViews(Document doc)
        {
            using (var tx = new Transaction(doc, "Setup Non-Graphical Views"))
            {
                tx.Start();
                var titleBlock = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .FirstElementId();
                ViewSheet.Create(doc, titleBlock ?? ElementId.InvalidElementId);

                ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Walls));

                var draftingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);
                if (draftingType != null)
                {
                    ViewDrafting.Create(doc, draftingType.Id);
                }

                tx.Commit();
            }
        }

        private static List<View> PrepareDiverseGraphicalViewsWithMassingAndTemplate(Document doc)
        {
            var views = new List<View>();

            using (var tx = new Transaction(doc, "Setup Shadow Study Views"))
            {
                tx.Start();

                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .First();

                Wall? existingWall = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .FirstOrDefault();

                XYZ targetCenter = XYZ.Zero;
                if (existingWall == null)
                {
                    WallType wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    existingWall = Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                    Wall.Create(doc, Line.CreateBound(new XYZ(20, 0, 0), new XYZ(20, 15, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                    targetCenter = new XYZ(10, 7.5, 6);
                }
                else
                {
                    BoundingBoxXYZ? wallBox = existingWall.get_BoundingBox(null);
                    if (wallBox != null)
                    {
                        targetCenter = (wallBox.Min + wallBox.Max) * 0.5;
                    }
                }

                // FloorPlan + controlling View Template
                ViewPlan? floorPlan = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                if (floorPlan != null)
                {
                    floorPlan.CropBoxActive = true;
                    View? existingTemplate = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => v.IsTemplate && v.ViewType == ViewType.FloorPlan);

                    if (existingTemplate == null)
                    {
                        existingTemplate = floorPlan.CreateViewTemplate();
                    }

                    floorPlan.ViewTemplateId = existingTemplate.Id;
                    views.Add(floorPlan);
                }

                // Orthographic and Perspective View3D
                ViewFamilyType? threeDType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                if (threeDType != null)
                {
                    View3D ortho3d = View3D.CreateIsometric(doc, threeDType.Id);
                    ortho3d.Name = "Shadow Ortho 3D";
                    ortho3d.CropBoxActive = true;
                    views.Add(ortho3d);

                    View3D persp3d = View3D.CreatePerspective(doc, threeDType.Id);
                    persp3d.Name = "Shadow Perspective 3D";
                    XYZ eye = targetCenter + new XYZ(25, -25, 16);
                    XYZ forward = (targetCenter - eye).Normalize();
                    XYZ right = forward.CrossProduct(XYZ.BasisZ).Normalize();
                    XYZ up = right.CrossProduct(forward).Normalize();
                    persp3d.SetOrientation(new ViewOrientation3D(eye, up, forward));
                    persp3d.CropBoxActive = true;
                    views.Add(persp3d);
                }

                // Section
                ViewFamilyType? sectionType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

                if (sectionType != null)
                {
                    Transform sectionTransform = Transform.Identity;
                    sectionTransform.Origin = targetCenter;
                    sectionTransform.BasisX = XYZ.BasisX;
                    sectionTransform.BasisY = XYZ.BasisZ;
                    sectionTransform.BasisZ = -XYZ.BasisY;

                    var box = new BoundingBoxXYZ
                    {
                        Transform = sectionTransform,
                        Min = new XYZ(-20, -15, -15),
                        Max = new XYZ(20, 20, 15)
                    };
                    ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, box);
                    section.Name = "Shadow Section";
                    views.Add(section);
                }

                // Elevation
                View? elevation = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.Elevation);

                if (elevation == null)
                {
                    ViewFamilyType? elevType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewFamilyType))
                        .Cast<ViewFamilyType>()
                        .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Elevation);
                    if (elevType != null)
                    {
                        ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevType.Id, new XYZ(10, -20, 0), 50);
                        elevation = marker.CreateElevation(doc, floorPlan != null ? floorPlan.Id : doc.ActiveView.Id, 0);
                        elevation.Name = "Shadow Elevation";
                    }
                }

                if (elevation != null)
                {
                    views.Add(elevation);
                }

                tx.Commit();
            }

            return views;
        }

        private static int CountAllElements(Document doc)
        {
            return new FilteredElementCollector(doc).GetElementCount();
        }

        private static ViewSnapshot CaptureSnapshot(View view)
        {
            SunAndShadowSettings sun = view.SunAndShadowSettings;
            Parameter? scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            return new ViewSnapshot
            {
                ViewTemplateId = view.ViewTemplateId,
                DisplayStyle = view.DisplayStyle,
                ShadowIntensity = view.ShadowIntensity,
                CropBoxActive = view.CropBoxActive,
                CropBoxMin = view.CropBox.Min,
                CropBoxMax = view.CropBox.Max,
                ScopeBoxId = scopeBoxParam != null ? scopeBoxParam.AsElementId() : ElementId.InvalidElementId,
                SharesSettings = sun.SharesSettings,
                SunPathVisible = sun.Visible,
                SunType = sun.SunAndShadowType,
                SunriseToSunset = sun.SunriseToSunset,
                StartDateAndTime = sun.StartDateAndTime
            };
        }

        private static void AssertSnapshotUnchanged(ViewSnapshot before, View view)
        {
            SunAndShadowSettings sun = view.SunAndShadowSettings;
            Parameter? scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            ElementId scopeBoxAfter = scopeBoxParam != null ? scopeBoxParam.AsElementId() : ElementId.InvalidElementId;

            Assert.AreEqual(before.ViewTemplateId, view.ViewTemplateId, $"ViewTemplateId changed on {view.Name}");
            Assert.AreEqual(before.DisplayStyle, view.DisplayStyle, $"DisplayStyle changed on {view.Name}");
            Assert.AreEqual(before.ShadowIntensity, view.ShadowIntensity, $"ShadowIntensity changed on {view.Name}");
            Assert.AreEqual(before.CropBoxActive, view.CropBoxActive, $"CropBoxActive changed on {view.Name}");
            Assert.IsTrue(before.CropBoxMin.IsAlmostEqualTo(view.CropBox.Min), $"CropBox.Min changed on {view.Name}");
            Assert.IsTrue(before.CropBoxMax.IsAlmostEqualTo(view.CropBox.Max), $"CropBox.Max changed on {view.Name}");
            Assert.AreEqual(before.ScopeBoxId, scopeBoxAfter, $"ScopeBox changed on {view.Name}");
            Assert.AreEqual(before.SharesSettings, sun.SharesSettings, $"SharesSettings changed on {view.Name}");
            Assert.AreEqual(before.SunPathVisible, sun.Visible, $"SunPath Visible changed on {view.Name}");
            Assert.AreEqual(before.SunType, sun.SunAndShadowType, $"SunAndShadowType changed on {view.Name}");
            Assert.AreEqual(before.SunriseToSunset, sun.SunriseToSunset, $"SunriseToSunset changed on {view.Name}");
            Assert.AreEqual(before.StartDateAndTime, sun.StartDateAndTime, $"StartDateAndTime changed on {view.Name}");
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

        private static bool ContainsNonWhitePixels(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                byte[] pixels = new byte[width * height * 4];
                converted.CopyPixels(pixels, width * 4, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] < 250 || pixels[i + 1] < 250 || pixels[i + 2] < 250)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class ViewSnapshot
        {
            public ElementId ViewTemplateId { get; set; } = ElementId.InvalidElementId;
            public DisplayStyle DisplayStyle { get; set; }
            public int ShadowIntensity { get; set; }
            public bool CropBoxActive { get; set; }
            public XYZ CropBoxMin { get; set; } = XYZ.Zero;
            public XYZ CropBoxMax { get; set; } = XYZ.Zero;
            public ElementId ScopeBoxId { get; set; } = ElementId.InvalidElementId;
            public bool SharesSettings { get; set; }
            public bool SunPathVisible { get; set; }
            public SunAndShadowType SunType { get; set; }
            public bool SunriseToSunset { get; set; }
            public DateTime StartDateAndTime { get; set; }
        }

        private sealed class CallbackProgress : IProgress<ProgressState>
        {
            private readonly Action<ProgressState> _callback;
            public CallbackProgress(Action<ProgressState> callback) => _callback = callback;
            public void Report(ProgressState value) => _callback(value);
        }
    }
}
