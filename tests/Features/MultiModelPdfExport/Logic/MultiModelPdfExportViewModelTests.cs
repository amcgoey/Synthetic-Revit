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

        [Test]
        public void BulkPrintSetOptions_SortByActivePresenceThenMatchCountThenAlphabetical_AndFormatVariantALabels()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active-core", "CoreShell.rvt", @"C:\BIM\CoreShell.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("IFC Review", 2),
                    new MultiModelPdfSetSnapshot("Permit Set", 4),
                    new MultiModelPdfSetSnapshot("CD Set", 5)
                });
            var link1 = new MultiModelPdfDocumentSnapshot(
                "link-1", "Unit1.rvt", @"C:\BIM\Unit1.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("cd set", 3),
                    new MultiModelPdfSetSnapshot("Unit Plans", 2)
                });
            var link2 = new MultiModelPdfDocumentSnapshot(
                "link-2", "Unit2.rvt", @"C:\BIM\Unit2.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("CD Set", 3),
                    new MultiModelPdfSetSnapshot("Permit Set", 2),
                    new MultiModelPdfSetSnapshot("Unit Plans", 2)
                });
            var link3 = new MultiModelPdfDocumentSnapshot(
                "link-3", "Retail.rvt", @"C:\BIM\Retail.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("Permit Set", 2),
                    new MultiModelPdfSetSnapshot("SD Package", 1)
                });
            var unloadedLink = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "Site.rvt", @"C:\BIM\Site.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Workshared,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var openTab = new MultiModelPdfDocumentSnapshot(
                "open-annex", "Annex.rvt", @"C:\BIM\Annex.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("CD Set", 6),
                    new MultiModelPdfSetSnapshot("Unit Plans", 2),
                    new MultiModelPdfSetSnapshot("DD Set", 4)
                });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "CoreShell.rvt",
                new[] { activeDoc, link1, link2, link3, unloadedLink, openTab },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            // In Active: CD Set (4), Permit Set (3), IFC Review (1)
            // Not in Active: Unit Plans (3), DD Set (1), SD Package (1) [alphabetical]
            CollectionAssert.AreEqual(
                new[]
                {
                    "CD Set (4 of 6 models)",
                    "Permit Set (3 of 6 models)",
                    "IFC Review (1 of 6 models)",
                    "Unit Plans (3 of 6 models)",
                    "DD Set (1 of 6 models)",
                    "SD Package (1 of 6 models)"
                },
                vm.BulkPrintSetOptions.Select(o => o.DisplayName).ToArray());

            Assert.IsTrue(vm.HasBulkPrintSetOptions);
            Assert.IsNotNull(vm.SelectedBulkPrintSetOption);
            Assert.AreEqual("CD Set", vm.SelectedBulkPrintSetOption!.Name);
            Assert.IsTrue(vm.Rows.All(r => r.SelectedPrintSetName == null), "Preselecting bulk dropdown must not auto-assign row Print Sets.");

            // Empty sets scenario disables bulk controls safely
            var emptyVm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(FakeMultiModelPdfExportAdapter.CreateDefaultActiveSnapshot(includePrintSets: false)),
                new FakeFolderDialogService());
            Assert.IsFalse(emptyVm.HasBulkPrintSetOptions);
            Assert.IsEmpty(emptyVm.BulkPrintSetOptions);
            Assert.IsNull(emptyVm.SelectedBulkPrintSetOption);
        }

        [Test]
        public void ApplyBulkPrintSet_FullMatch_UpdatesCheckedAndUncheckedEligibleRows_WithSuccessBanner()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "Host.rvt", @"C:\BIM\Host.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });
            var linkDoc = new MultiModelPdfDocumentSnapshot(
                "link-1", "Struct.rvt", @"C:\BIM\Struct.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("cd set", 3) });
            var openDoc = new MultiModelPdfDocumentSnapshot(
                "open-1", "Annex.rvt", @"C:\BIM\Annex.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var unloadedLink = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "Site.rvt", @"C:\BIM\Site.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Host.rvt",
                new[] { activeDoc, linkDoc, openDoc, unloadedLink },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");

            Assert.IsTrue(vm.ApplyBulkPrintSetCommand.CanExecute(null));
            vm.ApplyBulkPrintSetCommand.Execute(null);

            MultiModelPdfExportRowViewModel activeRow = vm.Rows.Single(r => r.DocumentKey == "active");
            MultiModelPdfExportRowViewModel linkRow = vm.Rows.Single(r => r.DocumentKey == "link-1");
            MultiModelPdfExportRowViewModel openRow = vm.Rows.Single(r => r.DocumentKey == "open-1");
            MultiModelPdfExportRowViewModel unloadedRow = vm.Rows.Single(r => r.DocumentKey == "link-unloaded");

            Assert.AreEqual("CD Set", activeRow.SelectedPrintSetName);
            Assert.AreEqual("cd set", linkRow.SelectedPrintSetName);
            Assert.AreEqual("CD Set", openRow.SelectedPrintSetName);
            Assert.IsNull(unloadedRow.SelectedPrintSetName);
            Assert.IsTrue(activeRow.IsChecked);
            Assert.IsTrue(linkRow.IsChecked);
            Assert.IsFalse(openRow.IsChecked, "Apply to Match must update unchecked rows without forcing check state.");
            Assert.IsFalse(unloadedRow.IsChecked);
            Assert.IsTrue(vm.Rows.All(r => !r.HasBulkMismatchAccent));

            Assert.IsTrue(vm.HasBulkFeedbackBanner);
            Assert.AreEqual(MultiModelPdfBulkFeedbackKind.Success, vm.BulkFeedbackKind);
            Assert.IsFalse(vm.ShowUncheckUnmatchedButton);
            Assert.AreEqual("Applied \"CD Set\" to all 2 checked projects (+1 unchecked).", vm.BulkFeedbackMessage);
            Assert.IsTrue(vm.CanExport);
            Assert.AreEqual("Export 2 PDFs", vm.ExportButtonText);
        }

        [Test]
        public void ApplyBulkPrintSet_PartialMatch_PreservesPriorSelections_AndClearsMismatchStripeOnAllThreeTriggers()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "Core.rvt", @"C:\BIM\Core.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("CD Set", 5),
                    new MultiModelPdfSetSnapshot("Permit Set", 3)
                });
            var linkMatched = new MultiModelPdfDocumentSnapshot(
                "link-match", "Unit1.rvt", @"C:\BIM\Unit1.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });
            var linkPriorSet = new MultiModelPdfDocumentSnapshot(
                "link-prior", "Retail.rvt", @"C:\BIM\Retail.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[]
                {
                    new MultiModelPdfSetSnapshot("Permit Set", 2),
                    new MultiModelPdfSetSnapshot("SD Package", 1)
                });
            var linkUnset = new MultiModelPdfDocumentSnapshot(
                "link-unset", "Garage.rvt", @"C:\BIM\Garage.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("SD Package", 2) });
            var openUncheckedMatch = new MultiModelPdfDocumentSnapshot(
                "open-match", "Annex.rvt", @"C:\BIM\Annex.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Core.rvt",
                new[] { activeDoc, linkMatched, linkPriorSet, linkUnset, openUncheckedMatch },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");

            MultiModelPdfExportRowViewModel retailRow = vm.Rows.Single(r => r.DocumentKey == "link-prior");
            MultiModelPdfExportRowViewModel garageRow = vm.Rows.Single(r => r.DocumentKey == "link-unset");
            retailRow.SelectedPrintSetName = "Permit Set";

            vm.SelectedBulkPrintSetOption = vm.BulkPrintSetOptions.First(o => o.Name == "CD Set");
            vm.ApplyBulkPrintSetCommand.Execute(null);

            // Matched rows receive CD Set; unmatched rows preserve prior selections
            Assert.AreEqual("CD Set", vm.Rows.Single(r => r.DocumentKey == "active").SelectedPrintSetName);
            Assert.AreEqual("CD Set", vm.Rows.Single(r => r.DocumentKey == "link-match").SelectedPrintSetName);
            Assert.AreEqual("CD Set", vm.Rows.Single(r => r.DocumentKey == "open-match").SelectedPrintSetName);
            Assert.AreEqual("Permit Set", retailRow.SelectedPrintSetName);
            Assert.IsNull(garageRow.SelectedPrintSetName);

            // Warning banner & mismatch stripe strictly on checked unmatched rows
            Assert.IsTrue(vm.HasBulkFeedbackBanner);
            Assert.AreEqual(MultiModelPdfBulkFeedbackKind.Warning, vm.BulkFeedbackKind);
            Assert.IsTrue(vm.ShowUncheckUnmatchedButton);
            Assert.AreEqual("Applied \"CD Set\" to 2 checked (+1 unchecked). No \"CD Set\" in: Retail.rvt, Garage.rvt.", vm.BulkFeedbackMessage);
            Assert.IsTrue(retailRow.HasBulkMismatchAccent);
            Assert.IsTrue(garageRow.HasBulkMismatchAccent);
            Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "active").HasBulkMismatchAccent);
            Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "open-match").HasBulkMismatchAccent);

            // Trigger 2: Per-row Print Set change clears stripe on Retail.rvt
            retailRow.SelectedPrintSetName = "SD Package";
            Assert.IsFalse(retailRow.HasBulkMismatchAccent);
            Assert.IsTrue(garageRow.HasBulkMismatchAccent);
            Assert.IsTrue(vm.ShowUncheckUnmatchedButton);

            // Trigger 3: Row uncheck clears stripe on Garage.rvt, and re-checking does not restore it
            garageRow.IsChecked = false;
            Assert.IsFalse(garageRow.HasBulkMismatchAccent);
            Assert.IsFalse(vm.ShowUncheckUnmatchedButton);
            garageRow.IsChecked = true;
            Assert.IsFalse(garageRow.HasBulkMismatchAccent);

            // Trigger 1: Banner dismiss clears banner and all mismatch stripes
            vm.ApplyBulkPrintSetCommand.Execute(null);
            Assert.IsTrue(retailRow.HasBulkMismatchAccent);
            Assert.IsTrue(garageRow.HasBulkMismatchAccent);
            vm.DismissBulkFeedbackCommand.Execute(null);
            Assert.IsFalse(vm.HasBulkFeedbackBanner);
            Assert.AreEqual(MultiModelPdfBulkFeedbackKind.None, vm.BulkFeedbackKind);
            Assert.AreEqual(string.Empty, vm.BulkFeedbackMessage);
            Assert.IsFalse(retailRow.HasBulkMismatchAccent);
            Assert.IsFalse(garageRow.HasBulkMismatchAccent);

            // One-click "Uncheck unmatched" unchecks checked mismatched rows, clears stripes & banner, unblocks export
            vm.ApplyBulkPrintSetCommand.Execute(null);
            Assert.IsFalse(vm.CanExport, "Garage.rvt has no Print Set so export is blocked before unchecking unmatched.");
            Assert.IsTrue(vm.UncheckUnmatchedProjectsCommand.CanExecute(null));
            vm.UncheckUnmatchedProjectsCommand.Execute(null);

            Assert.IsFalse(retailRow.IsChecked);
            Assert.IsFalse(garageRow.IsChecked);
            Assert.IsFalse(retailRow.HasBulkMismatchAccent);
            Assert.IsFalse(garageRow.HasBulkMismatchAccent);
            Assert.IsFalse(vm.HasBulkFeedbackBanner);
            Assert.IsTrue(vm.CanExport);
            Assert.AreEqual("Export 2 PDFs", vm.ExportButtonText);
        }

        [Test]
        public void ApplyBulkPrintSet_WhenZeroProjectsChecked_SafelyAppliesToUncheckedWithoutWarningBannerOrStripes()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "Host.rvt", @"C:\BIM\Host.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });
            var linkDoc = new MultiModelPdfDocumentSnapshot(
                "link-1", "Retail.rvt", @"C:\BIM\Retail.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 2) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Host.rvt",
                new[] { activeDoc, linkDoc },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService(),
                @"C:\Exports");

            vm.Rows[0].IsChecked = false;
            vm.Rows[1].SelectedPrintSetName = "Permit Set";
            vm.Rows[1].IsChecked = false;

            vm.SelectedBulkPrintSetOption = vm.BulkPrintSetOptions.First(o => o.Name == "CD Set");
            vm.ApplyBulkPrintSetCommand.Execute(null);

            Assert.AreEqual("CD Set", vm.Rows[0].SelectedPrintSetName);
            Assert.AreEqual("Permit Set", vm.Rows[1].SelectedPrintSetName);
            Assert.IsFalse(vm.Rows[0].IsChecked);
            Assert.IsFalse(vm.Rows[1].IsChecked);
            Assert.IsFalse(vm.Rows[0].HasBulkMismatchAccent);
            Assert.IsFalse(vm.Rows[1].HasBulkMismatchAccent);

            Assert.IsTrue(vm.HasBulkFeedbackBanner);
            Assert.AreEqual(MultiModelPdfBulkFeedbackKind.Success, vm.BulkFeedbackKind);
            Assert.IsFalse(vm.ShowUncheckUnmatchedButton);
            Assert.IsFalse(vm.UncheckUnmatchedProjectsCommand.CanExecute(null));
            Assert.AreEqual("Applied \"CD Set\" to 1 unchecked project (0 checked).", vm.BulkFeedbackMessage);
        }

        [Test]
        public void FilterText_MatchesTitleOriginAndStorageDistinctly_AndFormatsHiddenSelectionSummary()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "CoreShell.rvt", @"C:\BIM\CoreShell.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });
            var linkCloud = new MultiModelPdfDocumentSnapshot(
                "link-cloud", "Unit1.rvt", @"C:\BIM\Unit1.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.CloudAcc,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var linkLocal = new MultiModelPdfDocumentSnapshot(
                "link-local", "Retail.rvt", @"C:\BIM\Retail.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 2) });
            var linkUnloaded = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "SiteUnloaded.rvt", @"C:\BIM\SiteUnloaded.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Workshared,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var openLocal = new MultiModelPdfDocumentSnapshot(
                "open-local", "Annex.rvt", @"C:\BIM\Annex.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });
            var openUnsaved = new MultiModelPdfDocumentSnapshot(
                "open-unsaved", "Scratch.rvt", string.Empty,
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Unsaved,
                printSets: new[] { new MultiModelPdfSetSnapshot("SD Set", 1) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "CoreShell.rvt",
                new[] { openLocal, linkCloud, linkUnloaded, activeDoc, openUnsaved, linkLocal },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            CollectionAssert.AreEqual(
                new[] { "CoreShell.rvt", "Unit1.rvt", "SiteUnloaded.rvt", "Retail.rvt", "Annex.rvt", "Scratch.rvt" },
                vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());
            Assert.AreEqual(0, vm.HiddenCheckedCount);
            Assert.AreEqual("3 of 5 eligible", vm.SelectionSummaryText);
            Assert.IsFalse(vm.HasNoVisibleRows);

            // Title filter (case-insensitive, trimmed)
            vm.FilterText = "  unit1  ";
            CollectionAssert.AreEqual(new[] { "Unit1.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());
            Assert.AreEqual(2, vm.HiddenCheckedCount);
            Assert.AreEqual("3 selected (2 hidden)", vm.SelectionSummaryText);

            // Origin filter
            vm.FilterText = "open";
            CollectionAssert.AreEqual(new[] { "Annex.rvt", "Scratch.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());
            Assert.AreEqual(3, vm.HiddenCheckedCount);
            Assert.AreEqual("3 selected (3 hidden)", vm.SelectionSummaryText);

            // Storage filters
            vm.FilterText = "cloud";
            CollectionAssert.AreEqual(new[] { "Unit1.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            vm.FilterText = "ACC";
            CollectionAssert.AreEqual(new[] { "Unit1.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            vm.FilterText = "CloudAcc";
            CollectionAssert.AreEqual(new[] { "Unit1.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            vm.FilterText = "Unsaved";
            CollectionAssert.AreEqual(new[] { "Scratch.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            vm.FilterText = "Workshared";
            CollectionAssert.AreEqual(new[] { "CoreShell.rvt", "SiteUnloaded.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            // Distinct field boundaries: cross-field phrases, paths, and Print Sets must NOT match
            foreach (string nonMatchingCrossFieldQuery in new[] { "CoreShell.rvt Active", "Linked Cloud", "Open Local", "Standalone", ".rvt Linked", @"C:\BIM", "CD Set", "Permit Set" })
            {
                vm.FilterText = nonMatchingCrossFieldQuery;
                Assert.IsEmpty(vm.VisibleRows, $"Cross-field query '{nonMatchingCrossFieldQuery}' must not bleed across Title/Origin/Storage fields.");
                Assert.IsTrue(vm.HasNoVisibleRows);
                Assert.AreEqual(3, vm.HiddenCheckedCount);
                Assert.AreEqual("3 selected (3 hidden)", vm.SelectionSummaryText);
            }

            // Clearing filter restores all rows and standard summary
            vm.FilterText = string.Empty;
            Assert.AreEqual(6, vm.VisibleRows.Count);
            Assert.AreEqual(0, vm.HiddenCheckedCount);
            Assert.AreEqual("3 of 5 eligible", vm.SelectionSummaryText);
            Assert.IsFalse(vm.HasNoVisibleRows);
        }

        [Test]
        public void QuickSelectButtons_AllActiveLinksAndNone_ScopeStrictlyToVisibleFilteredEligibleRows()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "Core.rvt", @"C:\BIM\Core.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });
            var link1 = new MultiModelPdfDocumentSnapshot(
                "link-1", "Link1.rvt", @"C:\BIM\Link1.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var link2Mismatch = new MultiModelPdfDocumentSnapshot(
                "link-2", "Link2.rvt", @"C:\BIM\Link2.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.CloudAcc,
                printSets: new[] { new MultiModelPdfSetSnapshot("Permit Set", 2) });
            var linkUnloaded = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "Unloaded.rvt", @"C:\BIM\Unloaded.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var openLocal = new MultiModelPdfDocumentSnapshot(
                "open-local", "Open1.rvt", @"C:\BIM\Open1.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });
            var openWorkshared = new MultiModelPdfDocumentSnapshot(
                "open-ws", "Open2.rvt", @"C:\BIM\Open2.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Core.rvt",
                new[] { activeDoc, link1, link2Mismatch, linkUnloaded, openLocal, openWorkshared },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            // Mark Link2 as bulk mismatched so we verify hidden mismatch stripe is preserved while visible uncheck clears it
            vm.SelectedBulkPrintSetOption = vm.BulkPrintSetOptions.First(o => o.Name == "CD Set");
            vm.ApplyBulkPrintSetCommand.Execute(null);
            MultiModelPdfExportRowViewModel coreRow = vm.Rows.Single(r => r.DocumentKey == "active");
            MultiModelPdfExportRowViewModel link1Row = vm.Rows.Single(r => r.DocumentKey == "link-1");
            MultiModelPdfExportRowViewModel link2Row = vm.Rows.Single(r => r.DocumentKey == "link-2");
            MultiModelPdfExportRowViewModel unloadedRow = vm.Rows.Single(r => r.DocumentKey == "link-unloaded");
            MultiModelPdfExportRowViewModel open1Row = vm.Rows.Single(r => r.DocumentKey == "open-local");
            MultiModelPdfExportRowViewModel open2Row = vm.Rows.Single(r => r.DocumentKey == "open-ws");
            Assert.IsTrue(link2Row.HasBulkMismatchAccent);

            // Filter to "Local" -> visible: Link1 (checked), Unloaded (ineligible), Open1 (unchecked)
            vm.FilterText = "Local";
            CollectionAssert.AreEqual(new[] { "Link1.rvt", "Unloaded.rvt", "Open1.rvt" }, vm.VisibleRows.Select(r => r.DocumentTitle).ToArray());

            // Visible "All" checks Open1, keeps Unloaded unchecked, and leaves hidden Open2 unchecked
            vm.SelectAllVisibleCommand.Execute(null);
            Assert.IsTrue(link1Row.IsChecked);
            Assert.IsTrue(open1Row.IsChecked);
            Assert.IsFalse(unloadedRow.IsChecked);
            Assert.IsTrue(coreRow.IsChecked);
            Assert.IsTrue(link2Row.IsChecked);
            Assert.IsFalse(open2Row.IsChecked, "Hidden Open2 must not be checked by visible All.");

            // Visible "Active + Links" unchecks visible Open1, keeps Link1 checked, leaves hidden rows untouched
            vm.SelectActiveAndLinksVisibleCommand.Execute(null);
            Assert.IsTrue(link1Row.IsChecked);
            Assert.IsFalse(open1Row.IsChecked);
            Assert.IsFalse(unloadedRow.IsChecked);
            Assert.IsTrue(coreRow.IsChecked);
            Assert.IsTrue(link2Row.IsChecked);
            Assert.IsFalse(open2Row.IsChecked);

            // Visible "None" unchecks Link1 only, keeping hidden Core & Link2 checked (and Link2 mismatch stripe intact)
            open1Row.IsChecked = true;
            vm.SelectNoneVisibleCommand.Execute(null);
            Assert.IsFalse(link1Row.IsChecked);
            Assert.IsFalse(open1Row.IsChecked);
            Assert.IsTrue(coreRow.IsChecked);
            Assert.IsTrue(link2Row.IsChecked);
            Assert.IsTrue(link2Row.HasBulkMismatchAccent);

            // Unchecking visible Link2 via "None" clears Link2's mismatch stripe
            vm.FilterText = "Cloud";
            vm.SelectNoneVisibleCommand.Execute(null);
            Assert.IsFalse(link2Row.IsChecked);
            Assert.IsFalse(link2Row.HasBulkMismatchAccent);
            Assert.IsTrue(coreRow.IsChecked);
        }

        [Test]
        public void MasterHeaderCheckbox_AllVisibleChecked_TransitionsTriStateAndScopesToVisibleRows()
        {
            var activeDoc = new MultiModelPdfDocumentSnapshot(
                "active", "Core.rvt", @"C:\BIM\Core.rvt",
                MultiModelPdfDocumentOrigin.Active, MultiModelPdfStorageKind.Workshared,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 3) });
            var linkDoc = new MultiModelPdfDocumentSnapshot(
                "link-1", "Link1.rvt", @"C:\BIM\Link1.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 2) });
            var linkUnloaded = new MultiModelPdfDocumentSnapshot(
                "link-unloaded", "Unloaded.rvt", @"C:\BIM\Unloaded.rvt",
                MultiModelPdfDocumentOrigin.Linked, MultiModelPdfStorageKind.Local,
                isUnloadedLink: true,
                printSets: Array.Empty<MultiModelPdfSetSnapshot>());
            var openDoc = new MultiModelPdfDocumentSnapshot(
                "open-1", "Open1.rvt", @"C:\BIM\Open1.rvt",
                MultiModelPdfDocumentOrigin.OpenStandalone, MultiModelPdfStorageKind.Local,
                printSets: new[] { new MultiModelPdfSetSnapshot("CD Set", 4) });

            var snapshot = new MultiModelPdfSessionSnapshot(
                "Core.rvt",
                new[] { activeDoc, linkDoc, linkUnloaded, openDoc },
                new[] { MultiModelPdfSetupOption.CreateDefault() });

            var vm = new MultiModelPdfExportViewModel(
                new FakeMultiModelPdfExportAdapter(snapshot),
                new FakeFolderDialogService());

            // Initial mixed state: Core + Link1 checked, Open1 unchecked -> indeterminate (null)
            Assert.IsTrue(vm.HasVisibleEligibleRows);
            Assert.IsNull(vm.AllVisibleChecked);

            // Filtering to checked-only Active row -> true; Open-only -> false; Unloaded-only -> false & disabled
            vm.FilterText = "Active";
            Assert.IsTrue(vm.HasVisibleEligibleRows);
            Assert.AreEqual(true, vm.AllVisibleChecked);

            vm.FilterText = "Open";
            Assert.IsTrue(vm.HasVisibleEligibleRows);
            Assert.AreEqual(false, vm.AllVisibleChecked);

            vm.FilterText = "Unloaded";
            Assert.IsFalse(vm.HasVisibleEligibleRows);
            Assert.AreEqual(false, vm.AllVisibleChecked);
            vm.AllVisibleChecked = true;
            Assert.AreEqual(false, vm.AllVisibleChecked);
            Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "link-unloaded").IsChecked);

            // Clear filter and verify ToggleAllVisibleCommand & property setter transitions
            vm.FilterText = string.Empty;
            Assert.IsNull(vm.AllVisibleChecked);

            // ToggleAllVisible from indeterminate checks all visible eligible -> true
            vm.ToggleAllVisibleCommand.Execute(null);
            Assert.AreEqual(true, vm.AllVisibleChecked);
            Assert.IsTrue(vm.Rows.Single(r => r.DocumentKey == "open-1").IsChecked);
            Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "link-unloaded").IsChecked);

            // Setting AllVisibleChecked = false unchecks all visible eligible -> false
            vm.AllVisibleChecked = false;
            Assert.AreEqual(false, vm.AllVisibleChecked);
            Assert.AreEqual(0, vm.CheckedCount);

            // Incremental row checking transitions false -> null -> true
            vm.Rows.Single(r => r.DocumentKey == "active").IsChecked = true;
            Assert.IsNull(vm.AllVisibleChecked);
            vm.Rows.Single(r => r.DocumentKey == "link-1").IsChecked = true;
            Assert.IsNull(vm.AllVisibleChecked);
            vm.Rows.Single(r => r.DocumentKey == "open-1").IsChecked = true;
            Assert.AreEqual(true, vm.AllVisibleChecked);

            // Header uncheck while filtered affects ONLY visible eligible rows
            vm.FilterText = "Open";
            Assert.AreEqual(true, vm.AllVisibleChecked);
            vm.AllVisibleChecked = false;
            Assert.AreEqual(false, vm.AllVisibleChecked);
            Assert.IsFalse(vm.Rows.Single(r => r.DocumentKey == "open-1").IsChecked);
            Assert.IsTrue(vm.Rows.Single(r => r.DocumentKey == "active").IsChecked);
            Assert.IsTrue(vm.Rows.Single(r => r.DocumentKey == "link-1").IsChecked);
        }
    }
}
