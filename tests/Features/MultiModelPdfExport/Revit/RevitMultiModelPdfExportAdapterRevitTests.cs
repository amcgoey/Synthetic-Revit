using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Features.MultiModelPdfExport.Adapters;
using Synthetic.Features.MultiModelPdfExport.Models;
using Synthetic.Features.MultiModelPdfExport.Services;
using Synthetic.Platform.Host;
using Synthetic.Platform.Revit;
using Synthetic.Platform.Settings;
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

        [Test]
        public void CaptureSessionSnapshot_DiscoversOpenStandaloneTabs_ExcludesFamilyDocs_AndAvoidsUnsavedDetachedKeyThrow()
        {
            Document activeDoc = OpenIsolatedTestTemplateCopy();
            SeedExportFixtureSuite(activeDoc);
            string dirA = Path.Combine(CurrentTestTempDirectory, "TabA");
            Directory.CreateDirectory(dirA);
            activeDoc.SaveAs(Path.Combine(dirA, "SharedTitle.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            Document openTab = OpenIsolatedTestTemplateCopy();
            SeedSimpleSheetAndSet(openTab, "TAB-101", "OPEN TAB SHEET", "Tab Print Set");
            string dirB = Path.Combine(CurrentTestTempDirectory, "TabB");
            Directory.CreateDirectory(dirB);
            openTab.SaveAs(Path.Combine(dirB, "SharedTitle.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            Document unsavedTab1 = NewTrackedProjectDocument();
            Document unsavedTab2 = NewTrackedProjectDocument();

            Family editableFamily = new FilteredElementCollector(activeDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.IsEditable)
                ?? throw new AssertionException("Expected at least one editable family in isolated test template.");
            Document openFamilyDoc = TrackDocument(activeDoc.EditFamily(editableFamily));
            Assert.IsTrue(openFamilyDoc.IsFamilyDocument);

            int openDocsBefore = App.Documents.Size;

            var adapter = new RevitMultiModelPdfExportAdapter(activeDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            Assert.AreEqual(openDocsBefore, App.Documents.Size, "CaptureSessionSnapshot must not close or open documents.");
            Assert.IsFalse(activeDoc.IsModified);
            Assert.IsFalse(openTab.IsModified);

            Assert.AreEqual(MultiModelPdfDocumentOrigin.Active, snapshot.Documents[0].Origin);
            Assert.AreEqual(activeDoc.Title, snapshot.Documents[0].DocumentTitle);

            List<MultiModelPdfDocumentSnapshot> openSnapshots = snapshot.Documents
                .Where(d => d.Origin == MultiModelPdfDocumentOrigin.OpenStandalone)
                .ToList();
            Assert.AreEqual(3, openSnapshots.Count, "Expected open standalone project tabs (saved + 2 unsaved) without .rfa family doc.");

            Assert.IsFalse(
                snapshot.Documents.Any(d => string.Equals(d.DocumentTitle, openFamilyDoc.Title, StringComparison.OrdinalIgnoreCase)),
                "Open .rfa family document must be excluded from discovered project rows.");

            MultiModelPdfDocumentSnapshot? openSharedSnapshot = openSnapshots
                .FirstOrDefault(d => string.Equals(d.DisplayPath, openTab.PathName, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(openSharedSnapshot, "Expected saved open standalone tab in snapshot.");
            Assert.AreNotEqual(snapshot.Documents[0].DocumentKey, openSharedSnapshot!.DocumentKey);
            Assert.AreEqual(MultiModelPdfStorageKind.Local, openSharedSnapshot.Storage);
            Assert.AreEqual("Tab Print Set", openSharedSnapshot.PrintSets.Single(s => s.Name == "Tab Print Set").Name);

            var unsavedKeys = openSnapshots
                .Where(d => d.Storage == MultiModelPdfStorageKind.Unsaved)
                .Select(d => d.DocumentKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.AreEqual(2, unsavedKeys.Count, "Two unsaved open tabs must yield distinct deterministic DocumentKeys.");
        }

        [Test]
        public void CaptureSessionSnapshot_DetachedWorksharedCopies_NeverThrowAndYieldDistinctKeys()
        {
            Document seedDoc = OpenIsolatedTestTemplateCopy();
            Assert.IsTrue(seedDoc.CanEnableWorksharing(), "Isolated template should support enabling worksharing.");
            seedDoc.EnableWorksharing("Shared Levels and Grids", "Workset1");

            string centralPath = Path.Combine(CurrentTestTempDirectory, "CentralWorkshared.rvt");
            var worksharingSave = new WorksharingSaveAsOptions { SaveAsCentral = true };
            var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
            saveOpts.SetWorksharingOptions(worksharingSave);
            seedDoc.SaveAs(centralPath, saveOpts);
            seedDoc.Close(false);

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(centralPath);
            var detachPreserveOpts = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
            };

            Document activeDetached = TrackDocument(App.OpenDocumentFile(modelPath, detachPreserveOpts));
            Document openDetached = TrackDocument(App.OpenDocumentFile(modelPath, detachPreserveOpts));
            Assert.IsTrue(activeDetached.IsWorkshared && activeDetached.IsDetached);
            Assert.IsTrue(openDetached.IsWorkshared && openDetached.IsDetached);

            var adapter = new RevitMultiModelPdfExportAdapter(activeDetached, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            Assert.AreEqual(2, snapshot.Documents.Count, "Both detached workshared tabs must appear with distinct DocumentKeys.");
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Active, snapshot.Documents[0].Origin);
            Assert.AreEqual(MultiModelPdfDocumentOrigin.OpenStandalone, snapshot.Documents[1].Origin);
            Assert.AreNotEqual(snapshot.Documents[0].DocumentKey, snapshot.Documents[1].DocumentKey);
            StringAssert.StartsWith("detached:", snapshot.Documents[0].DocumentKey);
            StringAssert.StartsWith("detached:", snapshot.Documents[1].DocumentKey);
        }

        [Test]
        public void ExportDocument_MultiTabCombinedAndSeparate_RemapsCustomNamingRule_WarnsOnMissingParam_AndNeverClosesOpenTabs()
        {
            string previousSharedParamFile = App.SharedParametersFilename;
            string sharedParamPath = Path.Combine(CurrentTestTempDirectory, "TestSharedParams.txt");
            File.WriteAllText(sharedParamPath, string.Empty, Encoding.UTF8);

            try
            {
                App.SharedParametersFilename = sharedParamPath;
                DefinitionFile defFile = App.OpenSharedParameterFile();
                DefinitionGroup defGroup = defFile.Groups.Create("MultiModelPdfTests");

                Guid guidPrimary = Guid.NewGuid();
                Guid guidAltSameName = Guid.NewGuid();
                Guid guidDummy = Guid.NewGuid();

                var extDefPrimary = (ExternalDefinition)defGroup.Definitions.Create(
                    new ExternalDefinitionCreationOptions("SynPdfTag", SpecTypeId.String.Text) { GUID = guidPrimary });
                // Create a second group for the alternate-GUID parameter with the same Name.
                DefinitionGroup altGroup = defFile.Groups.Create("MultiModelPdfTestsAlt");
                var extDefSameNameDiffGuid = (ExternalDefinition)altGroup.Definitions.Create(
                    new ExternalDefinitionCreationOptions("SynPdfTag", SpecTypeId.String.Text) { GUID = guidAltSameName });
                var extDefDummy = (ExternalDefinition)altGroup.Definitions.Create(
                    new ExternalDefinitionCreationOptions("SynOffsetDummy", SpecTypeId.String.Text) { GUID = guidDummy });

                // Probe default PDFExportOptions naming rule in live Revit 2026
                var probeOptions = new PDFExportOptions();
                IList<TableCellCombinedParameterData> freshDefaultRule = probeOptions.GetNamingRule();
                TestContext.Progress.WriteLine($"Probe fresh new PDFExportOptions().GetNamingRule().Count = {freshDefaultRule?.Count ?? -1}");
                if (freshDefaultRule != null)
                {
                    foreach (TableCellCombinedParameterData cell in freshDefaultRule)
                    {
                        TestContext.Progress.WriteLine($"  DefaultRule entry: ParamId={cell.ParamId}, CategoryId={cell.CategoryId}, Sep='{cell.Separator}'");
                    }
                }

                // 1. Active Doc: has SynPdfTag (guidPrimary) + ExportPDFSettings "SynCustomNamingSetup"
                Document activeDoc = OpenIsolatedTestTemplateCopy();
                SeedSimpleSheetAndSet(activeDoc, "ACT-101", "ACTIVE SHEET", "Tab Set");
                SharedParameterElement activeParam = BindSheetSharedParamAndSetValue(activeDoc, extDefPrimary, "ACT-101", "ACTVAL");
                SeedCustomNamingPdfSetup(activeDoc, "SynCustomNamingSetup", activeParam.Id);
                string dirActive = Path.Combine(CurrentTestTempDirectory, "ProjActive");
                Directory.CreateDirectory(dirActive);
                activeDoc.SaveAs(Path.Combine(dirActive, "SameTitle.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

                // 2. Open Tab (GUID Match): binds dummy first so ElementId differs from activeDoc, then extDefPrimary (same GUID)
                Document guidTab = OpenIsolatedTestTemplateCopy();
                SeedSimpleSheetAndSet(guidTab, "GM-101", "GUID SHEET", "Tab Set");
                BindSheetSharedParamAndSetValue(guidTab, extDefDummy, "GM-101", "IGNORED");
                SharedParameterElement guidTabParam = BindSheetSharedParamAndSetValue(guidTab, extDefPrimary, "GM-101", "GUIDREMAPPED");
                Assert.AreNotEqual(activeParam.Id, guidTabParam.Id, "Test requires distinct ElementId across documents to verify real remapping.");
                string dirGuid = Path.Combine(CurrentTestTempDirectory, "ProjGuid");
                Directory.CreateDirectory(dirGuid);
                guidTab.SaveAs(Path.Combine(dirGuid, "SameTitle.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

                // 3. Open Tab (Name Match fallback): binds extDefSameNameDiffGuid (different GUID, same Definition.Name "SynPdfTag")
                Document nameTab = OpenIsolatedTestTemplateCopy();
                SeedSimpleSheetAndSet(nameTab, "NM-101", "NAME SHEET", "Tab Set");
                BindSheetSharedParamAndSetValue(nameTab, extDefSameNameDiffGuid, "NM-101", "NAMEREMAPPED");
                string dirName = Path.Combine(CurrentTestTempDirectory, "ProjName");
                Directory.CreateDirectory(dirName);
                nameTab.SaveAs(Path.Combine(dirName, "NameMatchDoc.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

                // 4. Open Tab (Missing Parameter): lacks SynPdfTag completely -> must fall back cleanly to default naming rule + warning
                Document missingTab = OpenIsolatedTestTemplateCopy();
                SeedSimpleSheetAndSet(missingTab, "MS-101", "MISSING FALLBACK SHEET", "Tab Set");
                string dirMissing = Path.Combine(CurrentTestTempDirectory, "ProjMissing");
                Directory.CreateDirectory(dirMissing);
                missingTab.SaveAs(Path.Combine(dirMissing, "MissingParamDoc.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

                int openDocsBeforeExport = App.Documents.Size;
                var adapter = new RevitMultiModelPdfExportAdapter(activeDoc, UiApp);
                MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

                MultiModelPdfDocumentSnapshot activeSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Active);
                MultiModelPdfDocumentSnapshot guidSnap = snapshot.Documents.Single(d => string.Equals(d.DisplayPath, guidTab.PathName, StringComparison.OrdinalIgnoreCase));
                MultiModelPdfDocumentSnapshot nameSnap = snapshot.Documents.Single(d => string.Equals(d.DisplayPath, nameTab.PathName, StringComparison.OrdinalIgnoreCase));
                MultiModelPdfDocumentSnapshot missingSnap = snapshot.Documents.Single(d => string.Equals(d.DisplayPath, missingTab.PathName, StringComparison.OrdinalIgnoreCase));

                IReadOnlyDictionary<string, MultiModelPdfResolvedToken> tokens = MultiModelPdfPathHelper.ResolveOutputTokens(
                    new[]
                    {
                        new MultiModelPdfTokenCandidate(activeSnap.DocumentKey, activeSnap.DocumentTitle, activeSnap.Origin),
                        new MultiModelPdfTokenCandidate(guidSnap.DocumentKey, guidSnap.DocumentTitle, guidSnap.Origin),
                        new MultiModelPdfTokenCandidate(nameSnap.DocumentKey, nameSnap.DocumentTitle, nameSnap.Origin),
                        new MultiModelPdfTokenCandidate(missingSnap.DocumentKey, missingSnap.DocumentTitle, missingSnap.Origin)
                    });

                Assert.AreEqual("SameTitle", tokens[activeSnap.DocumentKey].FinalToken);
                Assert.AreEqual("SameTitle_2", tokens[guidSnap.DocumentKey].FinalToken);

                // Separate mode batch across all 4 open tabs
                string separateOut = Path.Combine(CurrentTestTempDirectory, "MultiTabSeparateOut");
                var separateJobs = new[]
                {
                    CreateJob(activeSnap, "Tab Set", "SynCustomNamingSetup", MultiModelPdfFileMode.SeparatePerViewSheet, separateOut, tokens[activeSnap.DocumentKey].FinalToken),
                    CreateJob(guidSnap, "Tab Set", "SynCustomNamingSetup", MultiModelPdfFileMode.SeparatePerViewSheet, separateOut, tokens[guidSnap.DocumentKey].FinalToken),
                    CreateJob(nameSnap, "Tab Set", "SynCustomNamingSetup", MultiModelPdfFileMode.SeparatePerViewSheet, separateOut, tokens[nameSnap.DocumentKey].FinalToken),
                    CreateJob(missingSnap, "Tab Set", "SynCustomNamingSetup", MultiModelPdfFileMode.SeparatePerViewSheet, separateOut, tokens[missingSnap.DocumentKey].FinalToken)
                };

                var pipeline = new MultiModelPdfExportPipeline(adapter);
                MultiModelPdfRunSummary separateSummary = pipeline.Execute(
                    new MultiModelPdfRunRequest(separateJobs, separateOut, "SynCustomNamingSetup", MultiModelPdfFileMode.SeparatePerViewSheet));

                Assert.IsTrue(separateSummary.FailedDocumentCount == 0, string.Join(" | ", separateSummary.Errors));
                Assert.AreEqual(4, separateSummary.CompletedDocumentCount);
                Assert.AreEqual(4, separateSummary.GeneratedCount);

                AssertPdfHeaderValid(Path.Combine(separateOut, "SameTitle", "ACT-101_ACTVAL.pdf"));
                AssertPdfHeaderValid(Path.Combine(separateOut, "SameTitle_2", "GM-101_GUIDREMAPPED.pdf"));
                AssertPdfHeaderValid(Path.Combine(separateOut, "NameMatchDoc", "NM-101_NAMEREMAPPED.pdf"));

                string missingPdf = separateSummary.DocumentOutcomes.Single(o => o.DocumentKey == missingSnap.DocumentKey).GeneratedFilePaths.Single();
                AssertPdfHeaderValid(missingPdf);
                Assert.AreEqual(
                    "Sheet-MISSING FALLBACK SHEET.pdf",
                    Path.GetFileName(missingPdf),
                    "Expected fallback default naming rule to match Revit's built-in Separate-mode naming rule.");

                Assert.AreEqual(1, separateSummary.Warnings.Count, "Expected exactly one missing-parameter fallback warning (only MissingParamDoc).");
                StringAssert.Contains("MissingParamDoc", separateSummary.Warnings[0]);
                StringAssert.Contains("SynPdfTag", separateSummary.Warnings[0]);
                StringAssert.Contains("default", separateSummary.Warnings[0].ToLowerInvariant());

                // Combined mode across colliding titles (Active + OpenStandalone)
                string combinedOut = Path.Combine(CurrentTestTempDirectory, "MultiTabCombinedOut");
                var combinedJobs = new[]
                {
                    CreateJob(activeSnap, "Tab Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, combinedOut, tokens[activeSnap.DocumentKey].FinalToken),
                    CreateJob(guidSnap, "Tab Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, combinedOut, tokens[guidSnap.DocumentKey].FinalToken)
                };
                MultiModelPdfRunSummary combinedSummary = pipeline.Execute(
                    new MultiModelPdfRunRequest(combinedJobs, combinedOut, MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument));

                Assert.AreEqual(2, combinedSummary.CompletedDocumentCount);
                Assert.AreEqual(0, combinedSummary.FailedDocumentCount);
                AssertPdfHeaderValid(Path.Combine(combinedOut, "SameTitle.pdf"));
                AssertPdfHeaderValid(Path.Combine(combinedOut, "SameTitle_2.pdf"));

                // Zero accidental closure of pre-existing open standalone tabs
                Assert.AreEqual(openDocsBeforeExport, App.Documents.Size, "Open standalone tabs must NEVER be closed by Multi-Model PDF Export.");
                Assert.IsTrue(activeDoc.IsValidObject && !activeDoc.IsModified);
                Assert.IsTrue(guidTab.IsValidObject && !guidTab.IsModified);
                Assert.IsTrue(nameTab.IsValidObject && !nameTab.IsModified);
                Assert.IsTrue(missingTab.IsValidObject && !missingTab.IsModified);
            }
            finally
            {
                App.SharedParametersFilename = previousSharedParamFile ?? string.Empty;
            }
        }

        [Test]
        public void ExportDocument_MultiTabFaultIsolation_ContinuesWhenLockedPdfOrMissingPrintSetOccurs()
        {
            Document activeDoc = OpenIsolatedTestTemplateCopy();
            SeedSimpleSheetAndSet(activeDoc, "A-101", "ACTIVE", "Batch Set");
            activeDoc.SaveAs(GetTempFilePath("FaultActive.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            Document missingSetTab = OpenIsolatedTestTemplateCopy();
            SeedSimpleSheetAndSet(missingSetTab, "B-101", "OTHER", "Different Set");
            missingSetTab.SaveAs(GetTempFilePath("FaultMissingSet.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            Document lockedTab = OpenIsolatedTestTemplateCopy();
            SeedSimpleSheetAndSet(lockedTab, "C-101", "LOCKED", "Batch Set");
            lockedTab.SaveAs(GetTempFilePath("FaultLocked.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            Document healthyTab = OpenIsolatedTestTemplateCopy();
            SeedSimpleSheetAndSet(healthyTab, "D-101", "HEALTHY", "Batch Set");
            healthyTab.SaveAs(GetTempFilePath("FaultHealthy.rvt"), new SaveAsOptions { OverwriteExistingFile = true });

            int docsBefore = App.Documents.Size;
            var adapter = new RevitMultiModelPdfExportAdapter(activeDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            string outFolder = Path.Combine(CurrentTestTempDirectory, "FaultOut");
            Directory.CreateDirectory(outFolder);

            string lockedPdfPath = Path.Combine(outFolder, "FaultLocked.pdf");
            byte[] sentinelBytes = Encoding.ASCII.GetBytes("%PDF-1.4 SENTINEL-TAB-LOCKED\n");
            File.WriteAllBytes(lockedPdfPath, sentinelBytes);

            MultiModelPdfDocumentSnapshot activeSnap = snapshot.Documents.Single(d => d.DocumentTitle.StartsWith("FaultActive", StringComparison.OrdinalIgnoreCase));
            MultiModelPdfDocumentSnapshot missingSnap = snapshot.Documents.Single(d => d.DocumentTitle.StartsWith("FaultMissingSet", StringComparison.OrdinalIgnoreCase));
            MultiModelPdfDocumentSnapshot lockedSnap = snapshot.Documents.Single(d => d.DocumentTitle.StartsWith("FaultLocked", StringComparison.OrdinalIgnoreCase));
            MultiModelPdfDocumentSnapshot healthySnap = snapshot.Documents.Single(d => d.DocumentTitle.StartsWith("FaultHealthy", StringComparison.OrdinalIgnoreCase));

            var jobs = new[]
            {
                CreateJob(activeSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outFolder, "FaultActive"),
                CreateJob(missingSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outFolder, "FaultMissingSet"),
                CreateJob(lockedSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outFolder, "FaultLocked"),
                CreateJob(healthySnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outFolder, "FaultHealthy")
            };

            MultiModelPdfRunSummary summary;
            using (new FileStream(lockedPdfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var pipeline = new MultiModelPdfExportPipeline(adapter);
                summary = pipeline.Execute(new MultiModelPdfRunRequest(
                    jobs,
                    outFolder,
                    MultiModelPdfSetupOption.DefaultSetupId,
                    MultiModelPdfFileMode.CombinedPerDocument));
            }

            Assert.AreEqual(2, summary.CompletedDocumentCount, "Healthy open tabs must finish even when sibling tabs fail.");
            Assert.AreEqual(2, summary.FailedDocumentCount);
            AssertPdfHeaderValid(Path.Combine(outFolder, "FaultActive.pdf"));
            AssertPdfHeaderValid(Path.Combine(outFolder, "FaultHealthy.pdf"));
            CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(lockedPdfPath), "Locked target PDF must stay intact.");
            Assert.AreEqual(2, summary.Errors.Count);
            Assert.IsTrue(summary.Errors.Any(e => e.Contains("Batch Set")));
            Assert.AreEqual(docsBefore, App.Documents.Size, "Open tabs must remain open after fault-isolated export.");
        }

        [Test]
        public void Spike_DirectOpenLoadedLocalLink_ObservesRevitBehaviorWithoutLeakingDocs()
        {
            string localLinkPath = Path.Combine(CurrentTestTempDirectory, "SpikeLocalLink.rvt");
            Document seedLink = OpenIsolatedTestTemplateCopy(targetFileName: "SpikeLocalLinkSeed.rvt");
            SeedSimpleSheetAndSet(seedLink, "SL-101", "SPIKE LINK SHEET", "Spike Set");
            seedLink.SaveAs(localLinkPath, new SaveAsOptions { OverwriteExistingFile = true });
            seedLink.Close(false);

            string hostPath = Path.Combine(CurrentTestTempDirectory, "SpikeHost.rvt");
            Document hostSeed = OpenIsolatedTestTemplateCopy(targetFileName: "SpikeHostSeed.rvt");
            RevitLinkType linkType;
            using (var tx = new Transaction(hostSeed, "Link Local Model"))
            {
                tx.Start();
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath);
                LinkLoadResult loadResult = RevitLinkType.Create(hostSeed, mp, new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, loadResult.ElementId);
                linkType = (RevitLinkType)hostSeed.GetElement(loadResult.ElementId);
                tx.Commit();
            }
            hostSeed.SaveAs(hostPath, new SaveAsOptions { OverwriteExistingFile = true });
            hostSeed.Close(false);

            Document hostDoc = TrackDocument(App.OpenDocumentFile(hostPath));
            RevitLinkType hostLinkType = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .First();
            Assert.AreEqual(LinkedFileStatus.Loaded, hostLinkType.GetLinkedFileStatus());
            Assert.IsFalse(hostDoc.IsModified);

            Document? directOpened = null;
            string directResult;
            LinkedFileStatus statusAfterDirect;
            bool modifiedAfterDirect;
            try
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath);
                directOpened = App.OpenDocumentFile(mp, new OpenOptions());
                directResult = directOpened != null
                    ? $"OPENED:{directOpened.Title}(IsLinked={directOpened.IsLinked})"
                    : "RETURNED_NULL";
                statusAfterDirect = hostLinkType.GetLinkedFileStatus();
                modifiedAfterDirect = hostDoc.IsModified;
            }
            catch (Exception ex)
            {
                directResult = $"THREW:{ex.GetType().Name}:{ex.Message}";
                statusAfterDirect = hostLinkType.GetLinkedFileStatus();
                modifiedAfterDirect = hostDoc.IsModified;
            }
            finally
            {
                if (directOpened != null && directOpened.IsValidObject && !directOpened.IsLinked)
                {
                    directOpened.Close(false);
                }
            }

            string summaryText = $"[SPIKE_DIRECT_LOCAL_LINK] Result={directResult}; LinkStatusAfter={statusAfterDirect}; HostIsModified={modifiedAfterDirect}";
            Console.WriteLine(summaryText);
            TestContext.Progress.WriteLine(summaryText);
            Assert.Pass(summaryText);
        }

        [Test]
        public void CaptureSessionSnapshot_FederatedSession_ExcludesNestedSurfacesUnloadedAndDeduplicatesByPrecedence()
        {
            string linksDir = Path.Combine(CurrentTestTempDirectory, "FedLinks");
            Directory.CreateDirectory(linksDir);

            // 1. Nested link model (attached inside LoadedLocalLink so IsNestedLink == true in Host)
            string nestedPath = Path.Combine(linksDir, "NestedChild.rvt");
            Document nestedDoc = OpenIsolatedTestTemplateCopy(targetFileName: "NestedSeed.rvt");
            SeedSimpleSheetAndSet(nestedDoc, "N-101", "NESTED SHEET", "Shared Set");
            nestedDoc.SaveAs(nestedPath, new SaveAsOptions { OverwriteExistingFile = true });
            nestedDoc.Close(false);

            // 2. Loaded local non-workshared link (attaches NestedChild)
            string localLinkPath = Path.Combine(linksDir, "LoadedLocalLink.rvt");
            Document localLinkDoc = OpenIsolatedTestTemplateCopy(targetFileName: "LoadedLocalSeed.rvt");
            SeedSimpleSheetAndSet(localLinkDoc, "LL-101", "LOCAL LINK SHEET", "Shared Set");
            using (var tx = new Transaction(localLinkDoc, "Attach Nested Link"))
            {
                tx.Start();
                ModelPath nestedMp = ModelPathUtils.ConvertUserVisiblePathToModelPath(nestedPath);
                LinkLoadResult nestedRes = RevitLinkType.Create(localLinkDoc, nestedMp, new RevitLinkOptions(false));
                RevitLinkInstance.Create(localLinkDoc, nestedRes.ElementId);
                var nestedType = (RevitLinkType)localLinkDoc.GetElement(nestedRes.ElementId);
                nestedType.AttachmentType = AttachmentType.Attachment;
                tx.Commit();
            }
            localLinkDoc.SaveAs(localLinkPath, new SaveAsOptions { OverwriteExistingFile = true });
            localLinkDoc.Close(false);

            // 3. Loaded workshared central link (placed twice to verify GetTypeId grouping)
            string centralLinkPath = Path.Combine(linksDir, "WorksharedLinkCentral.rvt");
            Document centralSeed = OpenIsolatedTestTemplateCopy(targetFileName: "WorksharedSeed.rvt");
            Assert.IsTrue(centralSeed.CanEnableWorksharing());
            centralSeed.EnableWorksharing("Shared Levels and Grids", "Workset1");
            SeedSimpleSheetAndSet(centralSeed, "WS-101", "WORKSHARED LINK SHEET", "Shared Set");
            var saveCentralOpts = new SaveAsOptions { OverwriteExistingFile = true };
            saveCentralOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
            centralSeed.SaveAs(centralLinkPath, saveCentralOpts);
            centralSeed.Close(false);

            // 4. Overlapping workshared central (both linked in host AND open as a standalone local replica tab)
            string overlapCentralPath = Path.Combine(linksDir, "OverlapModel.rvt");
            Document overlapSeed = OpenIsolatedTestTemplateCopy(targetFileName: "OverlapSeed.rvt");
            overlapSeed.EnableWorksharing("Shared Levels and Grids", "Workset1");
            SeedSimpleSheetAndSet(overlapSeed, "OV-101", "OVERLAP SHEET", "Shared Set");
            overlapSeed.SaveAs(overlapCentralPath, saveCentralOpts);
            overlapSeed.Close(false);

            string overlapLocalCopyPath = Path.Combine(CurrentTestTempDirectory, "OverlapLocalTab.rvt");
            WorksharingUtils.CreateNewLocal(
                ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapCentralPath),
                ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapLocalCopyPath));
            Document overlapOpenTab = TrackDocument(App.OpenDocumentFile(overlapLocalCopyPath));

            // 5. Unloaded local link
            string unloadedPath = Path.Combine(linksDir, "UnloadedSite.rvt");
            Document unloadedSeed = OpenIsolatedTestTemplateCopy(targetFileName: "UnloadedSeed.rvt");
            SeedSimpleSheetAndSet(unloadedSeed, "UL-101", "UNLOADED SHEET", "Shared Set");
            unloadedSeed.SaveAs(unloadedPath, new SaveAsOptions { OverwriteExistingFile = true });
            unloadedSeed.Close(false);

            // 6. Active Host document
            string hostPath = Path.Combine(CurrentTestTempDirectory, "FederatedHost.rvt");
            Document hostSeed = OpenIsolatedTestTemplateCopy(targetFileName: "FederatedHostSeed.rvt");
            SeedSimpleSheetAndSet(hostSeed, "H-101", "HOST SHEET", "Shared Set");
            ElementId unloadedTypeId;
            using (var tx = new Transaction(hostSeed, "Link Federated Models"))
            {
                tx.Start();
                LinkLoadResult localRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, localRes.ElementId);

                LinkLoadResult wsRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(centralLinkPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, wsRes.ElementId);
                RevitLinkInstance.Create(hostSeed, wsRes.ElementId); // Second instance of same type

                LinkLoadResult overlapRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapCentralPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, overlapRes.ElementId);

                LinkLoadResult unloadedRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(unloadedPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, unloadedRes.ElementId);
                unloadedTypeId = unloadedRes.ElementId;
                tx.Commit();
            }

            var unloadedType = (RevitLinkType)hostSeed.GetElement(unloadedTypeId);
            unloadedType.Unload(null);
            hostSeed.SaveAs(hostPath, new SaveAsOptions { OverwriteExistingFile = true });
            hostSeed.Close(false);

            Document hostDoc = TrackDocument(App.OpenDocumentFile(hostPath));
            int docsBefore = App.Documents.Size;

            var adapter = new RevitMultiModelPdfExportAdapter(hostDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            Assert.AreEqual(docsBefore, App.Documents.Size, "CaptureSessionSnapshot must not open or close Revit documents.");
            Assert.IsFalse(hostDoc.IsModified, "CaptureSessionSnapshot must leave host document unmodified.");

            // Verify nested link exclusion
            Assert.IsFalse(
                snapshot.Documents.Any(d => d.DocumentTitle.IndexOf("NestedChild", StringComparison.OrdinalIgnoreCase) >= 0),
                "Nested links (RevitLinkType.IsNestedLink) must be excluded from discovery.");

            // Verify exact Active -> Linked -> OpenStandalone ordering and deduplication (Active + 3 Linked + 1 OpenStandalone = 5)
            Assert.AreEqual(5, snapshot.Documents.Count, "Expected Active + 3 top-level Linked (2 loaded + 1 unloaded) + 1 deduplicated OpenStandalone.");
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Active, snapshot.Documents[0].Origin);
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Linked, snapshot.Documents[1].Origin);
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Linked, snapshot.Documents[2].Origin);
            Assert.AreEqual(MultiModelPdfDocumentOrigin.Linked, snapshot.Documents[3].Origin);
            Assert.AreEqual(MultiModelPdfDocumentOrigin.OpenStandalone, snapshot.Documents[4].Origin);

            // Overlapping model must appear ONLY as OpenStandalone (Active > OpenStandalone > Linked)
            MultiModelPdfDocumentSnapshot overlapSnap = snapshot.Documents[4];
            Assert.AreEqual(MultiModelPdfDocumentOrigin.OpenStandalone, overlapSnap.Origin);
            Assert.IsFalse(overlapSnap.IsUnloadedLink);
            Assert.IsTrue(overlapSnap.PrintSets.Any(ps => ps.Name == "Shared Set" && ps.PrintableViewCount == 1));
            Assert.IsFalse(
                snapshot.Documents.Any(d => d.Origin == MultiModelPdfDocumentOrigin.Linked &&
                                            d.DocumentTitle.IndexOf("Overlap", StringComparison.OrdinalIgnoreCase) >= 0),
                "Overlapping linked model must be deduplicated in favor of OpenStandalone.");

            // Loaded links have harvested Print Sets
            MultiModelPdfDocumentSnapshot localLinkSnap = snapshot.Documents
                .Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.DocumentTitle.IndexOf("LoadedLocalLink", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsFalse(localLinkSnap.IsUnloadedLink);
            Assert.AreEqual(MultiModelPdfStorageKind.Local, localLinkSnap.Storage);
            Assert.IsTrue(localLinkSnap.PrintSets.Any(ps => ps.Name == "Shared Set" && ps.PrintableViewCount == 1));

            MultiModelPdfDocumentSnapshot wsLinkSnap = snapshot.Documents
                .Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.DocumentTitle.IndexOf("WorksharedLinkCentral", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsFalse(wsLinkSnap.IsUnloadedLink);
            Assert.AreEqual(MultiModelPdfStorageKind.Workshared, wsLinkSnap.Storage);
            Assert.IsTrue(wsLinkSnap.PrintSets.Any(ps => ps.Name == "Shared Set" && ps.PrintableViewCount == 1));

            // Unloaded link surfaced with IsUnloadedLink = true and empty PrintSets
            MultiModelPdfDocumentSnapshot unloadedSnap = snapshot.Documents
                .Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.IsUnloadedLink);
            Assert.IsTrue(unloadedSnap.DocumentTitle.IndexOf("UnloadedSite", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsEmpty(unloadedSnap.PrintSets);

            Assert.IsTrue(overlapOpenTab.IsValidObject);
        }

        [Test]
        public void ExportDocument_FederatedBatch_CreatesPdfs_SuppressesToastsRoomsAndDialogs_PreservesLinksUndoAndZeroLeaks()
        {
            string linksDir = Path.Combine(CurrentTestTempDirectory, "BatchLinks");
            Directory.CreateDirectory(linksDir);

            string missingRefPath = Path.Combine(linksDir, "MissingChildRef.rvt");
            Document missingRefDoc = OpenIsolatedTestTemplateCopy(targetFileName: "MissingRefSeed.rvt");
            missingRefDoc.SaveAs(missingRefPath, new SaveAsOptions { OverwriteExistingFile = true });
            missingRefDoc.Close(false);

            string driftedSettingsJson = Path.Combine(CurrentTestTempDirectory, "DriftedLinkSync.json");
            File.WriteAllText(driftedSettingsJson, JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "Standards", new { StandardsFilePath = @"C:\Standards\federated-drift.json" } }
            }));

            // 1. Loaded local link containing:
            //    - drifted SyncSettings (would spawn SyncToastNotification without suppression),
            //    - missing overlay child ref (fires TaskDialog_Unresolved_References on open),
            //    - unenclosed Room on a FloorPlan placed on sheet (exercises RoomNotEnclosed* suppression).
            string localLinkPath = Path.Combine(linksDir, "LocalWithRoomsAndRefs.rvt");
            Document localSeed = OpenIsolatedTestTemplateCopy(targetFileName: "LocalLinkSeed.rvt");
            SeedSimpleSheetAndSetWithUnenclosedRoom(localSeed, "L-101", "LOCAL ROOM SHEET", "Batch Set");
            using (var tx = new Transaction(localSeed, "Seed SyncSettings and Overlay Link"))
            {
                tx.Start();
                SettingsManager.Save(localSeed, new SyncSettings { LinkedFilePath = driftedSettingsJson });
                LinkLoadResult missingRes = RevitLinkType.Create(
                    localSeed,
                    ModelPathUtils.ConvertUserVisiblePathToModelPath(missingRefPath),
                    new RevitLinkOptions(false));
                RevitLinkInstance.Create(localSeed, missingRes.ElementId);
                var missingType = (RevitLinkType)localSeed.GetElement(missingRes.ElementId);
                missingType.AttachmentType = AttachmentType.Overlay;
                tx.Commit();
            }
            localSeed.SaveAs(localLinkPath, new SaveAsOptions { OverwriteExistingFile = true });
            localSeed.Close(false);
            SettingsManager.ClearCache();

            // Delete MissingChildRef.rvt from disk so opening LocalWithRoomsAndRefs standalone triggers TaskDialog_Unresolved_References
            File.Delete(missingRefPath);

            // 2. Loaded workshared central link with drifted SyncSettings
            string wsCentralPath = Path.Combine(linksDir, "WorksharedChildCentral.rvt");
            Document wsSeed = OpenIsolatedTestTemplateCopy(targetFileName: "WorksharedChildSeed.rvt");
            wsSeed.EnableWorksharing("Shared Levels and Grids", "Workset1");
            SeedSimpleSheetAndSet(wsSeed, "W-101", "WORKSHARED SHEET", "Batch Set");
            using (var tx = new Transaction(wsSeed, "Seed WS SyncSettings"))
            {
                tx.Start();
                SettingsManager.Save(wsSeed, new SyncSettings { LinkedFilePath = driftedSettingsJson });
                tx.Commit();
            }
            var saveCentralOpts = new SaveAsOptions { OverwriteExistingFile = true };
            saveCentralOpts.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
            wsSeed.SaveAs(wsCentralPath, saveCentralOpts);
            wsSeed.Close(false);

            // 3. Overlapping open standalone tab + linked central
            string overlapCentralPath = Path.Combine(linksDir, "OverlapCentralBatch.rvt");
            Document overlapSeed = OpenIsolatedTestTemplateCopy(targetFileName: "OverlapBatchSeed.rvt");
            overlapSeed.EnableWorksharing("Shared Levels and Grids", "Workset1");
            SeedSimpleSheetAndSet(overlapSeed, "O-101", "OVERLAP BATCH SHEET", "Batch Set");
            overlapSeed.SaveAs(overlapCentralPath, saveCentralOpts);
            overlapSeed.Close(false);

            string overlapLocalPath = Path.Combine(CurrentTestTempDirectory, "OverlapLocalBatchTab.rvt");
            WorksharingUtils.CreateNewLocal(
                ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapCentralPath),
                ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapLocalPath));
            Document overlapTab = TrackDocument(App.OpenDocumentFile(overlapLocalPath));

            // 4. Host model linking all 3
            string hostPath = Path.Combine(CurrentTestTempDirectory, "HostBatch.rvt");
            Document hostSeed = OpenIsolatedTestTemplateCopy(targetFileName: "HostBatchSeed.rvt");
            SeedSimpleSheetAndSet(hostSeed, "A-101", "HOST BATCH SHEET", "Batch Set");
            using (var tx = new Transaction(hostSeed, "Link Batch Models"))
            {
                tx.Start();
                LinkLoadResult localRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, localRes.ElementId);

                LinkLoadResult wsRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(wsCentralPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, wsRes.ElementId);

                LinkLoadResult ovRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(overlapCentralPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, ovRes.ElementId);
                tx.Commit();
            }
            hostSeed.SaveAs(hostPath, new SaveAsOptions { OverwriteExistingFile = true });
            hostSeed.Close(false);

            Document hostDoc = TrackDocument(App.OpenDocumentFile(hostPath));
            Assert.IsFalse(hostDoc.IsModified, "Re-opened host document must start clean.");
            DocumentVersion hostVersionBefore = Document.GetDocumentVersion(hostDoc);

            List<RevitLinkType> hostLinkTypes = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .Where(lt => !lt.IsNestedLink)
                .ToList();
            List<RevitLinkInstance> hostLinkInstances = new FilteredElementCollector(hostDoc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>()
                .Where(li => hostLinkTypes.Any(lt => lt.Id == li.GetTypeId()))
                .ToList();
            Assert.AreEqual(3, hostLinkTypes.Count);
            Assert.IsTrue(hostLinkTypes.All(lt => lt.GetLinkedFileStatus() == LinkedFileStatus.Loaded));
            Assert.IsTrue(hostLinkInstances.All(li => li.GetLinkDocument() != null && li.GetLinkDocument()!.IsValidObject));

            var adapter = new RevitMultiModelPdfExportAdapter(hostDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            MultiModelPdfDocumentSnapshot activeSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Active);
            MultiModelPdfDocumentSnapshot localLinkSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.Storage == MultiModelPdfStorageKind.Local);
            MultiModelPdfDocumentSnapshot wsLinkSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.Storage == MultiModelPdfStorageKind.Workshared);
            MultiModelPdfDocumentSnapshot openTabSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.OpenStandalone);

            string outDir = Path.Combine(CurrentTestTempDirectory, "FederatedOut");
            var jobs = new[]
            {
                CreateJob(activeSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "HostBatch"),
                CreateJob(localLinkSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "LocalWithRoomsAndRefs"),
                CreateJob(wsLinkSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "WorksharedChildCentral"),
                CreateJob(openTabSnap, "Batch Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "OverlapLocalBatchTab")
            };

            int docsBeforeExport = App.Documents.Size;
            var hostApp = new App();
            EventHandler<DocumentOpenedEventArgs> docOpenedHandler = hostApp.OnDocumentOpen;
            App.DocumentOpened += docOpenedHandler;

            var hostChangedTxNames = new List<string>();
            EventHandler<DocumentChangedEventArgs> docChangedHandler = (_, changedArgs) =>
            {
                Document changedDoc = changedArgs.GetDocument();
                if (changedDoc != null && changedDoc.IsValidObject && changedDoc.Equals(hostDoc))
                {
                    hostChangedTxNames.AddRange(changedArgs.GetTransactionNames());
                }
            };
            App.DocumentChanged += docChangedHandler;

            bool previousSuppressDiskFallback = SettingsRuntimeOptions.SuppressFirmwideDiskFallback;
            SettingsRuntimeOptions.SuppressFirmwideDiskFallback = true;

            int spawnedToastCount = 0;
            MultiModelPdfRunSummary summary;
            try
            {
                var pipeline = new MultiModelPdfExportPipeline(adapter);
                summary = pipeline.Execute(new MultiModelPdfRunRequest(
                    jobs,
                    outDir,
                    MultiModelPdfSetupOption.DefaultSetupId,
                    MultiModelPdfFileMode.CombinedPerDocument));

                if (System.Windows.Application.Current != null)
                {
                    spawnedToastCount = System.Windows.Application.Current.Windows
                        .OfType<Synthetic.Features.SettingsDashboard.Views.SyncToastNotification>()
                        .Count();
                }
            }
            finally
            {
                App.DocumentChanged -= docChangedHandler;
                App.DocumentOpened -= docOpenedHandler;
                SettingsRuntimeOptions.SuppressFirmwideDiskFallback = previousSuppressDiskFallback;
                if (System.Windows.Application.Current != null)
                {
                    foreach (var toast in System.Windows.Application.Current.Windows
                        .OfType<Synthetic.Features.SettingsDashboard.Views.SyncToastNotification>()
                        .ToList())
                    {
                        toast.Close();
                    }
                }
            }

            Assert.AreEqual(0, summary.FailedDocumentCount, string.Join(" | ", summary.Errors));
            Assert.AreEqual(4, summary.CompletedDocumentCount);
            Assert.AreEqual(4, summary.GeneratedCount);

            AssertPdfHeaderValid(Path.Combine(outDir, "HostBatch.pdf"));
            AssertPdfHeaderValid(Path.Combine(outDir, "LocalWithRoomsAndRefs.pdf"));
            AssertPdfHeaderValid(Path.Combine(outDir, "WorksharedChildCentral.pdf"));
            AssertPdfHeaderValid(Path.Combine(outDir, "OverlapLocalBatchTab.pdf"));

            Assert.AreEqual(0, spawnedToastCount, "Background linked-model opens must spawn 0 SyncToastNotification windows.");
            Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
            Assert.AreEqual(0, SettingsRuntimeOptions.SyncDriftSuppressionDepth);

            // Host links remain loaded, host document clean, DocumentVersion untouched, open tab still open, zero doc leaks
            Assert.IsTrue(hostLinkTypes.All(lt => lt.GetLinkedFileStatus() == LinkedFileStatus.Loaded), "Exporting linked models must NEVER unload host RevitLinkTypes.");
            Assert.IsTrue(hostLinkInstances.All(li => li.GetLinkDocument() != null && li.GetLinkDocument()!.IsValidObject), "Host link instances must remain loaded.");
            Assert.IsFalse(hostDoc.IsModified, "Host document must remain clean after linked-model export.");
            Assert.IsEmpty(hostChangedTxNames, "Host document must record zero DocumentChanged transactions or Undo entries during linked-model export.");
            Assert.IsTrue(hostVersionBefore.IsEqual(Document.GetDocumentVersion(hostDoc)), "Host DocumentVersion (VersionGUID + NumberOfSaves) must remain untouched after linked-model export.");
            Assert.IsTrue(overlapTab.IsValidObject, "Open standalone tab must remain open.");
            Assert.AreEqual(docsBeforeExport, App.Documents.Size, "Opened standalone linked documents must all close cleanly without leaking.");
        }

        [Test]
        public void ExportDocument_LinkedModelFailurePaths_NeverLeakOpenedDocsOrTempDirs()
        {
            string linksDir = Path.Combine(CurrentTestTempDirectory, "FailLinks");
            Directory.CreateDirectory(linksDir);

            string localLinkPath = Path.Combine(linksDir, "FailLocalLink.rvt");
            Document localSeed = OpenIsolatedTestTemplateCopy(targetFileName: "FailLocalSeed.rvt");
            SeedSimpleSheetAndSet(localSeed, "FL-101", "FAIL LOCAL SHEET", "Real Set");
            localSeed.SaveAs(localLinkPath, new SaveAsOptions { OverwriteExistingFile = true });
            localSeed.Close(false);

            string unloadedLinkPath = Path.Combine(linksDir, "FailUnloadedLink.rvt");
            Document unloadedSeed = OpenIsolatedTestTemplateCopy(targetFileName: "FailUnloadedSeed.rvt");
            SeedSimpleSheetAndSet(unloadedSeed, "FU-101", "FAIL UNLOADED SHEET", "Real Set");
            unloadedSeed.SaveAs(unloadedLinkPath, new SaveAsOptions { OverwriteExistingFile = true });
            unloadedSeed.Close(false);

            string hostPath = Path.Combine(CurrentTestTempDirectory, "FailHost.rvt");
            Document hostSeed = OpenIsolatedTestTemplateCopy(targetFileName: "FailHostSeed.rvt");
            ElementId unloadedTypeId;
            using (var tx = new Transaction(hostSeed, "Link Fail Test Models"))
            {
                tx.Start();
                LinkLoadResult localRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(localLinkPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, localRes.ElementId);

                LinkLoadResult unloadedRes = RevitLinkType.Create(hostSeed, ModelPathUtils.ConvertUserVisiblePathToModelPath(unloadedLinkPath), new RevitLinkOptions(false));
                RevitLinkInstance.Create(hostSeed, unloadedRes.ElementId);
                unloadedTypeId = unloadedRes.ElementId;
                tx.Commit();
            }
            ((RevitLinkType)hostSeed.GetElement(unloadedTypeId)).Unload(null);
            hostSeed.SaveAs(hostPath, new SaveAsOptions { OverwriteExistingFile = true });
            hostSeed.Close(false);

            Document hostDoc = TrackDocument(App.OpenDocumentFile(hostPath));
            var adapter = new RevitMultiModelPdfExportAdapter(hostDoc, UiApp);
            MultiModelPdfSessionSnapshot snapshot = adapter.CaptureSessionSnapshot();

            MultiModelPdfDocumentSnapshot loadedLinkSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && !d.IsUnloadedLink);
            MultiModelPdfDocumentSnapshot unloadedLinkSnap = snapshot.Documents.Single(d => d.Origin == MultiModelPdfDocumentOrigin.Linked && d.IsUnloadedLink);

            int docsBefore = App.Documents.Size;
            var linkCopyDirsBefore = new HashSet<string>(
                Directory.GetDirectories(Path.GetTempPath(), "SyntheticPdfLinkCopy_*"),
                StringComparer.OrdinalIgnoreCase);
            string outDir = Path.Combine(CurrentTestTempDirectory, "FailOut");
            Directory.CreateDirectory(outDir);

            // 1. Missing Print Set on loaded linked model -> closes opened standalone doc in finally
            MultiModelPdfDocumentOutcome missingSetOutcome = adapter.ExportDocument(
                CreateJob(loadedLinkSnap, "NonExistentSet", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "FailLocalLink"));
            Assert.IsFalse(missingSetOutcome.Succeeded);
            StringAssert.Contains("NonExistentSet", missingSetOutcome.ErrorMessage!);
            Assert.AreEqual(docsBefore, App.Documents.Size, "Opened linked standalone doc must close when Print Set is missing.");
            Assert.IsEmpty(Directory.GetDirectories(Path.GetTempPath(), "SyntheticPdfLinkCopy_*").Where(d => !linkCopyDirsBefore.Contains(d)).ToList(),
                "Scoped temp link copy directory must be deleted on failure.");
            Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);

            // 2. Locked target PDF on loaded linked model -> closes opened standalone doc in finally and preserves sentinel
            string lockedPdfPath = Path.Combine(outDir, "FailLocalLink.pdf");
            byte[] sentinelBytes = Encoding.ASCII.GetBytes("%PDF-1.4 SENTINEL-LINK-LOCKED\n");
            File.WriteAllBytes(lockedPdfPath, sentinelBytes);

            MultiModelPdfDocumentOutcome lockedOutcome;
            using (new FileStream(lockedPdfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                lockedOutcome = adapter.ExportDocument(
                    CreateJob(loadedLinkSnap, "Real Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "FailLocalLink"));
            }

            Assert.IsFalse(lockedOutcome.Succeeded);
            CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(lockedPdfPath));
            Assert.AreEqual(docsBefore, App.Documents.Size, "Opened linked standalone doc must close when target PDF is locked.");
            Assert.IsEmpty(Directory.GetDirectories(Path.GetTempPath(), "SyntheticPdfLinkCopy_*").Where(d => !linkCopyDirsBefore.Contains(d)).ToList(),
                "Scoped temp link copy directory must be deleted when PDF write fails.");
            Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);

            // 3. Unloaded link job -> fails cleanly without opening or leaking
            MultiModelPdfDocumentOutcome unloadedOutcome = adapter.ExportDocument(
                CreateJob(unloadedLinkSnap, "Real Set", MultiModelPdfSetupOption.DefaultSetupId, MultiModelPdfFileMode.CombinedPerDocument, outDir, "FailUnloadedLink"));
            Assert.IsFalse(unloadedOutcome.Succeeded);
            Assert.AreEqual(docsBefore, App.Documents.Size);
            Assert.IsEmpty(Directory.GetDirectories(Path.GetTempPath(), "SyntheticPdfLinkCopy_*").Where(d => !linkCopyDirsBefore.Contains(d)).ToList(),
                "Scoped temp link copy directory must not remain after unloaded link failure.");
            Assert.IsFalse(SettingsRuntimeOptions.SuppressSyncDriftNotifications);
        }

        private static void SeedSimpleSheetAndSetWithUnenclosedRoom(Document doc, string sheetNumber, string sheetName, string printSetName)
        {
            CompositeFailuresPreprocessor roomWarningSuppressor = new FailurePipelineBuilder()
                .OnFailure(BuiltInFailures.RoomFailures.RoomNotEnclosed)
                .Build();

            using (var tx = new Transaction(doc, "Seed Sheet With Unenclosed Room and Print Set"))
            {
                tx.Start();
                try
                {
                    FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(roomWarningSuppressor);
                    tx.SetFailureHandlingOptions(fho);

                    Level level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .First();

                    ElementId planTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeFloorPlan);
                    if (planTypeId == ElementId.InvalidElementId)
                    {
                        planTypeId = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .First(vft => vft.ViewFamily == ViewFamily.FloorPlan)
                            .Id;
                    }

                    ViewPlan plan = ViewPlan.Create(doc, planTypeId, level.Id);
                    plan.Name = "PLAN WITH UNENCLOSED ROOM";
                    plan.DisplayStyle = DisplayStyle.Shading;

                    // Create an unenclosed room on the plan's level (triggers RoomNotEnclosed warning on commit/export)
                    doc.Create.NewRoom(level, new UV(0, 0));

                    ElementId titleBlockId = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .FirstElementId() ?? ElementId.InvalidElementId;

                    ViewSheet sheet = ViewSheet.Create(doc, titleBlockId);
                    sheet.SheetNumber = sheetNumber;
                    sheet.Name = sheetName;
                    if (Viewport.CanAddViewToSheet(doc, sheet.Id, plan.Id))
                    {
                        Viewport.Create(doc, sheet.Id, plan.Id, XYZ.Zero);
                    }

                    PrintManager pm = doc.PrintManager;
                    pm.PrintRange = PrintRange.Select;
                    pm.Apply();
                    ViewSheetSetting setting = pm.ViewSheetSetting;
                    var viewSet = new ViewSet();
                    viewSet.Insert(sheet);
                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = viewSet;
                    Assert.IsTrue(setting.SaveAs(printSetName), $"Failed to save Print Set '{printSetName}'.");
                    tx.Commit();
                }
                catch
                {
                    if (tx.HasStarted()) tx.RollBack();
                    throw;
                }
            }
        }

        private static MultiModelPdfDocumentJob CreateJob(
            MultiModelPdfDocumentSnapshot docSnapshot,
            string printSetName,
            string pdfSetupId,
            MultiModelPdfFileMode fileMode,
            string outputFolder,
            string outputFileToken)
        {
            return new MultiModelPdfDocumentJob(
                documentKey: docSnapshot.DocumentKey,
                documentTitle: docSnapshot.DocumentTitle,
                origin: docSnapshot.Origin,
                storage: docSnapshot.Storage,
                printSetName: printSetName,
                pdfSetupId: pdfSetupId,
                fileMode: fileMode,
                outputFolder: outputFolder,
                outputFileToken: outputFileToken);
        }

        private static void SeedSimpleSheetAndSet(Document doc, string sheetNumber, string sheetName, string printSetName)
        {
            using (var tx = new Transaction(doc, "Seed Sheet and Print Set"))
            {
                tx.Start();
                try
                {
                    ElementId titleBlockId = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .FirstElementId() ?? ElementId.InvalidElementId;

                    ViewSheet sheet = ViewSheet.Create(doc, titleBlockId);
                    sheet.SheetNumber = sheetNumber;
                    sheet.Name = sheetName;

                    PrintManager pm = doc.PrintManager;
                    pm.PrintRange = PrintRange.Select;
                    pm.Apply();
                    ViewSheetSetting setting = pm.ViewSheetSetting;
                    var viewSet = new ViewSet();
                    viewSet.Insert(sheet);
                    setting.CurrentViewSheetSet = setting.InSession;
                    setting.CurrentViewSheetSet.Views = viewSet;
                    Assert.IsTrue(setting.SaveAs(printSetName), $"Failed to save Print Set '{printSetName}'.");
                    tx.Commit();
                }
                catch
                {
                    if (tx.HasStarted()) tx.RollBack();
                    throw;
                }
            }
        }

        private static SharedParameterElement BindSheetSharedParamAndSetValue(
            Document doc,
            ExternalDefinition definition,
            string sheetNumber,
            string value)
        {
            using (var tx = new Transaction(doc, "Bind Shared Parameter and Set Sheet Value"))
            {
                tx.Start();
                try
                {
                    CategorySet catSet = doc.Application.Create.NewCategorySet();
                    Category sheetCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Sheets);
                    catSet.Insert(sheetCat);
                    InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);

#if REVIT2023 || REVIT2024
                    bool bound = doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_IDENTITY_DATA);
#else
                    bool bound = doc.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData);
#endif
                    Assert.IsTrue(bound, $"Expected parameter binding for '{definition.Name}' to succeed.");

                    ViewSheet sheet = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .First(s => string.Equals(s.SheetNumber, sheetNumber, StringComparison.Ordinal));
                    Parameter? param = sheet.LookupParameter(definition.Name);
                    Assert.IsNotNull(param, $"Expected sheet '{sheetNumber}' to expose bound parameter '{definition.Name}'.");
                    Assert.IsTrue(param!.Set(value), $"Failed to set '{definition.Name}' on sheet '{sheetNumber}'.");

                    SharedParameterElement? sharedElem = SharedParameterElement.Lookup(doc, definition.GUID);
                    Assert.IsNotNull(sharedElem, $"Expected SharedParameterElement for '{definition.Name}' ({definition.GUID}).");
                    tx.Commit();
                    return sharedElem!;
                }
                catch
                {
                    if (tx.HasStarted()) tx.RollBack();
                    throw;
                }
            }
        }

        private static void SeedCustomNamingPdfSetup(Document doc, string setupName, ElementId customParamId)
        {
            using (var tx = new Transaction(doc, "Seed Custom Naming PDF Setup"))
            {
                tx.Start();
                try
                {
                    TableCellCombinedParameterData sheetNumEntry = TableCellCombinedParameterData.Create();
                    sheetNumEntry.ParamId = new ElementId(BuiltInParameter.SHEET_NUMBER);
                    sheetNumEntry.CategoryId = new ElementId(BuiltInCategory.OST_Sheets);
                    sheetNumEntry.Separator = "_";

                    TableCellCombinedParameterData customEntry = TableCellCombinedParameterData.Create();
                    customEntry.ParamId = customParamId;
                    customEntry.CategoryId = new ElementId(BuiltInCategory.OST_Sheets);
                    customEntry.Separator = string.Empty;

                    var rule = new List<TableCellCombinedParameterData> { sheetNumEntry, customEntry };
                    Assert.IsTrue(PDFExportOptions.IsValidNamingRule(rule), "Custom naming rule should be valid.");

                    var opts = new PDFExportOptions
                    {
                        PaperFormat = ExportPaperFormat.Default,
                        Combine = false
                    };
                    opts.SetNamingRule(rule);
                    ExportPDFSettings.Create(doc, setupName, opts);
                    tx.Commit();
                }
                catch
                {
                    if (tx.HasStarted()) tx.RollBack();
                    throw;
                }
            }
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
