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
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #154 (multi-date batch export with
    /// hemisphere-aware built-in astronomical DateLabels inside a single rolled-back TransactionGroup).
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class ShadowStudyMultiDateRevitTests : RevitIsolatedDocumentFixture
    {
        [Test]
        public void Pipeline_MultiDateBatch_ExportsSummerAndWinterCompositesInsideSingleRolledBackTransactionGroup()
        {
            Document doc = OpenAndSaveCleanTempDocument(out View activePlan);

            Assert.IsFalse(doc.IsModified, "Saved temp document must start clean (IsModified == false).");

            int elementsBefore = new FilteredElementCollector(doc).GetElementCount();
            ElementId templateBefore = activePlan.ViewTemplateId;
            DisplayStyle styleBefore = activePlan.DisplayStyle;
            int shadowIntensityBefore = activePlan.ShadowIntensity;
            bool cropBefore = activePlan.CropBoxActive;

            var adapter = new RevitShadowStudyAdapter(doc, null, UiApp);
            ShadowStudyViewItem viewItem = adapter.GetEligibleViews().First(v => v.Id == activePlan.UniqueId);
            ShadowStudySiteInfo site = adapter.GetSiteInfo();

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
                stepMinutes: 180,
                minAltitudeDegrees: 10.0,
                maxCompositeDarknessPercent: 80);

            DateSolarSchedule winterSchedule = SolarScheduleCalculator.BuildSchedule(
                winterEntry,
                site,
                stepMinutes: 180,
                minAltitudeDegrees: 10.0,
                maxCompositeDarknessPercent: 80);

            Assert.IsTrue(summerSchedule.HasValidFrames, "Summer solstice must yield valid daylight frames.");
            Assert.IsTrue(winterSchedule.HasValidFrames, "Winter solstice must yield valid daylight frames.");

            string outputDir = GetTempFilePath("multi_date_output");
            var pipeline = new ShadowStudyPipeline(adapter);
            ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
            {
                Views = new[] { viewItem },
                DateSchedules = new[] { summerSchedule, winterSchedule },
                OutputFolder = outputDir,
                PixelWidth = 360,
                KeepIndividualFrames = false
            });

            Assert.IsFalse(summary.WasCancelled);
            Assert.AreEqual(2, summary.GeneratedCount);

            string expectedSummerPath = Path.Combine(
                outputDir,
                ShadowStudyPathHelper.BuildCompositeFileName(viewItem.ViewLabel, summerEntry.DateLabel));
            string expectedWinterPath = Path.Combine(
                outputDir,
                ShadowStudyPathHelper.BuildCompositeFileName(viewItem.ViewLabel, winterEntry.DateLabel));

            Assert.IsTrue(File.Exists(expectedSummerPath), $"Missing summer composite: {expectedSummerPath}");
            Assert.IsTrue(File.Exists(expectedWinterPath), $"Missing winter composite: {expectedWinterPath}");
            CollectionAssert.AreEquivalent(
                new[] { expectedSummerPath, expectedWinterPath },
                summary.CompositePaths.ToArray());

            GetPngDimensions(expectedSummerPath, out int sw, out int sh);
            GetPngDimensions(expectedWinterPath, out int ww, out int wh);
            Assert.AreEqual(360, sw);
            Assert.AreEqual(360, ww);
            Assert.AreEqual(sh, wh);

            // Verify zero document or Undo mutations
            Assert.IsFalse(doc.IsModified, "Multi-date Shadow Study Export must never modify the Revit document or Undo history.");
            Assert.AreEqual(elementsBefore, new FilteredElementCollector(doc).GetElementCount());
            Assert.AreEqual(templateBefore, activePlan.ViewTemplateId);
            Assert.AreEqual(styleBefore, activePlan.DisplayStyle);
            Assert.AreEqual(shadowIntensityBefore, activePlan.ShadowIntensity);
            Assert.AreEqual(cropBefore, activePlan.CropBoxActive);
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

            string cleanRvtPath = GetTempFilePath($"CleanShadowMultiDate_{App.VersionNumber}.rvt");
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
    }
}
