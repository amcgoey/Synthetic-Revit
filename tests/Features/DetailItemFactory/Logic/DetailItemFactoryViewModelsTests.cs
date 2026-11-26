using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Synthetic.Features.DetailItemFactory.Models;
using Synthetic.Features.DetailItemFactory.Settings;
using Synthetic.Features.DetailItemFactory.ViewModels;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Features.DetailItemFactory
{
    [TestFixture]
    [UnitTest]
    public class DetailItemFactoryViewModelsTests
    {
        [Test]
        public void DetailItemFactoryViewModel_HydratesSettingsDefaultsSubcategoriesAndElements()
        {
            var snapshot = new DetailItemSessionSnapshot(
                activeViewUniqueId: "view-elev-1",
                activeViewName: "North Elevation",
                activeViewKind: DetailItemSourceViewKind.ElevationOrSection,
                isActiveViewSupported: true,
                defaultOutputFolder: @"C:\Fallback\MyDocs",
                settings: new DetailItemFactorySettings
                {
                    OutputPath = @"C:\Project\DetailItems",
                    TargetSubcategory = "heavy line"
                },
                availableSubcategories: new[] { "Thin Line", "Heavy Line" },
                elements: new[]
                {
                    new DetailItemSessionElement("u-1", "2001", "Doors", "Flush", "36x84"),
                    new DetailItemSessionElement("u-2", "2002", "Windows", "Fixed", "48x48")
                });

            var folderDialog = new FakeFolderDialogService { NextResult = @"D:\Picked\Folder" };
            var vm = new DetailItemFactoryViewModel(snapshot, folderDialog);

            Assert.AreEqual(@"C:\Project\DetailItems\", vm.OutputPath);
            Assert.AreEqual("Heavy Line", vm.SelectedSubcategory);
            Assert.IsTrue(vm.OverwriteExisting);
            Assert.AreEqual(2, vm.Elements.Count);
            Assert.AreEqual(DetailItemOrientations.ElevFront, vm.Elements[0].SelectedOrientation);
            CollectionAssert.AreEqual(new[] { "Thin Line", "Heavy Line", "Detail Items" }, vm.Subcategories);
            Assert.IsTrue(vm.RunCommand.CanExecute(null));

            vm.BrowseCommand.Execute(null);
            Assert.AreEqual(@"D:\Picked\Folder\", vm.OutputPath);
            Assert.AreEqual(1, folderDialog.Calls.Count);
        }

        [Test]
        public void DetailItemFactoryViewModel_RowRemovalUpdatesCanExecuteAndAvoidsFalseDeduplication()
        {
            var snapshot = new DetailItemSessionSnapshot(
                activeViewUniqueId: "view-plan-1",
                activeViewName: "Level 1",
                activeViewKind: DetailItemSourceViewKind.Plan,
                isActiveViewSupported: true,
                defaultOutputFolder: @"C:\Fallback\MyDocs",
                settings: new DetailItemFactorySettings().Defaults(),
                availableSubcategories: new[] { "Detail Items" },
                elements: new[]
                {
                    new DetailItemSessionElement("u-1", "101", "Doors", "Single-Flush", "36"),
                    new DetailItemSessionElement("u-2", "102", "Doors", "Single-Flush", "36")
                });

            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            Assert.AreEqual(@"C:\Fallback\MyDocs\", vm.OutputPath);
            Assert.AreEqual("Detail Items", vm.SelectedSubcategory);

            // Remove the first duplicate row in the UI before building the plan.
            vm.RemoveElementCommand.Execute(vm.Elements[0]);
            Assert.AreEqual(1, vm.Elements.Count);
            Assert.IsTrue(vm.RunCommand.CanExecute(null));

            DetailItemBatchPlan planAfterRemoval = vm.BuildBatchPlan(_ => false);
            Assert.AreEqual(1, planAfterRemoval.ExecutableCount);
            Assert.AreEqual(0, planAfterRemoval.SkippedCount);
            Assert.AreEqual("u-2", planAfterRemoval.Items[0].ElementUniqueId);

            // Removing the last element disables RunCommand.
            vm.RemoveElementCommand.Execute(vm.Elements[0]);
            Assert.AreEqual(0, vm.Elements.Count);
            Assert.IsFalse(vm.RunCommand.CanExecute(null));
        }

        [Test]
        public void DetailItemFactoryViewModel_RunAndCancelCommands_ValidatePathAndRaiseCloseEvents()
        {
            var snapshot = new FakeDetailItemFactoryAdapter().CaptureSessionSnapshot();
            var vm = new DetailItemFactoryViewModel(snapshot, folderDialogService: null);

            Assert.IsFalse(vm.BrowseCommand.CanExecute(null));

            var closeResults = new List<bool>();
            vm.RequestClose += result => closeResults.Add(result);

            vm.OutputPath = "relative\\invalid";
            Assert.IsFalse(vm.RunCommand.CanExecute(null));
            vm.RunCommand.Execute(null);
            Assert.IsEmpty(closeResults);

            vm.OutputPath = @"C:\Valid\Output";
            Assert.IsTrue(vm.RunCommand.CanExecute(null));
            vm.RunCommand.Execute(null);
            vm.CancelCommand.Execute(null);

            CollectionAssert.AreEqual(new[] { true, false }, closeResults);
        }

        [Test]
        public void FakeDetailItemFactoryAdapter_ReportsOneBasedProgressAndPreservesOutcomeStatuses()
        {
            var adapter = new FakeDetailItemFactoryAdapter();
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());

            // Duplicate item 0's family/type/orientation via custom plan and test mixed statuses
            DetailItemBatchPlan plan = vm.BuildBatchPlan(_ => false);
            var progressReports = new List<ProgressState>();
            var progress = new SynchronousProgress(progressReports);

            int callIndex = 0;
            adapter.ItemResultProvider = item =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    return new DetailItemResultItem
                    {
                        ElementName = item.DisplayName,
                        ElementId = item.ElementId,
                        Status = DetailItemResultStatus.PartialSuccess,
                        Message = "Skipped 1 microscopic curve."
                    };
                }

                return new DetailItemResultItem
                {
                    ElementName = item.DisplayName,
                    ElementId = item.ElementId,
                    Status = DetailItemResultStatus.Failed,
                    Message = "Simulated failure."
                };
            };

            DetailItemBatchOutcome outcome = adapter.ExecuteBatch(plan, progress, CancellationToken.None);

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsFalse(outcome.WasCancelled);
            Assert.AreEqual(2, outcome.TotalCount);
            Assert.AreEqual(1, outcome.PartialSuccessCount);
            Assert.AreEqual(1, outcome.FailedCount);
            Assert.AreEqual(1, outcome.CommittedCount);
            Assert.AreEqual(2, progressReports.Count);
            Assert.AreEqual(1, progressReports[0].ProgressIndex);
            Assert.AreEqual(2, progressReports[1].ProgressIndex);
            Assert.AreEqual(2, progressReports[1].MaximumBounds);
        }

        [Test]
        public void FakeDetailItemFactoryAdapter_HandlesCancellationWithSkippedResults()
        {
            var adapter = new FakeDetailItemFactoryAdapter();
            DetailItemSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            var vm = new DetailItemFactoryViewModel(snapshot, new FakeFolderDialogService());
            DetailItemBatchPlan plan = vm.BuildBatchPlan(_ => false);

            using var cts = new CancellationTokenSource();
            adapter.CancelAfterItemCount = 1;
            adapter.CancelSourceToTrigger = cts;

            var reports = new List<ProgressState>();
            DetailItemBatchOutcome outcome = adapter.ExecuteBatch(plan, new SynchronousProgress(reports), cts.Token);

            Assert.IsTrue(outcome.Succeeded);
            Assert.IsTrue(outcome.WasCancelled);
            Assert.AreEqual(1, outcome.SucceededCount);
            Assert.AreEqual(1, outcome.SkippedCount);
            Assert.AreEqual(DetailItemResultStatus.Skipped, outcome.Results[1].Status);
            Assert.AreEqual("Cancelled by user.", outcome.Results[1].Message);
            Assert.AreEqual(2, reports.Count);
            Assert.AreEqual(2, reports[1].ProgressIndex);
        }

        [Test]
        public void DetailItemFactoryResultsViewModel_DeletesTempExportsAndHandlesNullPromptService()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SynDifResultsTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string tempDwg = Path.Combine(tempDir, "temp_export_sample.dwg");
                string tempPcp = Path.Combine(tempDir, "temp_export_sample.pcp");
                string keepRfa = Path.Combine(tempDir, "2D - Door - 36 - Plan.rfa");
                string keepDwg = Path.Combine(tempDir, "reference_plan.dwg");
                File.WriteAllText(tempDwg, "dwg");
                File.WriteAllText(tempPcp, "pcp");
                File.WriteAllText(keepRfa, "rfa");
                File.WriteAllText(keepDwg, "keep");

                var results = new[]
                {
                    new DetailItemResultItem
                    {
                        ElementName = "Door - 36",
                        ElementId = "101",
                        Status = DetailItemResultStatus.Success,
                        Message = "OK"
                    },
                    new DetailItemResultItem
                    {
                        ElementName = "Wall - 8",
                        ElementId = "102",
                        Status = DetailItemResultStatus.PartialSuccess,
                        Message = "Partial"
                    }
                };

                var prompt = new FakeUserPromptService();
                var vm = new DetailItemFactoryResultsViewModel(tempDir, results, prompt);
                Assert.AreEqual(2, vm.Results.Count);

                vm.DeleteTempDwgsCommand.Execute(null);
                Assert.IsFalse(File.Exists(tempDwg));
                Assert.IsFalse(File.Exists(tempPcp));
                Assert.IsTrue(File.Exists(keepRfa));
                Assert.IsTrue(File.Exists(keepDwg));
                Assert.AreEqual(1, prompt.ShownMessages.Count);
                StringAssert.Contains("deleted 2 temporary CAD files", prompt.ShownMessages[0]);

                bool closed = false;
                vm.RequestClose += () => closed = true;
                vm.CloseCommand.Execute(null);
                Assert.IsTrue(closed);

                // Null IUserPromptService must not throw even when folder is missing.
                var headlessVm = new DetailItemFactoryResultsViewModel(
                    Path.Combine(tempDir, "MissingSubfolder"),
                    results,
                    userPromptService: null);
                Assert.DoesNotThrow(() => headlessVm.DeleteTempDwgsCommand.Execute(null));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
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
