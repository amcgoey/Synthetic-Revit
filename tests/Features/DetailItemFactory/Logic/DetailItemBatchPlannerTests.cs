using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Services;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.DetailItemFactory
{
    [TestFixture]
    [UnitTest]
    public class DetailItemBatchPlannerTests
    {
        [TestCase("NormalName", "NormalName")]
        [TestCase("Door:36\"x84\"", "Door_36_x84_")]
        [TestCase("A/B\\C*D?E<F>G|H", "A_B_C_D_E_F_G_H")]
        [TestCase("TrailingDot.", "TrailingDot")]
        [TestCase("TrailingDotsAndSpaces. . ", "TrailingDotsAndSpaces")]
        [TestCase("  LeadingAndTrailing  ", "LeadingAndTrailing")]
        [TestCase("...", "Fallback")]
        [TestCase("   ", "Fallback")]
        [TestCase(null, "Fallback")]
        [TestCase("CON", "_CON")]
        [TestCase("prn", "_prn")]
        [TestCase("AUX", "_AUX")]
        [TestCase("nul", "_nul")]
        [TestCase("COM1", "_COM1")]
        [TestCase("lpt9", "_lpt9")]
        [TestCase("CON.txt", "_CON.txt")]
        [TestCase("CON.", "_CON")]
        public void SanitizeFileNameSegment_HandlesInvalidCharsTrailingDotsSpacesAndReservedDeviceNames(
            string? input,
            string expected)
        {
            string actual = DetailItemBatchPlanner.SanitizeFileNameSegment(input, "Fallback");
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void BuildTargetFileName_AssemblesStandardizedDetailItemName()
        {
            string fileName = DetailItemBatchPlanner.BuildTargetFileName(
                "Single/Flush.",
                "36\" x 84\"",
                DetailItemOrientations.ElevFront);

            Assert.AreEqual("2D - Single_Flush - 36_ x 84_ - Elev Front.rfa", fileName);
        }

        [TestCase(DetailItemSourceViewKind.Plan, false, null, DetailItemOrientations.Plan)]
        [TestCase(DetailItemSourceViewKind.ElevationOrSection, false, null, DetailItemOrientations.ElevFront)]
        [TestCase(DetailItemSourceViewKind.Other, false, null, DetailItemOrientations.Plan)]
        [TestCase(DetailItemSourceViewKind.Legend, false, "Front", DetailItemOrientations.Plan)]
        [TestCase(DetailItemSourceViewKind.Legend, true, null, DetailItemOrientations.Plan)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Floor Plan", DetailItemOrientations.Plan)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Front", DetailItemOrientations.ElevFront)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Back", DetailItemOrientations.ElevBack)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Left", DetailItemOrientations.ElevSide)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Right", DetailItemOrientations.ElevSide)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Side", DetailItemOrientations.ElevSide)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Top", DetailItemOrientations.ElevTop)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Elevation : Bottom", DetailItemOrientations.ElevBottom)]
        [TestCase(DetailItemSourceViewKind.Legend, true, "Section", DetailItemOrientations.ElevSection)]
        public void ResolveDefaultOrientation_ResolvesPlanSectionElevationAndLegendDefaults(
            DetailItemSourceViewKind viewKind,
            bool isLegendComponent,
            string? legendViewValue,
            string expectedOrientation)
        {
            string actual = DetailItemBatchPlanner.ResolveDefaultOrientation(
                viewKind,
                isLegendComponent,
                legendViewValue);

            Assert.AreEqual(expectedOrientation, actual);
        }

        [Test]
        public void BuildPlan_ClassifiesExistingFilesAsSkippedWhenOverwriteDisabled_AndExecutesWhenEnabled()
        {
            string outputFolder = @"C:\Output\DetailItems";
            string existingTarget = Path.Combine(outputFolder, "2D - Door - 36 - Plan.rfa");

            var selections = new[]
            {
                new DetailItemElementSelection("u-1", "101", "Doors - Door - 36", "Door", "36", DetailItemOrientations.Plan),
                new DetailItemElementSelection("u-2", "102", "Windows - Casement - 24", "Casement", "24", DetailItemOrientations.ElevFront)
            };

            var existingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { existingTarget };

            DetailItemBatchPlan skipPlan = DetailItemBatchPlanner.BuildPlan(
                "view-1",
                outputFolder,
                "Detail Items",
                overwriteExisting: false,
                selections,
                path => existingSet.Contains(path));

            Assert.AreEqual(1, skipPlan.ExecutableCount);
            Assert.AreEqual(1, skipPlan.SkippedCount);
            Assert.AreEqual(DetailItemPlanDisposition.SkipExisting, skipPlan.Items[0].Disposition);
            StringAssert.Contains("already exists", skipPlan.Items[0].SkipReason);
            Assert.AreEqual(DetailItemPlanDisposition.Execute, skipPlan.Items[1].Disposition);
            Assert.AreEqual(Path.Combine(outputFolder, "2D - Casement - 24 - Elev Front.rfa"), skipPlan.Items[1].TargetPath);

            DetailItemBatchPlan overwritePlan = DetailItemBatchPlanner.BuildPlan(
                "view-1",
                outputFolder,
                "Detail Items",
                overwriteExisting: true,
                selections,
                path => existingSet.Contains(path));

            Assert.AreEqual(2, overwritePlan.ExecutableCount);
            Assert.AreEqual(0, overwritePlan.SkippedCount);
            Assert.AreEqual(DetailItemPlanDisposition.Execute, overwritePlan.Items[0].Disposition);
            Assert.AreEqual(DetailItemPlanDisposition.Execute, overwritePlan.Items[1].Disposition);
        }

        [Test]
        public void BuildPlan_DeduplicatesCaseInsensitiveTargetRfaPaths_WhileAllowingDifferentOrientations()
        {
            string outputFolder = @"C:\Output\DetailItems";
            var selections = new[]
            {
                new DetailItemElementSelection("u-1", "101", "Doors - Single-Flush - 36", "Single-Flush", "36", DetailItemOrientations.Plan),
                new DetailItemElementSelection("u-2", "102", "Doors - single-flush - 36", "single-flush", "36", DetailItemOrientations.Plan),
                new DetailItemElementSelection("u-3", "103", "Doors - Single-Flush - 36", "Single-Flush", "36", DetailItemOrientations.ElevFront)
            };

            DetailItemBatchPlan plan = DetailItemBatchPlanner.BuildPlan(
                "view-1",
                outputFolder,
                "",
                overwriteExisting: true,
                selections,
                _ => false);

            Assert.AreEqual("Detail Items", plan.TargetSubcategory);
            Assert.AreEqual(3, plan.Items.Count);
            Assert.AreEqual(2, plan.ExecutableCount);
            Assert.AreEqual(1, plan.SkippedCount);

            Assert.AreEqual(DetailItemPlanDisposition.Execute, plan.Items[0].Disposition);
            Assert.AreEqual(DetailItemPlanDisposition.SkipDuplicate, plan.Items[1].Disposition);
            StringAssert.Contains("Duplicate", plan.Items[1].SkipReason);
            Assert.AreEqual(DetailItemPlanDisposition.Execute, plan.Items[2].Disposition);
        }

        [Test]
        public void CreateTempRunToken_GeneratesShortFixedWidthAlphanumericTokens()
        {
            string token1 = DetailItemBatchPlanner.CreateTempRunToken();
            string token2 = DetailItemBatchPlanner.CreateTempRunToken();

            Assert.AreEqual(DetailItemBatchPlanner.TempRunTokenLength, token1.Length);
            Assert.AreEqual(DetailItemBatchPlanner.TempRunTokenLength, token2.Length);
            Assert.AreNotEqual(token1, token2);
            foreach (char ch in token1)
            {
                Assert.IsTrue(char.IsLetterOrDigit(ch), $"Expected alphanumeric token char, got '{ch}'.");
            }
        }

        [Test]
        public void BuildTempExportPrefix_GeneratesFixedWidthUniquePrefixesAndValidatesArguments()
        {
            string prefix0 = DetailItemBatchPlanner.BuildTempExportPrefix("ab12cd34", 0);
            string prefix42 = DetailItemBatchPlanner.BuildTempExportPrefix("ab12cd34", 42);
            string sanitizedPrefix = DetailItemBatchPlanner.BuildTempExportPrefix("run:1/2*", 7);

            Assert.AreEqual("temp_export_ab12cd34_0000_", prefix0);
            Assert.AreEqual("temp_export_ab12cd34_0042_", prefix42);
            Assert.AreEqual(prefix0.Length, prefix42.Length);
            Assert.AreNotEqual(prefix0, prefix42);
            Assert.AreEqual("temp_export_run_1_2__0007_", sanitizedPrefix);

            Assert.Throws<ArgumentException>(() => DetailItemBatchPlanner.BuildTempExportPrefix("   ", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DetailItemBatchPlanner.BuildTempExportPrefix("ab12cd34", -1));
        }

        [Test]
        public void BuildPlan_DefaultsCleanupTempFilesToTrueAndPropagatesOptOut()
        {
            var selections = new[]
            {
                new DetailItemElementSelection("u-1", "101", "Doors - Door - 36", "Door", "36", DetailItemOrientations.Plan)
            };

            DetailItemBatchPlan defaultPlan = DetailItemBatchPlanner.BuildPlan(
                "view-1",
                @"C:\Output\DetailItems",
                "Detail Items",
                overwriteExisting: true,
                selections,
                _ => false);
            Assert.IsTrue(defaultPlan.CleanupTempFiles);

            DetailItemBatchPlan retainedPlan = DetailItemBatchPlanner.BuildPlan(
                "view-1",
                @"C:\Output\DetailItems",
                "Detail Items",
                overwriteExisting: true,
                selections,
                _ => false,
                cleanupTempFiles: false);
            Assert.IsFalse(retainedPlan.CleanupTempFiles);
        }

        [Test]
        public void CleanupTempExportFiles_PerPrefixAndDirectoryWideCleanup_DeletesOnlyTargetedTempFiles()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SynDifPlannerCleanup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string prefix0 = DetailItemBatchPlanner.BuildTempExportPrefix("run00001", 0);
                string prefix1 = DetailItemBatchPlanner.BuildTempExportPrefix("run00001", 1);
                string otherRunPrefix = DetailItemBatchPlanner.BuildTempExportPrefix("run00002", 0);

                string p0Dwg = Path.Combine(tempDir, prefix0 + "FloorPlan.dwg");
                string p0Pcp = Path.Combine(tempDir, prefix0 + "FloorPlan.pcp");
                string p1Dwg = Path.Combine(tempDir, prefix1 + "FloorPlan.dwg");
                string otherRunPcp = Path.Combine(tempDir, otherRunPrefix + "FloorPlan.pcp");
                string userDwg = Path.Combine(tempDir, "reference_plan.dwg");
                string userRfa = Path.Combine(tempDir, "2D - Door - 36 - Plan.rfa");

                File.WriteAllText(p0Dwg, "dwg0");
                File.WriteAllText(p0Pcp, "pcp0");
                File.WriteAllText(p1Dwg, "dwg1");
                File.WriteAllText(otherRunPcp, "pcp-other");
                File.WriteAllText(userDwg, "user-dwg");
                File.WriteAllText(userRfa, "user-rfa");

                DetailItemTempCleanupSummary perPrefixSummary = DetailItemBatchPlanner.CleanupTempExportFiles(tempDir, prefix0);
                Assert.IsTrue(perPrefixSummary.DirectoryExists);
                Assert.IsNull(perPrefixSummary.EnumerationError);
                Assert.AreEqual(2, perPrefixSummary.DeletedCount);
                Assert.AreEqual(0, perPrefixSummary.FailedCount);
                Assert.IsFalse(File.Exists(p0Dwg));
                Assert.IsFalse(File.Exists(p0Pcp));
                Assert.IsTrue(File.Exists(p1Dwg));
                Assert.IsTrue(File.Exists(otherRunPcp));
                Assert.IsTrue(File.Exists(userDwg));
                Assert.IsTrue(File.Exists(userRfa));

                DetailItemTempCleanupSummary directorySummary = DetailItemBatchPlanner.CleanupTempExportFiles(tempDir);
                Assert.IsTrue(directorySummary.DirectoryExists);
                Assert.AreEqual(2, directorySummary.DeletedCount);
                Assert.AreEqual(0, directorySummary.FailedCount);
                Assert.IsFalse(File.Exists(p1Dwg));
                Assert.IsFalse(File.Exists(otherRunPcp));
                Assert.IsTrue(File.Exists(userDwg));
                Assert.IsTrue(File.Exists(userRfa));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Test]
        public void CleanupTempExportFiles_LockedFileAndMissingDirectory_ToleratesIoErrorsWithoutThrowing()
        {
            string missingDir = Path.Combine(Path.GetTempPath(), "SynDifMissing_" + Guid.NewGuid().ToString("N"));
            DetailItemTempCleanupSummary missingSummary = DetailItemBatchPlanner.CleanupTempExportFiles(missingDir);
            Assert.IsFalse(missingSummary.DirectoryExists);
            Assert.AreEqual(0, missingSummary.DeletedCount);
            Assert.AreEqual(0, missingSummary.FailedCount);

            Assert.Throws<ArgumentException>(() => DetailItemBatchPlanner.CleanupTempExportFiles(missingDir, "reference_"));

            string tempDir = Path.Combine(Path.GetTempPath(), "SynDifLockedCleanup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string prefix = DetailItemBatchPlanner.BuildTempExportPrefix("lockrun1", 0);
                string lockedDwg = Path.Combine(tempDir, prefix + "locked.dwg");
                string unlockedPcp = Path.Combine(tempDir, prefix + "unlocked.pcp");
                File.WriteAllText(lockedDwg, "locked");
                File.WriteAllText(unlockedPcp, "unlocked");

                DetailItemTempCleanupSummary summary;
                using (new FileStream(lockedDwg, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    summary = DetailItemBatchPlanner.CleanupTempExportFiles(tempDir, prefix);
                }

                Assert.IsTrue(summary.DirectoryExists);
                Assert.AreEqual(1, summary.DeletedCount);
                Assert.AreEqual(1, summary.FailedCount);
                Assert.AreEqual(1, summary.Errors.Count);
                Assert.IsFalse(File.Exists(unlockedPcp));
                Assert.IsTrue(File.Exists(lockedDwg));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
