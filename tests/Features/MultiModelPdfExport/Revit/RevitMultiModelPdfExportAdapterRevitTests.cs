using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Platform.UI;
using SyntheticTests.Fixtures;
using View = Autodesk.Revit.DB.View;

namespace SyntheticTests.Features.MultiModelPdfExport
{
    /// <summary>
    /// Executing live Revit isolated-document integration tests for <see cref="RevitMultiModelPdfExportAdapter"/>
    /// covering Print Set discovery (OrderedViewList + fallback + skip diagnostics), native PDF export,
    /// temp-staging atomic promotion/failure preservation, modifiable-doc guards, and family-doc filtering.
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class RevitMultiModelPdfExportAdapterRevitTests : RevitIsolatedDocumentFixture
    {
        private bool _previousSuppressUi;

        protected override void OnTestSetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            ProgressCoordinator.SuppressUI = true;
        }

        protected override void OnTestTearDown()
        {
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
        }

        [Test]
        public void CaptureSessionSnapshot_HarvestsActivePrintSetsOrderedAndFallbackViewsSkippedItemsAndPdfSetups()
        {
            Document doc = OpenIsolatedTestTemplateCopy();
            ExportFixtureSuite suite = SeedExportFixtureSuite(doc);

            string baselinePath = GetTempFilePath("SnapshotBaseline.rvt");
            doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified, "Fixture SaveAs should clear IsModified.");

            var adapter = new RevitMultiModelPdfExportAdapter(doc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            Assert.IsFalse(doc.IsModified, "CaptureSessionSnapshot must not mutate the active Revit document.");
            Assert.AreEqual(1, snapshot.Documents.Count, "Tracer bullet must discover exactly 1 active project document.");

            MultiModelPdfDocumentSnapshot activeDoc = snapshot.Documents[0];
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Active, activeDoc.Origin);
            Assert.IsFalse(activeDoc.IsUnloadedLink);
            Assert.AreEqual(MultiModelPdfStorageKind.Local, activeDoc.Storage);
            Assert.Greater(activeDoc.PrintSets.Count, 0);

            MultiModelPdfSetSnapshot orderedSet = activeDoc.PrintSets.FirstOrDefault(s => s.Name == "CD Ordered Set")
                ?? throw new AssertionException("Expected 'CD Ordered Set' in harvested Print Sets.");
            Assert.AreEqual(2, orderedSet.PrintableViewCount, "Expected both Sheet A101 and Sheet A201 to be printable.");
            Assert.AreEqual(0, orderedSet.SkippedViews.Count);

            MultiModelPdfSetSnapshot mixedSet = activeDoc.PrintSets.FirstOrDefault(s => s.Name == "Mixed Skip Set")
                ?? throw new AssertionException("Expected 'Mixed Skip Set' in harvested Print Sets.");
            Assert.AreEqual(1, mixedSet.PrintableViewCount, "Expected only Sheet PDF-101 to be printable in Mixed Skip Set.");
            Assert.AreEqual(2, mixedSet.SkippedViews.Count, "Expected Placeholder sheet and non-printable Wall Schedule to be recorded as skipped.");

            MultiModelPdfSkippedViewInfo? placeholderInfo = mixedSet.SkippedViews
                .FirstOrDefault(s => string.Equals(s.ViewName, suite.PlaceholderSheet.Name, StringComparison.Ordinal));
            Assert.IsNotNull(placeholderInfo, "Expected placeholder sheet in SkippedViews diagnostics.");
            Assert.AreEqual("PDF-900", placeholderInfo!.DisplayCode);
            Assert.AreEqual("Placeholder sheet", placeholderInfo.SkipReason);

            MultiModelPdfSkippedViewInfo? scheduleInfo = mixedSet.SkippedViews
                .FirstOrDefault(s => string.Equals(s.ViewName, suite.WallSchedule.Name, StringComparison.Ordinal));
            Assert.IsNotNull(scheduleInfo, "Expected non-printable Wall Schedule in SkippedViews diagnostics.");
            Assert.AreEqual("Schedule", scheduleInfo!.DisplayCode);
            Assert.AreEqual("Schedule — not printable", scheduleInfo.SkipReason);

            Assert.IsTrue(
                snapshot.PdfSetups.Any(s => s.IsDefault && s.Id == MultiModelPdfSetupOption.DefaultSetupId),
                "Expected <Default — Use Sheet Size> setup option first.");
            Assert.IsTrue(
                snapshot.PdfSetups.Any(s => !s.IsDefault && s.Id == "INC_SmokeSetup"),
                "Expected seeded ExportPDFSettings 'INC_SmokeSetup' in PdfSetups.");
        }

        [Test]
        public void ExportDocument_CombinedMode_DefaultAndSavedPdfSetup_CreatesValidPdfsWithoutMutatingDoc()
        {
            Document doc = OpenIsolatedTestTemplateCopy();
            SeedExportFixtureSuite(doc);

            string baselinePath = GetTempFilePath("ExportBaseline.rvt");
            doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified);

            var adapter = new RevitMultiModelPdfExportAdapter(doc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            MultiModelPdfDocumentSnapshot activeDoc = snapshot.Documents.Single();

            string defaultOutFolder = Path.Combine(CurrentTestTempDirectory, "DefaultOut");
            var defaultJob = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "CD Ordered Set",
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                outputFolder: defaultOutFolder,
                outputFileToken: "HostDefault");

            MultiModelPdfDocumentOutcome defaultOutcome = adapter.ExportDocument(defaultJob);

            Assert.IsTrue(defaultOutcome.Succeeded, defaultOutcome.ErrorMessage ?? "Expected Default setup PDF export to succeed.");
            Assert.AreEqual(1, defaultOutcome.GeneratedFilePaths.Count);
            AssertPdfHeaderValid(defaultOutcome.GeneratedFilePaths[0]);
            Assert.IsFalse(doc.IsModified, "Default PDF export must not dirty the Revit document.");

            string savedOutFolder = Path.Combine(CurrentTestTempDirectory, "SavedSetupOut");
            var savedJob = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "Mixed Skip Set",
                pdfSetupId: "INC_SmokeSetup",
                fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                outputFolder: savedOutFolder,
                outputFileToken: "HostSavedSetup");

            MultiModelPdfDocumentOutcome savedOutcome = adapter.ExportDocument(savedJob);

            Assert.IsTrue(savedOutcome.Succeeded, savedOutcome.ErrorMessage ?? "Expected saved ExportPDFSettings PDF export to succeed.");
            Assert.AreEqual(1, savedOutcome.GeneratedFilePaths.Count);
            AssertPdfHeaderValid(savedOutcome.GeneratedFilePaths[0]);
            Assert.IsTrue(savedOutcome.Warnings.Count > 0, "Expected skipped-view warning notice when exporting Mixed Skip Set.");
            StringAssert.Contains("PDF-900", savedOutcome.Warnings[0]);
            StringAssert.Contains("Placeholder sheet", savedOutcome.Warnings[0]);
            Assert.IsFalse(doc.IsModified, "Saved-setup PDF export must not dirty the Revit document.");
        }

        [Test]
        public void ExportDocument_SeparateMode_CreatesPerViewPdfsInSubfolder_PropagatesSkipNotices_AndPreservesLockedFiles()
        {
            Document doc = OpenIsolatedTestTemplateCopy();
            SeedExportFixtureSuite(doc);

            string baselinePath = GetTempFilePath("SeparateBaseline.rvt");
            doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
            Assert.IsFalse(doc.IsModified);

            var adapter = new RevitMultiModelPdfExportAdapter(doc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            MultiModelPdfDocumentSnapshot activeDoc = snapshot.Documents.Single();

            // 1. Two-sheet CD Ordered Set exported in Separate mode into <OutRoot>\<Token>\
            string outRoot = Path.Combine(CurrentTestTempDirectory, "SeparateOut");
            var separateJob = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "CD Ordered Set",
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.SeparatePerViewSheet,
                outputFolder: outRoot,
                outputFileToken: "TowerA_Core");

            MultiModelPdfDocumentOutcome outcome = adapter.ExportDocument(separateJob);

            Assert.IsTrue(outcome.Succeeded, outcome.ErrorMessage ?? "Expected Separate-mode PDF export to succeed.");
            Assert.AreEqual(2, outcome.GeneratedCount, "Expected one PDF per printable sheet in Separate mode.");
            Assert.AreEqual(2, outcome.GeneratedFilePaths.Count);
            Assert.IsNotNull(outcome.PrimaryFilePath);
            Assert.AreEqual(outcome.GeneratedFilePaths[0], outcome.PrimaryFilePath);

            string expectedSubfolder = Path.Combine(outRoot, "TowerA_Core");
            Assert.IsTrue(Directory.Exists(expectedSubfolder), $"Expected document subfolder '{expectedSubfolder}'.");
            Assert.IsFalse(File.Exists(Path.Combine(outRoot, "TowerA_Core.pdf")), "Separate mode must not create a combined PDF in OutputFolder root.");
            Assert.IsEmpty(Directory.GetFiles(expectedSubfolder, ".syn_*.tmp"), "Temp promotion artifacts must be cleaned up.");

            foreach (string pdfPath in outcome.GeneratedFilePaths)
            {
                Assert.AreEqual(expectedSubfolder, Path.GetDirectoryName(pdfPath));
                AssertPdfHeaderValid(pdfPath);
            }
            Assert.AreNotEqual(outcome.GeneratedFilePaths[0], outcome.GeneratedFilePaths[1]);
            Assert.IsFalse(doc.IsModified, "Separate PDF export must not dirty the Revit document.");

            // 2. Pipeline execution on Mixed Skip Set in Separate mode aggregates skip diagnostics into CompletionNotices
            string mixedOutRoot = Path.Combine(CurrentTestTempDirectory, "SeparateMixedOut");
            var pipeline = new MultiModelPdfExportPipeline(adapter);
            var mixedJob = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "Mixed Skip Set",
                pdfSetupId: "INC_SmokeSetup",
                fileMode: MultiModelPdfFileMode.SeparatePerViewSheet,
                outputFolder: mixedOutRoot,
                outputFileToken: "HostMixed");
            var request = new MultiModelPdfRunRequest(
                new[] { mixedJob },
                mixedOutRoot,
                "INC_SmokeSetup",
                MultiModelPdfFileMode.SeparatePerViewSheet);

            MultiModelPdfRunSummary summary = pipeline.Execute(request);
            Assert.AreEqual(1, summary.CompletedDocumentCount);
            Assert.AreEqual(1, summary.GeneratedCount);
            Assert.AreEqual(Path.Combine(mixedOutRoot, "HostMixed"), Path.GetDirectoryName(summary.PrimaryFilePath!));
            AssertPdfHeaderValid(summary.PrimaryFilePath!);
            Assert.IsNotEmpty(summary.Warnings);
            StringAssert.Contains("PDF-900", summary.CompletionNotices[0]);
            StringAssert.Contains("UNPLACED PLACEHOLDER", summary.CompletionNotices[0]);
            StringAssert.Contains("Placeholder sheet", summary.CompletionNotices[0]);
            StringAssert.Contains("WALL SCHEDULE", summary.CompletionNotices[0]);
            StringAssert.Contains("Schedule — not printable", summary.CompletionNotices[0]);

            // 3. Locked target file in Separate subfolder fails cleanly without corrupting existing PDFs
            string firstExistingPdf = outcome.GeneratedFilePaths[0];
            string secondExistingPdf = outcome.GeneratedFilePaths[1];
            byte[] firstOriginalBytes = File.ReadAllBytes(firstExistingPdf);
            byte[] secondOriginalBytes = File.ReadAllBytes(secondExistingPdf);

            MultiModelPdfDocumentOutcome lockedOutcome;
            using (new FileStream(secondExistingPdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                lockedOutcome = adapter.ExportDocument(separateJob);
            }

            Assert.IsFalse(lockedOutcome.Succeeded, "Expected failure when a Separate target PDF is locked.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(lockedOutcome.ErrorMessage));
            CollectionAssert.AreEqual(firstOriginalBytes, File.ReadAllBytes(firstExistingPdf), "Sibling PDF must remain untouched when another file is locked.");
            CollectionAssert.AreEqual(secondOriginalBytes, File.ReadAllBytes(secondExistingPdf), "Locked PDF must remain untouched.");
            Assert.IsEmpty(Directory.GetFiles(expectedSubfolder, ".syn_*.tmp"));
        }

        [Test]
        public void ExportDocument_PreservesPreExistingPdf_OnMissingPrintSetAndLockedTargetFailure()
        {
            Document doc = OpenIsolatedTestTemplateCopy();
            SeedExportFixtureSuite(doc);

            string baselinePath = GetTempFilePath("PreservePdfBaseline.rvt");
            doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });

            var adapter = new RevitMultiModelPdfExportAdapter(doc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            MultiModelPdfDocumentSnapshot activeDoc = snapshot.Documents.Single();

            string outFolder = Path.Combine(CurrentTestTempDirectory, "Out");
            Directory.CreateDirectory(outFolder);

            // 1. Missing Print Set must fail cleanly before export without touching existing destination PDF.
            string missingTargetPdf = Path.Combine(outFolder, "KeepOnMissing.pdf");
            byte[] sentinelMissingBytes = Encoding.ASCII.GetBytes("%PDF-1.4 SENTINEL-MISSING\n");
            File.WriteAllBytes(missingTargetPdf, sentinelMissingBytes);

            var missingSetJob = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "DoesNotExistSet",
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                outputFolder: outFolder,
                outputFileToken: "KeepOnMissing");

            MultiModelPdfDocumentOutcome missingOutcome = adapter.ExportDocument(missingSetJob);
            Assert.IsFalse(missingOutcome.Succeeded);
            Assert.IsTrue(missingOutcome.ErrorMessage!.Contains("DoesNotExistSet"));
            CollectionAssert.AreEqual(sentinelMissingBytes, File.ReadAllBytes(missingTargetPdf));

            // 2. File-lock during promotion must return clean failure and keep original PDF intact.
            string lockedTargetPdf = Path.Combine(outFolder, "KeepOnLocked.pdf");
            byte[] sentinelLockedBytes = Encoding.ASCII.GetBytes("%PDF-1.4 SENTINEL-LOCKED\n");
            File.WriteAllBytes(lockedTargetPdf, sentinelLockedBytes);

            MultiModelPdfDocumentOutcome lockedOutcome;
            using (new FileStream(lockedTargetPdf, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var lockedJob = new MultiModelPdfDocumentJob(
                    documentKey: activeDoc.DocumentKey,
                    documentTitle: activeDoc.DocumentTitle,
                    origin: activeDoc.Origin,
                    storage: activeDoc.Storage,
                    printSetName: "CD Ordered Set",
                    pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                    fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                    outputFolder: outFolder,
                    outputFileToken: "KeepOnLocked");

                lockedOutcome = adapter.ExportDocument(lockedJob);
            }

            Assert.IsFalse(lockedOutcome.Succeeded, "Expected failure when destination PDF is locked by another process.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(lockedOutcome.ErrorMessage));
            CollectionAssert.AreEqual(sentinelLockedBytes, File.ReadAllBytes(lockedTargetPdf));
        }

        [Test]
        public void ExportDocument_WhenDocumentIsModifiable_ReturnsFailureCleanly()
        {
            Document doc = OpenIsolatedTestTemplateCopy();
            SeedExportFixtureSuite(doc);

            var adapter = new RevitMultiModelPdfExportAdapter(doc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();
            MultiModelPdfDocumentSnapshot activeDoc = snapshot.Documents.Single();

            var job = new MultiModelPdfDocumentJob(
                documentKey: activeDoc.DocumentKey,
                documentTitle: activeDoc.DocumentTitle,
                origin: activeDoc.Origin,
                storage: activeDoc.Storage,
                printSetName: "CD Ordered Set",
                pdfSetupId: MultiModelPdfSetupOption.DefaultSetupId,
                fileMode: MultiModelPdfFileMode.CombinedPerDocument,
                outputFolder: Path.Combine(CurrentTestTempDirectory, "Out"),
                outputFileToken: "ModifiableGuard");

            using (var tx = new Transaction(doc, "Open Modifiable Transaction"))
            {
                tx.Start();
                Assert.IsTrue(doc.IsModifiable);

                MultiModelPdfDocumentOutcome outcome = adapter.ExportDocument(job);
                Assert.IsFalse(outcome.Succeeded);
                Assert.IsTrue(outcome.ErrorMessage!.Contains("open transaction"));

                tx.RollBack();
            }
        }

        [Test]
        public void CaptureSessionSnapshot_OnFamilyDocument_ReturnsZeroProjectDocuments()
        {
            Document projectDoc = OpenIsolatedTestTemplateCopy();
            Family editableFamily = new FilteredElementCollector(projectDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.IsEditable)
                ?? throw new AssertionException("Expected at least one editable family in isolated test template.");

            Document familyDoc = TrackDocument(projectDoc.EditFamily(editableFamily));
            Assert.IsTrue(familyDoc.IsFamilyDocument);

            var familyAdapter = new RevitMultiModelPdfExportAdapter(familyDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = familyAdapter.CaptureSessionSnapshot();

            Assert.AreEqual(0, snapshot.Documents.Count, "Family documents must yield 0 project rows.");
        }

        private static ExportFixtureSuite SeedExportFixtureSuite(Document doc)
        {
            using (var tx = new Transaction(doc, "Seed MultiModelPdfExport Fixture"))
            {
                tx.Start();
                try
                {
                    ElementId titleBlockId = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .FirstElementId() ?? ElementId.InvalidElementId;

                    ViewSheet sheetA101 = ViewSheet.Create(doc, titleBlockId);
                    sheetA101.SheetNumber = "PDF-101";
                    sheetA101.Name = "FLOOR PLAN LEVEL 1";

                    ViewSheet sheetA201 = ViewSheet.Create(doc, titleBlockId);
                    sheetA201.SheetNumber = "PDF-201";
                    sheetA201.Name = "EXTERIOR ELEVATIONS";

                    ViewSheet placeholderSheet = ViewSheet.CreatePlaceholder(doc);
                    placeholderSheet.SheetNumber = "PDF-900";
                    placeholderSheet.Name = "UNPLACED PLACEHOLDER";

                    ViewSchedule wallSchedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Walls));
                    wallSchedule.Name = "WALL SCHEDULE";

                    PrintManager printManager = doc.PrintManager;
                    printManager.PrintRange = PrintRange.Select;
                    printManager.Apply();
                    ViewSheetSetting setting = printManager.ViewSheetSetting;

                    // 1. Ordered manual Print Set: Sheet PDF-201 then Sheet PDF-101
                    var orderedSet = new ViewSet();
                    orderedSet.Insert(sheetA201);
                    orderedSet.Insert(sheetA101);
                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = orderedSet;
                    Assert.IsTrue(setting.SaveAs("CD Ordered Set"), "Failed to seed 'CD Ordered Set'.");

                    ViewSheetSet savedOrdered = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheetSet))
                        .Cast<ViewSheetSet>()
                        .First(s => string.Equals(s.Name, "CD Ordered Set", StringComparison.Ordinal));
                    savedOrdered.IsAutomatic = false;
                    savedOrdered.OrderedViewList = new List<View> { sheetA201, sheetA101 };

                    // 2. Mixed Print Set with printable sheet + unplaced placeholder sheet + non-printable schedule
                    var mixedSet = new ViewSet();
                    mixedSet.Insert(sheetA101);
                    mixedSet.Insert(placeholderSheet);
                    if (!wallSchedule.CanBePrinted)
                    {
                        mixedSet.Insert(wallSchedule);
                    }
                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = mixedSet;
                    Assert.IsTrue(setting.SaveAs("Mixed Skip Set"), "Failed to seed 'Mixed Skip Set'.");

                    // 3. Named ExportPDFSettings option
                    var pdfOpts = new PDFExportOptions
                    {
                        PaperFormat = ExportPaperFormat.ARCH_D,
                        ZoomType = ZoomType.Zoom,
                        ZoomPercentage = 100,
                        Combine = true
                    };
                    ExportPDFSettings.Create(doc, "INC_SmokeSetup", pdfOpts);

                    tx.Commit();

                    return new ExportFixtureSuite
                    {
                        SheetA101 = sheetA101,
                        SheetA201 = sheetA201,
                        PlaceholderSheet = placeholderSheet,
                        WallSchedule = wallSchedule
                    };
                }
                catch
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    throw;
                }
            }
        }

        private static void AssertPdfHeaderValid(string pdfPath)
        {
            Assert.IsTrue(File.Exists(pdfPath), $"Expected generated PDF at '{pdfPath}'.");
            byte[] bytes = File.ReadAllBytes(pdfPath);
            Assert.Greater(bytes.Length, 100, $"Expected non-trivial PDF size at '{pdfPath}'.");
            string header = Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length));
            Assert.AreEqual("%PDF-", header, $"Expected valid %PDF- magic header in '{pdfPath}'.");
        }

        private sealed class ExportFixtureSuite
        {
            public ViewSheet SheetA101 { get; set; } = null!;
            public ViewSheet SheetA201 { get; set; } = null!;
            public ViewSheet PlaceholderSheet { get; set; } = null!;
            public ViewSchedule WallSchedule { get; set; } = null!;
        }
    }
}
