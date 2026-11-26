using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Adapters;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.Revit;
using Synthetic.Platform.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.DetailItemFactory
{
    /// <summary>
    /// Live isolated-document Revit integration tests verifying end-to-end execution of
    /// <see cref="RevitDetailItemFactoryAdapter"/>.
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class DetailItemFactoryExecutionRevitTests : RevitIsolatedDocumentFixture
    {
        private bool _previousSuppressUi;
        private bool _previousSuppressDiskFallback;

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            _previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            ProgressCoordinator.SuppressUI = true;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;
            SettingsManager.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            SettingsManager.ClearCache();
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = _previousSuppressDiskFallback;
        }

        [Test]
        public void ExecuteBatch_HappyPath_GeneratesSavesAndLoads2DDetailFamily_WithoutDocumentLeaks()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            Level? level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault();
            Assert.IsNotNull(level, "Test template must contain at least one Level.");

            ViewFamilyType? floorPlanType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.FloorPlan);
            Assert.IsNotNull(floorPlanType, "Test template must contain a FloorPlan ViewFamilyType.");

            WallType? wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind == WallKind.Basic)
                ?? new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault();
            Assert.IsNotNull(wallType, "Test template must contain at least one WallType.");

            ViewPlan? planView = null;
            ElementId wallId = ElementId.InvalidElementId;
            doc.ExecuteTransaction(
                "Create Sample Plan and Wall",
                FailurePipelines.ObserveOnly(),
                () =>
                {
                    planView = ViewPlan.Create(doc, floorPlanType!.Id, level!.Id);
                    planView.CropBoxActive = false;

                    Line wallLine = Line.CreateBound(new XYZ(500.0, 500.0, 0.0), new XYZ(510.0, 500.0, 0.0));
                    Wall wall = Wall.Create(doc, wallLine, wallType!.Id, level.Id, 10.0, 0.0, false, false);
                    wallId = wall.Id;
                });
            Assert.IsNotNull(planView);
            Assert.AreNotEqual(ElementId.InvalidElementId, wallId);

            string hostRvtPath = GetTempFilePath("DetailItemFactoryHost.rvt");
            doc.SaveAs(hostRvtPath, new SaveAsOptions { OverwriteExistingFile = true });

            int docsBeforeRun = App.Documents.Size;

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView!, new[] { wallId });
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            Assert.IsTrue(snapshot.IsActiveViewSupported);
            Assert.AreEqual(DetailItemSourceViewKind.Plan, snapshot.ActiveViewKind);
            Assert.AreEqual(
                Path.GetFullPath(CurrentTestTempDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(snapshot.DefaultOutputFolder).TrimEnd(Path.DirectorySeparatorChar));
            Assert.AreEqual(1, snapshot.Elements.Count);

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            vm.SelectedSubcategory = DetailItemFactorySettings.DefaultTargetSubcategory;
            vm.OverwriteExisting = true;
            DetailItemBatchPlan plan = vm.BuildBatchPlan();

            Assert.AreEqual(1, plan.ExecutableCount);
            Assert.AreEqual(0, plan.SkippedCount);

            var progressReports = new List<ProgressState>();
            DetailItemBatchOutcome outcome = adapter.ExecuteBatch(
                plan,
                new SynchronousProgress(progressReports),
                CancellationToken.None);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage ?? "Expected ExecuteBatch to succeed.");
            Assert.IsFalse(outcome.WasCancelled);
            Assert.IsNull(outcome.ErrorMessage);
            Assert.AreEqual(1, outcome.TotalCount);
            Assert.AreEqual(1, outcome.CommittedCount);
            Assert.AreEqual(0, outcome.FailedCount);
            Assert.AreEqual(0, outcome.SkippedCount);
            Assert.AreEqual(1, outcome.Results.Count);
            Assert.AreEqual(DetailItemResultStatus.Success, outcome.Results[0].Status, outcome.Results[0].Message);

            string expectedRfaPath = plan.Items[0].TargetPath;
            Assert.IsTrue(File.Exists(expectedRfaPath), $"Expected generated family file at '{expectedRfaPath}'.");

            string expectedFamilyName = Path.GetFileNameWithoutExtension(plan.Items[0].TargetFileName);
            bool familyLoaded = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Any(f => string.Equals(f.Name, expectedFamilyName, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(familyLoaded, $"Expected family '{expectedFamilyName}' to be loaded into the host document.");

            Assert.IsTrue(
                progressReports.Any(r => r.ProgressIndex == 1 && r.MaximumBounds == 1),
                "Expected 1-based ProgressIndex == 1 to be reported.");

            Assert.AreEqual(docsBeforeRun, App.Documents.Size, "ExecuteBatch must close and dispose transient family documents.");
        }

        private sealed class SynchronousProgress : IProgress<ProgressState>
        {
            private readonly IList<ProgressState> _reports;

            public SynchronousProgress(IList<ProgressState> reports)
            {
                _reports = reports;
            }

            public void Report(ProgressState value)
            {
                _reports.Add(value);
            }
        }
    }
}
