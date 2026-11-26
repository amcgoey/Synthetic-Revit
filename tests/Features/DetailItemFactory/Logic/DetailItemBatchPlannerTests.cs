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
    }
}
