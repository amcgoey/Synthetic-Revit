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

        [Test]
        public void SeparateMode_SwitchesLiveOutputPreviewButtonCountValidationAndRunRequest()
        {
            var snapshot = new MultiModelPdfSessionSnapshot(
                "Host.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-host", "Host.rvt", @"C:\BIM\Host.rvt",
                        MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                        printSets: new[]
                        {
                            new MultiModelPdfSetSnapshot("Single", 1),
                            new MultiModelPdfSetSnapshot("CD Set", 4)
                        }),
                    new MultiModelPdfDocumentSnapshot(
                        "open-second", "SecondModel.rvt", @"C:\BIM\SecondModel.rvt",
                        MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 3) })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService())
            {
                OutputFolder = @"C:\Exports\Deliverables"
            };

            // 1. Single view in Combined vs Separate -> both singular "Export 1 PDF"
            vm.Rows[0].SelectedPrintSetName = "Single";
            Assert.AreEqual("Export 1 PDF", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host.pdf", vm.OutputPreviewText);
            Assert.AreEqual(MultiModelPdfFooterStatusKind.Success, vm.FooterStatusKind);
            Assert.AreEqual("1 project · 1 view ready", vm.FooterStatusText);

            vm.FileMode = MultiModelPdfFileMode.SeparatePerViewSheet;
            Assert.AreEqual("Export 1 PDF", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host\<NamingRule>.pdf (1 PDF in 1 folder)", vm.OutputPreviewText);

            // 2. Multi-view single project switches from "Export 1 PDF" (Combined) to "Export 4 PDFs" (Separate)
            vm.Rows[0].SelectedPrintSetName = "CD Set";
            Assert.AreEqual("Export 4 PDFs", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host\<NamingRule>.pdf (4 PDFs in 1 folder)", vm.OutputPreviewText);

            vm.FileMode = MultiModelPdfFileMode.CombinedPerDocument;
            Assert.AreEqual("Export 1 PDF", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host.pdf", vm.OutputPreviewText);

            // 3. Multi-project count sums printable views in Separate mode and preserves Drive root preview
            vm.Rows[1].SelectedPrintSetName = "Permit Set";
            vm.Rows[1].IsChecked = true;
            Assert.AreEqual("Export 2 PDFs", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host.pdf (+1 more PDF)", vm.OutputPreviewText);

            vm.FileMode = MultiModelPdfFileMode.SeparatePerViewSheet;
            Assert.AreEqual("Export 7 PDFs", vm.ExportButtonText);
            Assert.AreEqual(@"C:\Exports\Deliverables\Host\<NamingRule>.pdf (7 PDFs in 2 folders)", vm.OutputPreviewText);

            vm.OutputFolder = @"C:\";
            Assert.AreEqual(@"C:\Host\<NamingRule>.pdf (7 PDFs in 2 folders)", vm.OutputPreviewText);

            // 4. Separate mode validates subfolder path length < 255
            vm.OutputFolder = @"C:\Exports\" + new string('a', 240);
            Assert.IsTrue(vm.HasErrorBanner);
            Assert.IsTrue(vm.ValidationErrors.Any(e => e.Code == MultiModelPdfValidationCode.PathTooLong));
            Assert.IsFalse(vm.CanExport);
            Assert.AreEqual("Export PDFs", vm.ExportButtonText);

            // 5. BuildRunRequest carries SeparatePerViewSheet into request and every job
            vm.OutputFolder = @"C:\Exports\Final";
            Assert.IsTrue(vm.CanExport);
            MultiModelPdfRunRequest request = vm.BuildRunRequest();
            Assert.AreEqual(MultiModelPdfFileMode.SeparatePerViewSheet, request.FileMode);
            Assert.IsTrue(request.Jobs.All(j => j.FileMode == MultiModelPdfFileMode.SeparatePerViewSheet));
        }

        [Test]
        public void PrintSetDropdownFormatting_AndSkippedViewBadgeTooltipTruncation_ProjectCorrectly()
        {
            var eightSkipped = Enumerable.Range(1, 8)
                .Select(i => new MultiModelPdfSkippedViewInfo($"A90{i}", $"PLACEHOLDER {i}", "Placeholder sheet"))
                .ToArray();

            var elevenSkipped = new[]
            {
                new MultiModelPdfSkippedViewInfo("A900", "UNPLACED SIGNAGE", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("FloorPlan", "Work-In-Progress Template", "View template"),
                new MultiModelPdfSkippedViewInfo("Schedule", "Door Schedule", "Schedule — not printable"),
                new MultiModelPdfSkippedViewInfo("A901", "SHEET 4", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A902", "SHEET 5", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A903", "SHEET 6", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A904", "SHEET 7", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A905", "SHEET 8", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A906", "OMITTED NINTH", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A907", "OMITTED TENTH", "Placeholder sheet"),
                new MultiModelPdfSkippedViewInfo("A908", "OMITTED ELEVENTH", "Not printable")
            };

            var snapshot = new MultiModelPdfSessionSnapshot(
                "HostModel.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-host",
                        "HostModel.rvt",
                        @"C:\BIM\HostModel.rvt",
                        MultiModelPdfDocumentOrigin.Active,
                        MultiModelPdfStorageKind.Workshared,
                        printSets: new[]
                        {
                            new MultiModelPdfSetSnapshot("Single Ready", 1),
                            new MultiModelPdfSetSnapshot("Exact Eight Skipped", 2, eightSkipped),
                            new MultiModelPdfSetSnapshot("Eleven Skipped", 3, elevenSkipped),
                            new MultiModelPdfSetSnapshot("Zero Printable", 0, new[] { new MultiModelPdfSkippedViewInfo("X001", "FUTURE", "Placeholder sheet") })
                        })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService())
            {
                OutputFolder = @"C:\Exports"
            };

            MultiModelPdfExportRowViewModel row = vm.Rows[0];
            CollectionAssert.AreEqual(
                new[]
                {
                    "<Select Print Set...>",
                    "Single Ready (1 view)",
                    "Exact Eight Skipped (2 views · 8 skip)",
                    "Eleven Skipped (3 views · 11 skip)",
                    "Zero Printable (0 views · 1 skip)"
                },
                row.PrintSetOptions.Select(o => o.DisplayName).ToArray());

            // 0 skipped -> Success footer, badge hidden
            row.SelectedPrintSetName = "Single Ready";
            Assert.IsFalse(row.HasSkippedViews);
            Assert.AreEqual("1 view", row.ViewsDisplayText);
            Assert.AreEqual(MultiModelPdfFooterStatusKind.Success, vm.FooterStatusKind);
            Assert.AreEqual("1 project · 1 view ready", vm.FooterStatusText);

            // Exactly 8 skipped -> all 8 listed, no "+ N more"
            row.SelectedPrintSetName = "Exact Eight Skipped";
            Assert.IsTrue(row.HasSkippedViews);
            Assert.AreEqual("8 skip", row.SkippedBadgeText);
            StringAssert.Contains("8 view/sheets will be skipped in HostModel.rvt:", row.SkippedTooltipText);
            StringAssert.Contains("• A908 PLACEHOLDER 8 (Placeholder sheet)", row.SkippedTooltipText);
            StringAssert.DoesNotContain("more", row.SkippedTooltipText);
            Assert.AreEqual(MultiModelPdfFooterStatusKind.Warning, vm.FooterStatusKind);
            Assert.AreEqual("1 project · 2 views · 8 skipped", vm.FooterStatusText);

            // 11 skipped -> first 8 listed, 9..11 omitted, "+ 3 more" appended
            row.SelectedPrintSetName = "Eleven Skipped";
            Assert.IsTrue(row.HasSkippedViews);
            Assert.AreEqual("11 skip", row.SkippedBadgeText);
            StringAssert.Contains("• A900 UNPLACED SIGNAGE (Placeholder sheet)", row.SkippedTooltipText);
            StringAssert.Contains("• FloorPlan Work-In-Progress Template (View template)", row.SkippedTooltipText);
            StringAssert.Contains("• Schedule Door Schedule (Schedule — not printable)", row.SkippedTooltipText);
            StringAssert.Contains("• A905 SHEET 8 (Placeholder sheet)", row.SkippedTooltipText);
            StringAssert.DoesNotContain("OMITTED NINTH", row.SkippedTooltipText);
            StringAssert.DoesNotContain("OMITTED ELEVENTH", row.SkippedTooltipText);
            Assert.IsTrue(row.SkippedTooltipText.EndsWith("+ 3 more", StringComparison.Ordinal));
            Assert.AreEqual(MultiModelPdfFooterStatusKind.Warning, vm.FooterStatusKind);
            Assert.AreEqual("1 project · 3 views · 11 skipped", vm.FooterStatusText);

            // Checked with 0 printable views -> Coral Red 0 views hides skip badge; unchecked shows 0 views + skip badge
            row.SelectedPrintSetName = "Zero Printable";
            Assert.IsTrue(row.HasZeroPrintableViews);
            Assert.IsFalse(row.HasSkippedViews);

            row.IsChecked = false;
            Assert.IsFalse(row.HasZeroPrintableViews);
            Assert.IsTrue(row.HasNormalViewCount);
            Assert.IsTrue(row.HasSkippedViews);
            Assert.AreEqual("1 skip", row.SkippedBadgeText);
        }

        [Test]
        public void OpenStandaloneDefaultsUnchecked_OriginBadgesAndCollisionCuesUpdateDynamically()
        {
            var snapshot = new MultiModelPdfSessionSnapshot(
                "Tower.rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active-tower",
                        "Tower.rvt",
                        @"C:\BIM\Active\Tower.rvt",
                        MultiModelPdfDocumentOrigin.Active,
                        MultiModelPdfStorageKind.Workshared,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) }),
                    new MultiModelPdfDocumentSnapshot(
                        "open-tower",
                        "Tower.rvt",
                        @"C:\BIM\Open\Tower.rvt",
                        MultiModelPdfDocumentOrigin.OpenStandalone,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) }),
                    new MultiModelPdfDocumentSnapshot(
                        "link-tower",
                        "Tower.rvt",
                        @"C:\BIM\Links\Tower.rvt",
                        MultiModelPdfDocumentOrigin.Linked,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 1) })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");

            // Row order is Active -> Linked -> OpenStandalone; Active + Linked checked, OpenStandalone unchecked
            Assert.AreEqual(3, vm.Rows.Count);
            MultiModelPdfExportRowViewModel activeRow = vm.Rows[0];
            MultiModelPdfExportRowViewModel linkedRow = vm.Rows[1];
            MultiModelPdfExportRowViewModel openRow = vm.Rows[2];

            Assert.AreEqual("ACTIVE", activeRow.OriginBadgeText);
            Assert.IsTrue(activeRow.IsActiveOrigin);
            Assert.IsFalse(activeRow.IsOpenOrigin);
            Assert.IsTrue(activeRow.IsChecked);

            Assert.AreEqual("LINKED", linkedRow.OriginBadgeText);
            Assert.IsTrue(linkedRow.IsLinkedOrigin);
            Assert.IsTrue(linkedRow.IsChecked);

            Assert.AreEqual("OPEN", openRow.OriginBadgeText);
            Assert.IsTrue(openRow.IsOpenOrigin);
            Assert.IsFalse(openRow.IsChecked, "OpenStandalone row must start unchecked by default.");

            // OpenStandalone is unchecked -> no collision cue on it yet; Linked collides with Active -> Tower (Link)
            Assert.IsFalse(activeRow.IsCollisionDisambiguated);
            Assert.AreEqual(string.Empty, activeRow.CollisionCueText);
            Assert.IsFalse(openRow.IsCollisionDisambiguated);
            Assert.AreEqual(string.Empty, openRow.CollisionCueText);
            Assert.IsTrue(linkedRow.IsCollisionDisambiguated);
            Assert.AreEqual("→ Tower (Link).pdf", linkedRow.CollisionCueText);
            StringAssert.Contains("Tower (Link).pdf", linkedRow.CollisionTooltipText);

            // Check OpenStandalone -> Active > OpenStandalone > Linked => Open gets Tower_2, Linked keeps Tower (Link)
            activeRow.SelectedPrintSetName = "CD Set";
            linkedRow.SelectedPrintSetName = "CD Set";
            openRow.SelectedPrintSetName = "CD Set";
            openRow.IsChecked = true;

            Assert.IsFalse(activeRow.IsCollisionDisambiguated);
            Assert.IsTrue(openRow.IsCollisionDisambiguated);
            Assert.AreEqual("→ Tower_2.pdf", openRow.CollisionCueText);
            StringAssert.Contains("Tower_2.pdf", openRow.CollisionTooltipText);
            StringAssert.Contains(@"C:\BIM\Open\Tower.rvt", openRow.CollisionTooltipText);
            StringAssert.Contains("Tower_2.pdf", openRow.RowTooltipText);

            Assert.IsTrue(linkedRow.IsCollisionDisambiguated);
            Assert.AreEqual("→ Tower (Link).pdf", linkedRow.CollisionCueText);

            // Switch to Separate mode -> cues switch from .pdf to trailing backslash
            vm.FileMode = MultiModelPdfFileMode.SeparatePerViewSheet;
            Assert.AreEqual(@"→ Tower_2\", openRow.CollisionCueText);
            Assert.AreEqual(@"→ Tower (Link)\", linkedRow.CollisionCueText);
            StringAssert.Contains(@"Tower_2\", openRow.CollisionTooltipText);

            // BuildRunRequest carries collision-disambiguated tokens
            MultiModelPdfRunRequest request = vm.BuildRunRequest();
            Assert.AreEqual("Tower", request.Jobs.Single(j => j.DocumentKey == "active-tower").OutputFileToken);
            Assert.AreEqual("Tower (Link)", request.Jobs.Single(j => j.DocumentKey == "link-tower").OutputFileToken);
            Assert.AreEqual("Tower_2", request.Jobs.Single(j => j.DocumentKey == "open-tower").OutputFileToken);

            // Unchecking Active promotes OpenStandalone to un-disambiguated Tower
            activeRow.IsChecked = false;
            vm.FileMode = MultiModelPdfFileMode.CombinedPerDocument;
            Assert.IsFalse(openRow.IsCollisionDisambiguated);
            Assert.AreEqual("Tower", openRow.ResolvedOutputToken);
            Assert.AreEqual("→ Tower (Link).pdf", linkedRow.CollisionCueText);
        }

        [Test]
        public void CollisionSuffixExpandingPathTo255Chars_TriggersPathTooLongValidation()
        {
            string baseToken = new string('t', 238);
            var snapshot = new MultiModelPdfSessionSnapshot(
                baseToken + ".rvt",
                new[]
                {
                    new MultiModelPdfDocumentSnapshot(
                        "active",
                        baseToken + ".rvt",
                        @"C:\BIM\A.rvt",
                        MultiModelPdfDocumentOrigin.Active,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD", 1) }),
                    new MultiModelPdfDocumentSnapshot(
                        "open",
                        baseToken + ".rvt",
                        @"C:\BIM\B.rvt",
                        MultiModelPdfDocumentOrigin.OpenStandalone,
                        MultiModelPdfStorageKind.Local,
                        printSets: new[] { new MultiModelPdfSetSnapshot("CD", 1) })
                },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            // OutputFolder len = 10 ("C:\Exports"), + "\" + 238 + ".pdf" (4) = 253 (< 255) for Active,
            // while "_2" adds 2 chars => 255 (>= 255) for OpenStandalone!
            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");
            vm.Rows[0].SelectedPrintSetName = "CD";
            vm.Rows[1].SelectedPrintSetName = "CD";

            Assert.IsTrue(vm.CanExport, "Active alone at 253 chars should be valid.");

            vm.Rows[1].IsChecked = true;
            Assert.IsFalse(vm.CanExport, "Disambiguated _2 pushing path to 255 chars must block export.");
            Assert.IsTrue(vm.ValidationErrors.Any(e =>
                e.Code == MultiModelPdfValidationCode.PathTooLong &&
                e.TargetKey == "open"));
        }

        [Test]
        public void FederatedRows_OrderActiveThenLinkedThenOpen_CheckActiveAndLinksDefault_AndDisableUnloadedLinksAt25Percent()
        {
            var openTab = new MultiModelPdfDocumentSnapshot(
                "open-ref", "RefModel.rvt", @"C:\BIM\RefModel.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var unloadedLink = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "SiteUnloaded.rvt", @"C:\BIM\Links\SiteUnloaded.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var loadedLinkB = new MultiModelPdfDocumentSnapshot(
                "link-b", "MEP.rvt", @"C:\BIM\Links\MEP.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.CloudAcc,
                isUnloadedLink: false,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });
            var loadedLinkA = new MultiModelPdfDocumentSnapshot(
                "link-a", "Struct.rvt", @"C:\BIM\Links\Struct.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Workshared,
                isUnloadedLink: false,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active-main", "ArchHost.rvt", @"C:\BIM\ArchHost.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 5) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "ArchHost.rvt",
                new[] { openTab, unloadedLink, loadedLinkB, loadedLinkA, activeDoc },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");

            // Order preserves relative order inside each bucket: Active -> Linked -> OpenStandalone
            CollectionAssert.AreEqual(
                new[] { "ArchHost.rvt", "SiteUnloaded.rvt", "MEP.rvt", "Struct.rvt", "RefModel.rvt" },
                vm.Rows.Select(r => r.DocumentTitle).ToArray());

            MultiModelPdfExportRowViewModel activeRow = vm.Rows[0];
            MultiModelPdfExportRowViewModel unloadedRow = vm.Rows[1];
            MultiModelPdfExportRowViewModel mepRow = vm.Rows[2];
            MultiModelPdfExportRowViewModel structRow = vm.Rows[3];
            MultiModelPdfExportRowViewModel openRow = vm.Rows[4];

            Assert.IsTrue(activeRow.IsChecked);
            Assert.AreEqual("ACTIVE", activeRow.OriginBadgeText);
            Assert.AreEqual(1.0, activeRow.RowOpacity, 1e-6);

            // Unloaded link row: ineligible, unchecked, disabled at 25% opacity with exact status text
            Assert.IsTrue(unloadedRow.IsUnloadedLink);
            Assert.IsTrue(unloadedRow.IsLinkedOrigin);
            Assert.AreEqual("LINKED", unloadedRow.OriginBadgeText);
            Assert.IsFalse(unloadedRow.IsEligible);
            Assert.IsTrue(unloadedRow.IsDisabledRow);
            Assert.AreEqual(0.25, unloadedRow.RowOpacity, 1e-6);
            Assert.IsFalse(unloadedRow.IsChecked);
            Assert.AreEqual("Unloaded link — load in host to inspect", unloadedRow.DisabledStatusText);
            unloadedRow.IsChecked = true;
            Assert.IsFalse(unloadedRow.IsChecked, "Unloaded link row must remain unchecked when toggled.");

            // Eligible loaded links start checked by default; OpenStandalone starts unchecked
            Assert.IsTrue(mepRow.IsChecked);
            Assert.AreEqual("LINKED", mepRow.OriginBadgeText);
            Assert.AreEqual("Cloud (ACC)", mepRow.StorageDisplayText);
            Assert.AreEqual(1.0, mepRow.RowOpacity, 1e-6);

            Assert.IsTrue(structRow.IsChecked);
            Assert.AreEqual("LINKED", structRow.OriginBadgeText);

            Assert.IsFalse(openRow.IsChecked);
            Assert.AreEqual("OPEN", openRow.OriginBadgeText);

            Assert.AreEqual(3, vm.CheckedCount);
            Assert.AreEqual(4, vm.EligibleCount);
            Assert.AreEqual("3 of 4 eligible", vm.SelectionSummaryText);

            activeRow.SelectedPrintSetName = "CD Set";
            mepRow.SelectedPrintSetName = "CD Set";
            structRow.SelectedPrintSetName = "CD Set";

            Assert.IsTrue(vm.CanExport);
            Assert.AreEqual("Export 3 PDFs", vm.ExportButtonText);

            MultiModelPdfRunRequest request = vm.BuildRunRequest();
            CollectionAssert.AreEqual(
                new[] { "active-main", "link-b", "link-a" },
                request.Jobs.Select(j => j.DocumentKey).ToArray());
        }
    }
}
