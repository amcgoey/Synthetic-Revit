using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Color = Autodesk.Revit.DB.Color;
using View = Autodesk.Revit.DB.View;
using Synthetic.Features.ShadowStudy.Adapters;
using Synthetic.Features.ShadowStudy.Models;
using Synthetic.Features.ShadowStudy.Services;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #153 (Shaded Visual Style preservation,
    /// per-timestamp reference normalization, and Wireframe/Rendering fallback to Hidden Line).
    /// Runs inside real Revit 2023–2026 via ricaun.RevitTest.
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class ShadowStudyVisualStyleRevitTests : RevitIsolatedDocumentFixture
    {
        [Test]
        public void MapViewItem_WireframeAndUncroppedView_SurfacesCoexistingWarningBadgesInBothModes()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            View3D wireframe3D = SetupVisualStyleTestViews(doc).Wireframe3D;

            var adapter = new RevitShadowStudyAdapter(doc, null, UiApp, wireframe3D);

            // CurrentView mode
            ShadowStudyViewItem? activeItem = adapter.GetActiveViewInfo();
            Assert.IsNotNull(activeItem);
            Assert.IsFalse(activeItem!.IsCropped);
            Assert.AreEqual("Wireframe", activeItem.DisplayStyleName);
            Assert.IsTrue(activeItem.HasWarnings);
            Assert.IsTrue(activeItem.Warnings.Any(w => w.Code == "Uncropped"));
            Assert.IsTrue(activeItem.Warnings.Any(w => w.Code == "VisualStyle"));

            // SelectedViews mode
            IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();
            ShadowStudyViewItem listedItem = eligible.First(v => v.Id == wireframe3D.UniqueId);
            Assert.IsTrue(listedItem.Warnings.Any(w => w.Code == "Uncropped"));
            Assert.IsTrue(listedItem.Warnings.Any(w => w.Code == "VisualStyle"));
        }

        [Test]
        public void Export_MixedBatchWithPreserveVisualStyle_PreservesShadedAndHlrBackgroundsFallsBackWireframeAndRollsBack()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            VisualStyleSuite suite = SetupVisualStyleTestViews(doc);

            string cleanRvtPath = GetTempFilePath("CleanVisualStyleBaseline.rvt");
            doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified);

            var adapter = new RevitShadowStudyAdapter(doc, null, UiApp);
            ShadowStudySiteInfo site = adapter.GetSiteInfo();
            DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 240,
                minAltitudeDegrees: 15.0,
                maxCompositeDarknessPercent: 80);

            Assert.GreaterOrEqual(schedule.Frames.Count, 2, "Expected at least 2 daylight frames.");

            IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();
            var targetViews = new View[] { suite.Shaded3D, suite.Wireframe3D, suite.HlrCustomBg3D };
            var selectedItems = targetViews
                .Select(v => eligible.First(item => item.Id == v.UniqueId))
                .ToList();

            int elementCountBefore = new FilteredElementCollector(doc).GetElementCount();
            List<VisualSnapshot> snapshotsBefore = targetViews.Select(CaptureSnapshot).ToList();

            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = selectedItems,
                DateSchedules = new[] { schedule },
                OutputFolder = CurrentTestTempDirectory,
                PixelWidth = 320,
                ForceHiddenLine = false,
                ForceWhiteBackground = true,
                KeepIndividualFrames = true
            });

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(3, summary.GeneratedCount);

            string dateLabel = schedule.DateEntry.DateLabel;
            string shadedComposite = GetTempFilePath($"Visual Shaded 3D - {dateLabel} - Composite.png");
            string wireframeComposite = GetTempFilePath($"Visual Wireframe 3D - {dateLabel} - Composite.png");
            string hlrComposite = GetTempFilePath($"Visual HLR CustomBg 3D - {dateLabel} - Composite.png");

            Assert.IsTrue(File.Exists(shadedComposite), "Missing shaded 3D composite.");
            Assert.IsTrue(File.Exists(wireframeComposite), "Missing Wireframe fallback composite.");
            Assert.IsTrue(File.Exists(hlrComposite), "Missing preserved HLR composite.");

            // 1. Shaded 3D composite retains colored pixels & non-white sky background
            byte[] shadedPixels = ReadBgra32Pixels(shadedComposite, out int shadedW, out int shadedH);
            Assert.AreEqual(320, shadedW);
            Assert.Greater(shadedH, 0);
            Assert.IsTrue(HasNonGrayscaleColorPixel(shadedPixels), "Preserved shaded 3D composite must retain non-grayscale color pixels.");
            AssertTopLeftIsNotPureWhite(shadedPixels, "Preserved shaded 3D composite must preserve its non-white gradient background.");

            // 2. Wireframe 3D composite fell back to Hidden Line on pure white background
            byte[] wireframePixels = ReadBgra32Pixels(wireframeComposite, out int wfW, out _);
            Assert.AreEqual(320, wfW);
            AssertTopLeftIsPureWhite(wireframePixels, "Wireframe fallback composite must render on a pure white background.");

            // 3. Preserved HLR 3D composite kept its custom sky gradient background (did not overwrite with white)
            byte[] hlrPixels = ReadBgra32Pixels(hlrComposite, out int hlrW, out _);
            Assert.AreEqual(320, hlrW);
            AssertTopLeftIsNotPureWhite(hlrPixels, "Preserved HLR view must retain its existing background when PreserveVisualStyle is enabled.");

            // 4. Individual frames retention: only <HHmm>.png frames exist, zero reference passes
            foreach (string viewLabel in new[] { "Visual Shaded 3D", "Visual Wireframe 3D", "Visual HLR CustomBg 3D" })
            {
                string frameFolder = GetTempFilePath($"{viewLabel} - {dateLabel}");
                Assert.IsTrue(Directory.Exists(frameFolder));
                string[] frameFiles = Directory.GetFiles(frameFolder, "*.png");
                Assert.AreEqual(schedule.Frames.Count, frameFiles.Length);
                foreach (var slot in schedule.Frames)
                {
                    string expectedFramePath = Path.Combine(frameFolder, $"{viewLabel} - {dateLabel} - {slot.FrameFileStem}.png");
                    Assert.IsTrue(File.Exists(expectedFramePath), $"Missing expected Shadow Frame '{expectedFramePath}'.");
                }
            }
            Assert.IsFalse(Directory.EnumerateFiles(CurrentTestTempDirectory, "r_*", SearchOption.AllDirectories).Any());
            Assert.IsFalse(Directory.EnumerateFiles(CurrentTestTempDirectory, "ref*", SearchOption.AllDirectories).Any());

            // 5. Rolled-back TransactionGroup guarantees zero permanent model / view / Undo mutations
            Assert.IsFalse(doc.IsModified, "Export must roll back cleanly without dirtying document or Undo stack.");
            Assert.AreEqual(elementCountBefore, new FilteredElementCollector(doc).GetElementCount());
            for (int i = 0; i < targetViews.Length; i++)
            {
                AssertSnapshotUnchanged(snapshotsBefore[i], targetViews[i]);
            }
        }

        [Test]
        public void Export_PreserveVisualStyleFalse_ForcesShaded3DToHiddenLineOnPureWhiteAndRollsBack()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            VisualStyleSuite suite = SetupVisualStyleTestViews(doc);

            string cleanRvtPath = GetTempFilePath("CleanHlrBaseline.rvt");
            doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified);

            var adapter = new RevitShadowStudyAdapter(doc, null, UiApp);
            ShadowStudySiteInfo site = adapter.GetSiteInfo();
            DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                site,
                stepMinutes: 240,
                minAltitudeDegrees: 15.0,
                maxCompositeDarknessPercent: 80);

            IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();
            ShadowStudyViewItem shadedItem = eligible.First(v => v.Id == suite.Shaded3D.UniqueId);
            VisualSnapshot before = CaptureSnapshot(suite.Shaded3D);

            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { shadedItem },
                DateSchedules = new[] { schedule },
                OutputFolder = CurrentTestTempDirectory,
                PixelWidth = 320,
                ForceHiddenLine = true,
                ForceWhiteBackground = true,
                KeepIndividualFrames = false
            });

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(1, summary.GeneratedCount);

            byte[] hlrForcedPixels = ReadBgra32Pixels(summary.PrimaryCompositePath, out int w, out _);
            Assert.AreEqual(320, w);
            AssertTopLeftIsPureWhite(hlrForcedPixels, "When PreserveVisualStyle is unchecked, shaded 3D view must export on pure white background.");

            Assert.IsFalse(doc.IsModified);
            AssertSnapshotUnchanged(before, suite.Shaded3D);
        }

        private static VisualStyleSuite SetupVisualStyleTestViews(Document doc)
        {
            using (var tx = new Transaction(doc, "Setup Visual Style Test Views"))
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

                if (existingWall == null)
                {
                    WallType wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                    Wall.Create(doc, Line.CreateBound(new XYZ(20, 0, 0), new XYZ(20, 15, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                }

                ViewFamilyType threeDType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .First(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

                var sky = new Color(40, 110, 200);
                var horizon = new Color(170, 200, 235);
                var ground = new Color(150, 125, 95);

                View3D shaded3D = View3D.CreateIsometric(doc, threeDType.Id);
                shaded3D.Name = "Visual Shaded 3D";
                if (shaded3D.ViewTemplateId != ElementId.InvalidElementId)
                {
                    shaded3D.ViewTemplateId = ElementId.InvalidElementId;
                }
                shaded3D.CropBoxActive = true;
                shaded3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
                shaded3D.SetBackground(ViewDisplayBackground.CreateGradient(sky, horizon, ground));

                // Apply explicit colored surface override on walls in shaded3D so shaded geometry always produces color pixels
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

                View3D wireframe3D = View3D.CreateIsometric(doc, threeDType.Id);
                wireframe3D.Name = "Visual Wireframe 3D";
                if (wireframe3D.ViewTemplateId != ElementId.InvalidElementId)
                {
                    wireframe3D.ViewTemplateId = ElementId.InvalidElementId;
                }
                wireframe3D.CropBoxActive = false;
                wireframe3D.DisplayStyle = DisplayStyle.Wireframe;
                wireframe3D.SetBackground(ViewDisplayBackground.CreateGradient(sky, horizon, ground));

                View3D hlrCustomBg3D = View3D.CreateIsometric(doc, threeDType.Id);
                hlrCustomBg3D.Name = "Visual HLR CustomBg 3D";
                if (hlrCustomBg3D.ViewTemplateId != ElementId.InvalidElementId)
                {
                    hlrCustomBg3D.ViewTemplateId = ElementId.InvalidElementId;
                }
                hlrCustomBg3D.CropBoxActive = true;
                hlrCustomBg3D.DisplayStyle = DisplayStyle.HLR;
                hlrCustomBg3D.SetBackground(ViewDisplayBackground.CreateGradient(sky, horizon, ground));

                tx.Commit();

                return new VisualStyleSuite
                {
                    Shaded3D = shaded3D,
                    Wireframe3D = wireframe3D,
                    HlrCustomBg3D = hlrCustomBg3D
                };
            }
        }

        private static VisualSnapshot CaptureSnapshot(View view)
        {
            ViewDisplayBackground bg = view.GetBackground();
            return new VisualSnapshot
            {
                ViewTemplateId = view.ViewTemplateId,
                DisplayStyle = view.DisplayStyle,
                ShadowIntensity = view.ShadowIntensity,
                CropBoxActive = view.CropBoxActive,
                BackgroundType = bg.Type,
                SkyColor = bg.SkyColor,
                GroundColor = bg.GroundColor
            };
        }

        private static void AssertSnapshotUnchanged(VisualSnapshot before, View view)
        {
            ViewDisplayBackground afterBg = view.GetBackground();
            Assert.AreEqual(before.ViewTemplateId, view.ViewTemplateId, $"ViewTemplateId changed on {view.Name}");
            Assert.AreEqual(before.DisplayStyle, view.DisplayStyle, $"DisplayStyle changed on {view.Name}");
            Assert.AreEqual(before.ShadowIntensity, view.ShadowIntensity, $"ShadowIntensity changed on {view.Name}");
            Assert.AreEqual(before.CropBoxActive, view.CropBoxActive, $"CropBoxActive changed on {view.Name}");
            Assert.AreEqual(before.BackgroundType, afterBg.Type, $"Background Type changed on {view.Name}");
            if (before.SkyColor != null && afterBg.SkyColor != null)
            {
                Assert.AreEqual(before.SkyColor.Red, afterBg.SkyColor.Red, $"SkyColor.Red changed on {view.Name}");
                Assert.AreEqual(before.SkyColor.Green, afterBg.SkyColor.Green, $"SkyColor.Green changed on {view.Name}");
                Assert.AreEqual(before.SkyColor.Blue, afterBg.SkyColor.Blue, $"SkyColor.Blue changed on {view.Name}");
            }
        }

        private static byte[] ReadBgra32Pixels(string path, out int width, out int height)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                width = converted.PixelWidth;
                height = converted.PixelHeight;
                byte[] pixels = new byte[width * height * 4];
                converted.CopyPixels(pixels, width * 4, 0);
                return pixels;
            }
        }

        private static bool HasNonGrayscaleColorPixel(byte[] pixels)
        {
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

        private static void AssertTopLeftIsPureWhite(byte[] pixels, string message)
        {
            byte b = pixels[0];
            byte g = pixels[1];
            byte r = pixels[2];
            Assert.That(b, Is.GreaterThanOrEqualTo(250), message);
            Assert.That(g, Is.GreaterThanOrEqualTo(250), message);
            Assert.That(r, Is.GreaterThanOrEqualTo(250), message);
        }

        private static void AssertTopLeftIsNotPureWhite(byte[] pixels, string message)
        {
            byte b = pixels[0];
            byte g = pixels[1];
            byte r = pixels[2];
            bool isNearWhite = b >= 245 && g >= 245 && r >= 245;
            Assert.IsFalse(isNearWhite, $"{message} Top-left pixel was RGB({r},{g},{b}).");
        }

        private sealed class VisualStyleSuite
        {
            public View3D Shaded3D { get; set; } = null!;
            public View3D Wireframe3D { get; set; } = null!;
            public View3D HlrCustomBg3D { get; set; } = null!;
        }

        private sealed class VisualSnapshot
        {
            public ElementId ViewTemplateId { get; set; } = ElementId.InvalidElementId;
            public DisplayStyle DisplayStyle { get; set; }
            public int ShadowIntensity { get; set; }
            public bool CropBoxActive { get; set; }
            public ViewDisplayBackgroundType BackgroundType { get; set; }
            public Color? SkyColor { get; set; }
            public Color? GroundColor { get; set; }
        }
    }
}
