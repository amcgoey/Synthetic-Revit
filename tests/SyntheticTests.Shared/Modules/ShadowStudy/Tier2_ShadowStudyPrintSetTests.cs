using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.ViewModels;
using Synthetic.Shared.UI;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #152 (Document Print Set loading and mixed Print Set Save / Save As).
    /// Runs inside real Revit 2023–2026 via ricaun.RevitTest.
    /// </summary>
    [TestFixture]
    public class Tier2_ShadowStudyPrintSetTests
    {
        private UIApplication? _uiapp;
        private string _tempDir = string.Empty;
        private bool _previousSuppressUi;

        [OneTimeSetUp]
        public void OneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [SetUp]
        public void SetUp()
        {
            _previousSuppressUi = ProgressCoordinator.SuppressUI;
            ProgressCoordinator.SuppressUI = true;
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "Tier2ShadowPrintSet_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            ProgressCoordinator.SuppressUI = _previousSuppressUi;
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }

        [Test]
        public void GetPrintSets_ReadsMixedPrintSetWithoutMutatingDocumentOrPrintRange()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                MixedPrintSetSuite suite = SetupMixedPrintSetSuite(doc, "Mixed Architectural Set");

                string baselinePath = Path.Combine(_tempDir, "BaselineReadOnly.rvt");
                doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified);

                PrintRange printRangeBefore = doc.PrintManager.PrintRange;

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                IReadOnlyList<ShadowStudyPrintSetItem> printSets = adapter.GetPrintSets();

                ShadowStudyPrintSetItem mixed = printSets.FirstOrDefault(p => p.Name == "Mixed Architectural Set")
                    ?? throw new AssertionException("Expected saved Print Set 'Mixed Architectural Set' to be returned.");

                CollectionAssert.AreEquivalent(new[] { suite.PlanA.UniqueId }, mixed.ShadowViewIds);
                Assert.IsFalse(mixed.ShadowViewIds.Contains(suite.Sheet.UniqueId));
                Assert.IsFalse(mixed.ShadowViewIds.Contains(suite.Schedule.UniqueId));
                Assert.IsTrue(mixed.NonShadowMemberIds.Contains(suite.Sheet.UniqueId));
                Assert.IsTrue(mixed.NonShadowMemberIds.Contains(suite.Schedule.UniqueId));
                Assert.GreaterOrEqual(mixed.IgnoredNonShadowMemberCount, 2);

                Assert.IsFalse(doc.IsModified, "Inspecting Print Sets must not mutate the Revit document.");
                Assert.AreEqual(printRangeBefore, doc.PrintManager.PrintRange, "Inspecting Print Sets must not mutate PrintManager.PrintRange.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SavePrintSet_UpdatesExistingMixedSetAndPreservesNonShadowMembersInCommittedTransaction()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                MixedPrintSetSuite suite = SetupMixedPrintSetSuite(doc, "Mixed Update Set");

                string baselinePath = Path.Combine(_tempDir, "BaselineSave.rvt");
                doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudyPrintSetItem before = adapter.GetPrintSets().First(p => p.Name == "Mixed Update Set");

                ShadowStudyPrintSetItem updated = adapter.SavePrintSet(
                    "Mixed Update Set",
                    new[] { suite.PlanB.UniqueId, suite.ThreeD.UniqueId },
                    saveAsNew: false,
                    sourcePrintSetId: before.Id);

                Assert.IsTrue(doc.IsModified, "Explicit Save Print Set must commit an Undo-visible transaction.");
                CollectionAssert.AreEquivalent(new[] { suite.PlanB.UniqueId, suite.ThreeD.UniqueId }, updated.ShadowViewIds);
                Assert.IsTrue(updated.NonShadowMemberIds.Contains(suite.Sheet.UniqueId), "Existing ViewSheet member must remain intact.");
                Assert.IsTrue(updated.NonShadowMemberIds.Contains(suite.Schedule.UniqueId), "Existing ViewSchedule member must remain intact.");

                ViewSheetSet persisted = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheetSet))
                    .WhereElementIsNotElementType()
                    .Cast<ViewSheetSet>()
                    .First(s => s.Name == "Mixed Update Set");

                var persistedIds = new HashSet<string>(persisted.Views.Cast<View>().Select(v => v.UniqueId), StringComparer.Ordinal);
                Assert.IsFalse(persistedIds.Contains(suite.PlanA.UniqueId));
                Assert.IsTrue(persistedIds.Contains(suite.PlanB.UniqueId));
                Assert.IsTrue(persistedIds.Contains(suite.ThreeD.UniqueId));
                Assert.IsTrue(persistedIds.Contains(suite.Sheet.UniqueId));
                Assert.IsTrue(persistedIds.Contains(suite.Schedule.UniqueId));
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void SavePrintSet_SaveAsFromMixedSet_CreatesNewSetPreservingNonShadowMembersAndGuardsDuplicates()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                MixedPrintSetSuite suite = SetupMixedPrintSetSuite(doc, "Mixed Source Set");

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudyPrintSetItem source = adapter.GetPrintSets().First(p => p.Name == "Mixed Source Set");

                ShadowStudyPrintSetItem forked = adapter.SavePrintSet(
                    "Mixed Forked Set",
                    new[] { suite.ThreeD.UniqueId },
                    saveAsNew: true,
                    sourcePrintSetId: source.Id);

                Assert.AreEqual("Mixed Forked Set", forked.Name);
                Assert.AreNotEqual(source.Id, forked.Id);
                CollectionAssert.AreEquivalent(new[] { suite.ThreeD.UniqueId }, forked.ShadowViewIds);
                Assert.IsTrue(forked.NonShadowMemberIds.Contains(suite.Sheet.UniqueId));
                Assert.IsTrue(forked.NonShadowMemberIds.Contains(suite.Schedule.UniqueId));

                // Original set remains untouched
                ShadowStudyPrintSetItem sourceAfter = adapter.GetPrintSets().First(p => p.Id == source.Id);
                CollectionAssert.AreEquivalent(new[] { suite.PlanA.UniqueId }, sourceAfter.ShadowViewIds);
                Assert.IsTrue(sourceAfter.NonShadowMemberIds.Contains(suite.Sheet.UniqueId));

                // Duplicate Save As name throws InvalidOperationException
                Assert.Throws<InvalidOperationException>(() =>
                    adapter.SavePrintSet(
                        "mixed forked set",
                        new[] { suite.PlanA.UniqueId },
                        saveAsNew: true));
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void Export_AfterLoadingPrintSetAndModifyingSelectionWithoutSaving_LeavesDocumentUntouched()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                MixedPrintSetSuite suite = SetupMixedPrintSetSuite(doc, "Export Only Set");

                string baselinePath = Path.Combine(_tempDir, "BaselineExportOnly.rvt");
                doc.SaveAs(baselinePath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                var vm = new ShadowStudyViewModel(
                    adapter,
                    new NoOpFolderDialogService(),
                    new DateTime(2026, 6, 21),
                    textInputPromptService: new StubTextInputPromptService())
                {
                    ExportRangeMode = ShadowStudyExportRangeMode.SelectedViews,
                    OutputFolder = _tempDir,
                    PixelWidth = 320,
                    TimeStepMinutes = 120,
                    ExportIndividualFrames = false
                };

                vm.SelectedPrintSet = vm.PrintSets.First(p => p.Name == "Export Only Set");
                Assert.AreEqual(1, vm.SelectedViewsCount);

                // Modify selection without clicking Save / Save As
                vm.AllViews.First(v => v.ViewId == suite.PlanB.UniqueId).IsSelected = true;
                Assert.AreEqual(2, vm.SelectedViewsCount);

                ShadowStudyRunRequest request = vm.BuildRunRequest();
                request.KeepIndividualFrames = false;
                // Keep test fast by limiting to a single slot if multiple exist
                DateSolarSchedule fullSchedule = request.DateSchedules[0];
                if (fullSchedule.Frames.Count > 1)
                {
                    request.DateSchedules = new[]
                    {
                        new DateSolarSchedule
                        {
                            DateEntry = fullSchedule.DateEntry,
                            PeakSolarAltitudeDegrees = fullSchedule.PeakSolarAltitudeDegrees,
                            ShadowIntensity = fullSchedule.ShadowIntensity,
                            Frames = new[] { fullSchedule.Frames[fullSchedule.Frames.Count / 2] }
                        }
                    };
                }

                var pipeline = new ShadowStudyPipeline(adapter);
                ShadowStudyRunSummary summary = pipeline.Execute(request);

                Assert.IsFalse(summary.WasCancelled);
                Assert.AreEqual(2, summary.GeneratedCount);
                Assert.IsFalse(doc.IsModified, "Exporting without Save/Save As must leave the document and Undo stack completely untouched.");

                // Persisted Print Set in document still only contains PlanA + non-shadow members
                ShadowStudyPrintSetItem persisted = adapter.GetPrintSets().First(p => p.Name == "Export Only Set");
                CollectionAssert.AreEquivalent(new[] { suite.PlanA.UniqueId }, persisted.ShadowViewIds);
                Assert.IsTrue(persisted.NonShadowMemberIds.Contains(suite.Sheet.UniqueId));
            }
            finally
            {
                doc.Close(false);
            }
        }

        private static Document OpenDetachedTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            var openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        private static MixedPrintSetSuite SetupMixedPrintSetSuite(Document doc, string printSetName)
        {
            using (var tx = new Transaction(doc, "Setup Mixed Print Set Suite"))
            {
                tx.Start();

                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .First();

                if (!new FilteredElementCollector(doc).OfClass(typeof(Wall)).Any())
                {
                    WallType wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), wallType.Id, level.Id, 10.0, 0.0, false, false);
                }

                var viewFamilyTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .ToList();

                ViewFamilyType floorType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.FloorPlan);
                ViewPlan planA = ViewPlan.Create(doc, floorType.Id, level.Id);
                planA.Name = "PrintSet Plan A";
                planA.CropBoxActive = true;

                ViewPlan planB = ViewPlan.Create(doc, floorType.Id, level.Id);
                planB.Name = "PrintSet Plan B";
                planB.CropBoxActive = true;

                ViewFamilyType threeDType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);
                View3D threeD = View3D.CreateIsometric(doc, threeDType.Id);
                threeD.Name = "PrintSet 3D";
                threeD.CropBoxActive = true;

                ElementId? titleBlockId = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .FirstElementId();
                ViewSheet sheet = ViewSheet.Create(doc, titleBlockId ?? ElementId.InvalidElementId);
                sheet.Name = "PrintSet Sheet";

                ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Walls));
                schedule.Name = "PrintSet Schedule";

                // Create mixed ViewSheetSet containing PlanA + Sheet + Schedule
                var viewSet = new ViewSet();
                viewSet.Insert(planA);
                viewSet.Insert(sheet);
                viewSet.Insert(schedule);

                PrintManager printManager = doc.PrintManager;
                printManager.PrintRange = PrintRange.Select;
                printManager.Apply();
                ViewSheetSetting setting = printManager.ViewSheetSetting;
                setting.CurrentViewSheetSet = setting.InSession;
                setting.CurrentViewSheetSet.Views = viewSet;
                Assert.IsTrue(setting.SaveAs(printSetName), "Failed to seed test ViewSheetSet.");

                tx.Commit();

                return new MixedPrintSetSuite
                {
                    PlanA = planA,
                    PlanB = planB,
                    ThreeD = threeD,
                    Sheet = sheet,
                    Schedule = schedule
                };
            }
        }

        private sealed class MixedPrintSetSuite
        {
            public ViewPlan PlanA { get; set; } = null!;
            public ViewPlan PlanB { get; set; } = null!;
            public View3D ThreeD { get; set; } = null!;
            public ViewSheet Sheet { get; set; } = null!;
            public ViewSchedule Schedule { get; set; } = null!;
        }

        private sealed class NoOpFolderDialogService : IFolderDialogService
        {
            public string? SelectFolder(string description, string? initialDirectory = null) => null;
        }

        private sealed class StubTextInputPromptService : ITextInputPromptService
        {
            public string? PromptText(
                string title,
                string promptMessage,
                string? initialValue = null,
                Func<string, string?>? validator = null) => null;
        }
    }
}
