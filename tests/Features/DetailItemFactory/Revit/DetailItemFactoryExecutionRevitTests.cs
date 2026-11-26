using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Live isolated-document Revit integration tests verifying end-to-end execution,
    /// transient family-document cleanup, transaction integrity, and runtime guards of
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
        public void ExecuteBatch_HappyPath_GeneratesSavesAndLoads2DDetailFamily_OnRootCategory_WithoutDocumentLeaks()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 1);
            int docsBeforeRun = App.Documents.Size;

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
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
            Assert.IsTrue(vm.CleanupTempFiles, "CleanupTempFiles should default to true.");
            DetailItemBatchPlan plan = vm.BuildBatchPlan();

            Assert.AreEqual(1, plan.ExecutableCount);
            Assert.AreEqual(0, plan.SkippedCount);
            Assert.IsTrue(plan.CleanupTempFiles);

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

            Assert.IsTrue(doc.IsModified, "Loading a generated family must mark the host document modified.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after ExecuteBatch.");

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
            Assert.IsEmpty(
                Directory.GetFiles(CurrentTestTempDirectory, "temp_export_*"),
                "CleanupTempFiles == true must delete temporary .dwg and .pcp files on success.");

            // Verify curves trace directly onto root OST_DetailComponents without creating a nested "Detail Items" subcategory.
            Document generatedFamilyDoc = App.OpenDocumentFile(expectedRfaPath);
            try
            {
                Category? detailCategory = generatedFamilyDoc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
                Assert.IsNotNull(detailCategory, "Family must define OST_DetailComponents category.");
                Assert.IsFalse(
                    detailCategory!.SubCategories.Contains(DetailItemFactorySettings.DefaultTargetSubcategory),
                    "Selecting 'Detail Items' must not create a nested 'Detail Items' child subcategory.");

                List<DetailCurve> detailCurves = new FilteredElementCollector(generatedFamilyDoc)
                    .OfClass(typeof(CurveElement))
                    .OfType<DetailCurve>()
                    .ToList();
                Assert.IsNotEmpty(detailCurves, "Generated family must contain traced detail curves.");

                foreach (DetailCurve detailCurve in detailCurves)
                {
                    GraphicsStyle? style = detailCurve.LineStyle as GraphicsStyle;
                    Assert.IsNotNull(style, "DetailCurve LineStyle should be assigned.");
                    Assert.AreEqual(
                        detailCategory.Id,
                        style!.GraphicsStyleCategory.Id,
                        "DetailCurve should trace directly on root OST_DetailComponents.");
                }
            }
            finally
            {
                if (generatedFamilyDoc.IsValidObject)
                {
                    generatedFamilyDoc.Close(false);
                }
                generatedFamilyDoc.Dispose();
            }

            Assert.AreEqual(docsBeforeRun, App.Documents.Size, "Verification family document must be closed and disposed.");
        }

        [Test]
        public void ExecuteBatch_LockedTargetRfaWithOverwriteEnabled_FailsAtSaveAs_WithZeroLeakedDocuments()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 1);
            int docsBeforeRun = App.Documents.Size;

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            vm.OverwriteExisting = true;
            DetailItemBatchPlan plan = vm.BuildBatchPlan();

            string lockedTargetPath = plan.Items[0].TargetPath;
            byte[] sentinelBytes = Encoding.ASCII.GetBytes("LOCKED-RFA-SENTINEL");
            File.WriteAllBytes(lockedTargetPath, sentinelBytes);

            var progressReports = new List<ProgressState>();
            DetailItemBatchOutcome outcome;
            using (new FileStream(lockedTargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                outcome = adapter.ExecuteBatch(
                    plan,
                    new SynchronousProgress(progressReports),
                    CancellationToken.None);
            }

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsFalse(outcome.WasCancelled);
            Assert.AreEqual(1, outcome.TotalCount);
            Assert.AreEqual(0, outcome.CommittedCount);
            Assert.AreEqual(1, outcome.FailedCount);
            Assert.AreEqual(DetailItemResultStatus.Failed, outcome.Results[0].Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Results[0].Message));
            CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(lockedTargetPath));

            Assert.IsFalse(doc.IsModified, "Failed SaveAs with zero commits must leave !doc.IsModified.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after failed SaveAs.");
            Assert.AreEqual(docsBeforeRun, App.Documents.Size, "Transient familyDoc must be closed and disposed on SaveAs failure.");
            Assert.IsTrue(progressReports.Any(r => r.ProgressIndex == 1 && r.MaximumBounds == 1));
            Assert.IsEmpty(
                Directory.GetFiles(CurrentTestTempDirectory, "temp_export_*"),
                "CleanupTempFiles == true must delete temporary .dwg and .pcp files even after SaveAs failure.");
        }

        [Test]
        public void ExecuteBatch_CleanupTempFilesFalse_RetainsTempExportsOnDisk_AndManualCleanupRemovesThem()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 1);
            int docsBeforeRun = App.Documents.Size;

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            vm.OverwriteExisting = true;
            vm.CleanupTempFiles = false;
            DetailItemBatchPlan plan = vm.BuildBatchPlan();
            Assert.IsFalse(plan.CleanupTempFiles);

            DetailItemBatchOutcome outcome = adapter.ExecuteBatch(plan, null, CancellationToken.None);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage ?? "Expected ExecuteBatch to succeed.");
            Assert.AreEqual(1, outcome.CommittedCount);
            Assert.AreEqual(docsBeforeRun, App.Documents.Size);
            Assert.IsTrue(File.Exists(plan.Items[0].TargetPath));

            string[] retainedDwgs = Directory.GetFiles(CurrentTestTempDirectory, "temp_export_*.dwg");
            Assert.IsNotEmpty(
                retainedDwgs,
                "CleanupTempFiles == false must retain temp_export_*.dwg files on disk for inspection.");

            var resultsVm = new DetailItemFactoryResultsViewModel(
                outcome.OutputFolder,
                outcome.Results,
                userPromptService: null);
            resultsVm.DeleteTempDwgsCommand.Execute(null);

            Assert.IsEmpty(
                Directory.GetFiles(CurrentTestTempDirectory, "temp_export_*"),
                "Manual Delete Temp DWGs command must clean up all retained temp_export_* files.");
        }

        [Test]
        public void ExecuteBatch_ZeroCommitBatches_AllSkippedAndAllFailed_LeaveDocumentUnmodified()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 1);
            int docsBeforeRun = App.Documents.Size;

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            // 1. All-skipped: pre-existing target .rfa with OverwriteExisting = false
            var skipVm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            skipVm.OverwriteExisting = false;
            string existingTargetPath = skipVm.BuildBatchPlan().Items[0].TargetPath;
            byte[] sentinelBytes = Encoding.ASCII.GetBytes("EXISTING-RFA-SENTINEL");
            File.WriteAllBytes(existingTargetPath, sentinelBytes);

            DetailItemBatchPlan skipPlan = skipVm.BuildBatchPlan();
            Assert.AreEqual(0, skipPlan.ExecutableCount);
            Assert.AreEqual(1, skipPlan.SkippedCount);

            DetailItemBatchOutcome skipOutcome = adapter.ExecuteBatch(skipPlan, null, CancellationToken.None);
            Assert.IsTrue(skipOutcome.Succeeded);
            Assert.AreEqual(0, skipOutcome.CommittedCount);
            Assert.AreEqual(1, skipOutcome.SkippedCount);
            Assert.AreEqual(DetailItemResultStatus.Skipped, skipOutcome.Results[0].Status);
            Assert.IsFalse(doc.IsModified, "All-skipped batch must leave !doc.IsModified.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after all-skipped batch.");
            Assert.AreEqual(docsBeforeRun, App.Documents.Size);

            // 2. All-failed: locked target .rfa with OverwriteExisting = true
            var failVm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            failVm.OverwriteExisting = true;
            DetailItemBatchPlan failPlan = failVm.BuildBatchPlan();

            DetailItemBatchOutcome failOutcome;
            using (new FileStream(existingTargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                failOutcome = adapter.ExecuteBatch(failPlan, null, CancellationToken.None);
            }

            Assert.IsTrue(failOutcome.Succeeded);
            Assert.AreEqual(0, failOutcome.CommittedCount);
            Assert.AreEqual(1, failOutcome.FailedCount);
            Assert.AreEqual(DetailItemResultStatus.Failed, failOutcome.Results[0].Status);
            Assert.IsFalse(doc.IsModified, "All-failed batch must leave !doc.IsModified.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after all-failed batch.");
            Assert.AreEqual(docsBeforeRun, App.Documents.Size);
            CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(existingTargetPath));
        }

        [Test]
        public void ExecuteBatch_PartialSuccessBatch_LoadsValidFamiliesAndIsolatesFailingItem_WithoutDocumentLeaks()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 2);
            int docsBeforeRun = App.Documents.Size;
            int initialFamilyCount = CountLoadedFamilies(doc);

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            Assert.AreEqual(2, snapshot.Elements.Count);

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            vm.OverwriteExisting = true;
            DetailItemBatchPlan plan = vm.BuildBatchPlan();
            Assert.AreEqual(2, plan.ExecutableCount);

            string firstTargetPath = plan.Items[0].TargetPath;
            string secondTargetPath = plan.Items[1].TargetPath;
            Assert.AreNotEqual(firstTargetPath, secondTargetPath, "Two distinct WallTypes should map to distinct .rfa targets.");

            byte[] sentinelBytes = Encoding.ASCII.GetBytes("LOCKED-SECOND-RFA");
            File.WriteAllBytes(secondTargetPath, sentinelBytes);

            var progressReports = new List<ProgressState>();
            DetailItemBatchOutcome outcome;
            using (new FileStream(secondTargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                outcome = adapter.ExecuteBatch(
                    plan,
                    new SynchronousProgress(progressReports),
                    CancellationToken.None);
            }

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsFalse(outcome.WasCancelled);
            Assert.AreEqual(2, outcome.TotalCount);
            Assert.AreEqual(1, outcome.CommittedCount);
            Assert.AreEqual(1, outcome.FailedCount);
            Assert.AreEqual(0, outcome.SkippedCount);
            Assert.AreEqual(DetailItemResultStatus.Success, outcome.Results[0].Status, outcome.Results[0].Message);
            Assert.AreEqual(DetailItemResultStatus.Failed, outcome.Results[1].Status);

            Assert.IsTrue(doc.IsModified, "Partial-success batch must leave host document modified.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after partial-success batch.");
            Assert.AreEqual(initialFamilyCount + 1, CountLoadedFamilies(doc), "Only the valid family should be loaded.");
            Assert.IsTrue(File.Exists(firstTargetPath));
            CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(secondTargetPath));
            Assert.AreEqual(docsBeforeRun, App.Documents.Size, "Failing item must not leak transient family documents.");
        }

        [Test]
        public void ExecuteBatch_MidBatchCancellation_PreservesCommittedFamilyAndMarksRemainingSkipped()
        {
            (Document doc, ViewPlan planView, List<ElementId> wallIds) = CreateSavedHostWithWalls(wallCount: 2);
            int docsBeforeRun = App.Documents.Size;
            int initialFamilyCount = CountLoadedFamilies(doc);

            var adapter = new RevitDetailItemFactoryAdapter(doc, null, UiApp, planView, wallIds);
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            vm.OverwriteExisting = true;
            DetailItemBatchPlan plan = vm.BuildBatchPlan();
            Assert.AreEqual(2, plan.ExecutableCount);

            var progressReports = new List<ProgressState>();
            using var cts = new CancellationTokenSource();
            DetailItemBatchOutcome outcome = adapter.ExecuteBatch(
                plan,
                new SynchronousProgress(progressReports, report =>
                {
                    if (report.ProgressIndex >= 1 && !cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                }),
                cts.Token);

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsTrue(outcome.WasCancelled);
            Assert.AreEqual(2, outcome.TotalCount);
            Assert.AreEqual(1, outcome.CommittedCount);
            Assert.AreEqual(1, outcome.SkippedCount);
            Assert.AreEqual(DetailItemResultStatus.Success, outcome.Results[0].Status, outcome.Results[0].Message);
            Assert.AreEqual(DetailItemResultStatus.Skipped, outcome.Results[1].Status);
            Assert.AreEqual("Cancelled by user.", outcome.Results[1].Message);

            Assert.IsTrue(doc.IsModified, "Mid-batch cancellation after commit must preserve committed family.");
            Assert.IsFalse(doc.IsModifiable, "No transaction may remain open after mid-batch cancellation.");
            Assert.AreEqual(initialFamilyCount + 1, CountLoadedFamilies(doc));
            Assert.AreEqual(docsBeforeRun, App.Documents.Size);
        }

        private (Document Doc, ViewPlan PlanView, List<ElementId> WallIds) CreateSavedHostWithWalls(int wallCount)
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

            WallType? baseWallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind == WallKind.Basic)
                ?? new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault();
            Assert.IsNotNull(baseWallType, "Test template must contain at least one WallType.");

            ViewPlan? planView = null;
            var wallIds = new List<ElementId>();
            doc.ExecuteTransaction(
                "Create Sample Plan and Walls",
                FailurePipelines.ObserveOnly(),
                () =>
                {
                    planView = ViewPlan.Create(doc, floorPlanType!.Id, level!.Id);
                    planView.CropBoxActive = false;

                    for (int i = 0; i < wallCount; i++)
                    {
                        WallType wallType = i == 0
                            ? baseWallType!
                            : (WallType)baseWallType!.Duplicate($"{baseWallType.Name}_Variant_{i + 1}");

                        double yOffset = i * 20.0;
                        Line wallLine = Line.CreateBound(
                            new XYZ(500.0, 500.0 + yOffset, 0.0),
                            new XYZ(510.0, 500.0 + yOffset, 0.0));
                        Wall wall = Wall.Create(doc, wallLine, wallType.Id, level.Id, 10.0, 0.0, false, false);
                        wallIds.Add(wall.Id);
                    }
                });

            Assert.IsNotNull(planView);
            Assert.AreEqual(wallCount, wallIds.Count);

            string hostRvtPath = GetTempFilePath("DetailItemFactoryHost.rvt");
            doc.SaveAs(hostRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified, "Freshly saved host document should start unmodified.");

            return (doc, planView!, wallIds);
        }

        private static int CountLoadedFamilies(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .GetElementCount();
        }

        private sealed class SynchronousProgress : IProgress<ProgressState>
        {
            private readonly IList<ProgressState> _reports;
            private readonly Action<ProgressState>? _onReport;

            public SynchronousProgress(IList<ProgressState> reports, Action<ProgressState>? onReport = null)
            {
                _reports = reports;
                _onReport = onReport;
            }

            public void Report(ProgressState value)
            {
                _reports.Add(value);
                _onReport?.Invoke(value);
            }
        }
    }
}
