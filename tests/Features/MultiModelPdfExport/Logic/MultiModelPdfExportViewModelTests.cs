using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.ViewModels;
using SyntheticTests;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    [TestFixture]
    [UnitTest]
    public class MultiModelPdfExportViewModelTests
    {
        [Test]
        public void Constructor_DefaultsActiveChecked_FirstRunPrintSetPlaceholder_AndDefaultPdfSetup()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            var folderDialog = new FakeFolderDialogService();

            var vm = new MultiModelPdfExportViewModel(adapter, folderDialog);

            Assert.AreEqual("HostModel.rvt", vm.ActiveDocumentTitle);
            Assert.AreEqual(1, vm.Rows.Count);
            MultiModelPdfExportRowViewModel row = vm.Rows[0];
            Assert.IsTrue(row.IsEligible);
            Assert.IsTrue(row.IsChecked, "Active project with Print Sets must default to checked.");
            Assert.IsNull(row.SelectedPrintSetName, "First-run Print Set must default safely to <Select Print Set...>.");
            Assert.AreEqual("<Select Print Set...>", row.SelectedPrintSetOption.DisplayName);
            Assert.IsTrue(row.HasMissingPrintSet);
            Assert.IsTrue(row.HasRowError);

            Assert.IsNotNull(vm.SelectedPdfSetup);
            Assert.IsTrue(vm.SelectedPdfSetup!.IsDefault);
            Assert.AreEqual(MultiModelPdfSetupOption.DefaultSetupDisplayName, vm.SelectedPdfSetup.DisplayName);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, vm.FileMode);
            Assert.IsFalse(vm.CanExport);
            Assert.AreEqual("Export PDFs", vm.ExportButtonText);
        }

        [Test]
        public void Constructor_OrdersDocumentsActiveThenLinkedThenOpen_AndDisablesZeroSetRows()
        {
            var openDoc = new MultiModelPdfDocumentSnapshot(
                "open-1", "OpenRef.rvt", @"C:\BIM\OpenRef.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var zeroSetLink = new MultiModelPdfDocumentSnapshot(
                "link-empty", "SiteContext.rvt", @"C:\BIM\SiteContext.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Workshared,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var unloadedLink = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "UnloadedLink.rvt", @"C:\BIM\UnloadedLink.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active-1", "Main.rvt", @"C:\BIM\Main.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Main.rvt",
                new[] { openDoc, zeroSetLink, unloadedLink, activeDoc },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            CollectionAssert.AreEqual(
                new[] { "Main.rvt", "SiteContext.rvt", "UnloadedLink.rvt", "OpenRef.rvt" },
                vm.Rows.Select(r => r.DocumentTitle).ToArray());

            Assert.IsTrue(vm.Rows[0].IsChecked);
            Assert.IsFalse(vm.Rows[1].IsEligible);
            Assert.IsFalse(vm.Rows[1].IsChecked);
            Assert.AreEqual("No saved Print Sets", vm.Rows[1].DisabledStatusText);

            // Attempting to check an ineligible 0-set row must remain false
            vm.Rows[1].IsChecked = true;
            Assert.IsFalse(vm.Rows[1].IsChecked);

            Assert.IsFalse(vm.Rows[2].IsEligible);
            Assert.AreEqual("Unloaded link — load in host to inspect", vm.Rows[2].DisabledStatusText);

            Assert.IsTrue(vm.Rows[3].IsEligible);
            Assert.IsFalse(vm.Rows[3].IsChecked, "OpenStandalone tabs must start unchecked by default.");
        }

        [Test]
        public void ZeroCheckedProjects_SuppressesNoProjectsFromRedErrorBanner_AndShowsCalmFooter()
        {
            var adapter = new FakeMultiModelPdfExportAdapter();
            var vm = new MultiModelPdfExportViewModel(adapter, new FakeFolderDialogService())
            {
                OutputFolder = @"C:\Exports\ProjectA"
            };

            vm.Rows[0].IsChecked = false;

            Assert.AreEqual(0, vm.CheckedCount);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.NoProjects));
            Assert.IsFalse(vm.HasErrorBanner, "NO_PROJECTS must be suppressed from the red error banner.");
            Assert.IsFalse(vm.CanExport);
            Assert.AreEqual("Select at least one project to export", vm.FooterStatusText);
            Assert.AreEqual("Export PDFs", vm.ExportButtonText);
        }

        [Test]
        public void ZeroSavedSetsOnActiveModel_WithEmptyFolder_OpensCalmWithoutRedErrorBanner()
        {
            var snapshot = FakeMultiModelPdfExportAdapter.CreateDefaultActiveSnapshot(includePrintSets: false);
            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            Assert.AreEqual(0, vm.CheckedCount);
            Assert.IsFalse(vm.HasErrorBanner);
            Assert.IsFalse(vm.ShowFolderErrorBorder);
            Assert.IsFalse(vm.CanExport);
            Assert.AreEqual("Select at least one project to export", vm.FooterStatusText);
        }

        [Test]
        public void BlockingValidationStates_SurfaceMissingSetZeroViewsInvalidFolderAndLongPath()
        {
            var snapshot = new MultiModelPdfSessionSnapshot(
                "Host.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-host",
                        "Host.rvt",
                        @"C:\BIM\Host.rvt",
                        MultiModelPdfDocumentOrigin.Active,
                        MultiModelPdfStorageKind.Workshared,
                        printSets: new[]
                        {
                            new MultiModelPdfSetSnapshot("Empty Set", 0, new[] { new MultiModelPdfSkippedViewInfo("A900", "Placeholder", "Placeholder sheet") }),
                            new MultiModelPdfSetSnapshot("CD Set", 3)
                        })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService())
            {
                OutputFolder = @"C:\Exports"
            };

            // 1. Missing Print Set
            Assert.IsTrue(vm.Rows[0].HasMissingPrintSet);
            Assert.IsTrue(vm.HasErrorBanner);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.MissingPrintSet));
            Assert.IsFalse(vm.CanExport);

            // 2. Empty Print Set (0 printable views)
            vm.Rows[0].SelectedPrintSetName = "Empty Set";
            Assert.IsFalse(vm.Rows[0].HasMissingPrintSet);
            Assert.IsTrue(vm.Rows[0].HasZeroPrintableViews);
            Assert.IsTrue(vm.HasErrorBanner);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.EmptyPrintSet));
            Assert.IsFalse(vm.CanExport);

            // 3. Invalid Output Folder
            vm.Rows[0].SelectedPrintSetName = "CD Set";
            vm.OutputFolder = "relative\\path";
            Assert.IsTrue(vm.HasErrorBanner);
            Assert.IsTrue(vm.ShowFolderErrorBorder);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.InvalidFolder));
            Assert.IsFalse(vm.CanExport);

            // 4. Path >= 255 characters
            vm.OutputFolder = @"C:\Exports\" + new string('a', 238);
            Assert.IsTrue(vm.HasErrorBanner);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.PathTooLong));
            Assert.IsFalse(vm.CanExport);

            // 5. Valid configuration enables export with exact singular button text
            vm.OutputFolder = @"C:\Exports\Deliverables";
            Assert.IsFalse(vm.HasErrorBanner);
            Assert.IsFalse(vm.ShowFolderErrorBorder);
            Assert.IsEmpty(vm.ValidationErrors);
            Assert.IsTrue(vm.CanExport);
            Assert.AreEqual("Export 1 PDF", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host.pdf", vm.OutputPreviewText);
        }

        [Test]
        public void DynamicButtonCount_BrowseFolder_CancelAndBuildRunRequest_BehaveAsSpecified()
        {
            var snapshot = new MultiModelPdfSessionSnapshot(
                "Host.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-host", "Host:One.rvt", @"C:\BIM\HostOne.rvt",
                        MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) }),
                    new MultiModelPdfDocumentSnapshot(
                        "open-second", "SecondModel.rvt", @"C:\BIM\SecondModel.rvt",
                        MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 5) })
                },
                new[]
                {
                    MultiModelPdfSetupOption.CreateDefault(),
                    new MultiModelPdfSetupOption("INC_FullSize", "INC_FullSize", isDefault: false)
                });

            var folderDialog = new FakeFolderDialogService { NextResult = @"C:\Exports\Run1" };
            var vm = new MultiModelPdfExportViewModel(new FakeMultiModelPdfExportAdapter(snapshot), folderDialog);

            bool? closedWith = null;
            vm.RequestClose += result => closedWith = result;

            // Cancel closes false with zero BuildRunRequest side effects
            vm.CancelCommand.Execute(null);
            Assert.AreEqual(false, closedWith);

            // Browse populates OutputFolder
            vm.BrowseOutputFolderCommand.Execute(null);
            Assert.AreEqual(@"C:\Exports\Run1", vm.OutputFolder);

            vm.Rows[0].SelectedPrintSetName = "CD Set";
            vm.SelectedPdfSetup = vm.PdfSetups.First(s => s.Id == "INC_FullSize");
            Assert.AreEqual("Export 1 PDF", vm.ExportButtonText);

            vm.Rows[1].SelectedPrintSetName = "Permit Set";
            vm.Rows[1].IsChecked = true;
            Assert.AreEqual("Export 2 PDFs", vm.ExportButtonText);
            Assert.IsTrue(vm.ExportCommand.CanExecute(null));

            vm.ExportCommand.Execute(null);
            Assert.AreEqual(true, closedWith);

            MultiModelPdfRunRequest request = vm.BuildRunRequest();
            Assert.AreEqual(@"C:\Exports\Run1", request.OutputFolder);
            Assert.AreEqual("INC_FullSize", request.PdfSetupId);
            Assert.AreEqual(MultiModelPdfFileMode.CombinedPerDocument, request.FileMode);
            Assert.AreEqual(2, request.Jobs.Count);
            Assert.AreEqual("Host_One", request.Jobs[0].OutputFileToken);
            Assert.AreEqual("CD Set", request.Jobs[0].PrintSetName);
            Assert.AreEqual("SecondModel", request.Jobs[1].OutputFileToken);
            Assert.AreEqual("Permit Set", request.Jobs[1].PrintSetName);
        }
    }
}
