using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Transform = Autodesk.Revit.DB.Transform;
using View = Autodesk.Revit.DB.View;
using Synthetic.Modules.ShadowStudy.Adapters;
using Synthetic.Modules.ShadowStudy.Models;
using Synthetic.Modules.ShadowStudy.Services;
using Synthetic.Modules.ShadowStudy.ViewModels;
using SyntheticTests.Helpers;

namespace SyntheticTests.Modules.ShadowStudy
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Ticket #151 (Multi-view Export Range and Selected Views batch export).
    /// Runs inside real Revit 2023–2026 via ricaun.RevitTest.
    /// </summary>
    [TestFixture]
    public class Tier2_ShadowStudyMultiViewTests
    {
        private UIApplication? _uiapp;
        private string _tempDir = string.Empty;

        [OneTimeSetUp]
        public void OneTimeSetUp(UIApplication uiapp)
        {
            _uiapp = uiapp;
            TestWorkerJournalMarker.EnsureWritten(uiapp);
        }

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "Tier2ShadowMultiView_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
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
        public void GetEligibleViews_HarvestsAllShadowCapableFamiliesExcludesNonGraphicalAndFlagsUncropped()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                CreatedViewSuite suite = SetupMultiViewSuite(doc);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();

                Assert.IsTrue(eligible.Any(v => v.Id == suite.FloorPlan.UniqueId && v.ViewType == "FloorPlan"));
                Assert.IsTrue(eligible.Any(v => v.Id == suite.CeilingPlan.UniqueId && v.ViewType == "CeilingPlan"));
                Assert.IsTrue(eligible.Any(v => v.Id == suite.ThreeD.UniqueId && v.ViewType == "ThreeD"));
                Assert.IsTrue(eligible.Any(v => v.Id == suite.Section.UniqueId && v.ViewType == "Section"));
                Assert.IsTrue(eligible.Any(v => v.Id == suite.Elevation.UniqueId && v.ViewType == "Elevation"));
                Assert.IsTrue(eligible.Any(v => v.Id == suite.Detail.UniqueId && v.ViewType == "Detail"));

                if (suite.EngineeringPlan != null)
                {
                    Assert.IsTrue(eligible.Any(v => v.Id == suite.EngineeringPlan.UniqueId && v.ViewType == "EngineeringPlan"));
                }

                if (suite.AreaPlan != null)
                {
                    Assert.IsTrue(eligible.Any(v => v.Id == suite.AreaPlan.UniqueId && v.ViewType == "AreaPlan"));
                }

                // Excluded non-graphical and Walkthrough views
                Assert.IsFalse(eligible.Any(v => v.Id == suite.Sheet.UniqueId));
                Assert.IsFalse(eligible.Any(v => v.Id == suite.Schedule.UniqueId));
                if (suite.DraftingView != null)
                {
                    Assert.IsFalse(eligible.Any(v => v.Id == suite.DraftingView.UniqueId));
                }

                Assert.IsFalse(eligible.Any(v =>
                    v.ViewType == "DrawingSheet" ||
                    v.ViewType == "Schedule" ||
                    v.ViewType == "DraftingView" ||
                    v.ViewType == "Legend" ||
                    v.ViewType == "Walkthrough"));

                // Uncropped warning badge check
                ShadowStudyViewItem uncroppedItem = eligible.First(v => v.Id == suite.CeilingPlan.UniqueId);
                Assert.IsFalse(uncroppedItem.IsCropped);
                Assert.IsTrue(uncroppedItem.HasWarnings);
                Assert.IsTrue(uncroppedItem.Warnings.Any(w => w.Code == "Uncropped"));
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void GetSiteInfo_WhenActiveViewIsSheetOrSchedule_AutoSelectsSelectedViewsAndResolvesDstFallback()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                CreatedViewSuite suite = SetupMultiViewSuite(doc);

                bool expectedFallbackUsesDst = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .WhereElementIsNotElementType()
                    .Cast<View>()
                    .First(v => RevitShadowStudyAdapter.IsEligibleView(v) && v.SunAndShadowSettings != null)
                    .SunAndShadowSettings.UsesDST;

                foreach (View nonGraphicalActive in new View[] { suite.Sheet, suite.Schedule })
                {
                    Assert.IsNull(nonGraphicalActive.SunAndShadowSettings, $"Expected {nonGraphicalActive.ViewType} to have null SunAndShadowSettings.");

                    var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp, nonGraphicalActive);
                    Assert.IsNull(adapter.GetActiveViewInfo(), $"Expected {nonGraphicalActive.ViewType} to be ineligible as active view.");

                    ShadowStudySiteInfo site = adapter.GetSiteInfo();
                    Assert.AreEqual(expectedFallbackUsesDst, site.UsesDst, $"Expected UsesDst fallback resolution when active view is {nonGraphicalActive.ViewType}.");

                    var vm = new ShadowStudyViewModel(adapter, new NoOpFolderDialogService(), new DateTime(2026, 6, 21));
                    Assert.IsFalse(vm.IsCurrentViewEligible);
                    Assert.AreEqual(ShadowStudyExportRangeMode.SelectedViews, vm.ExportRangeMode);
                    Assert.IsTrue(vm.IsSelectedViewsMode);
                    Assert.GreaterOrEqual(vm.TotalEligibleViewsCount, 6);
                    Assert.IsFalse(vm.CanExport);
                }
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void BatchExport_MultiViewWithNameCollisionsTemplatesAndUncropped_ExportsPngsAndRollsBackCleanly()
        {
            Assert.IsNotNull(_uiapp);
            Document doc = OpenDetachedTestTemplate(_uiapp!.Application);

            try
            {
                CreatedViewSuite suite = SetupMultiViewSuite(doc);

                // Save to a clean temp .rvt so doc.IsModified starts as false before export
                string cleanRvtPath = Path.Combine(_tempDir, "CleanBaseline.rvt");
                doc.SaveAs(cleanRvtPath, new SaveAsOptions { OverwriteExistingFile = true });
                Assert.IsFalse(doc.IsModified);

                var adapter = new RevitShadowStudyAdapter(doc, null, _uiapp);
                ShadowStudySiteInfo site = adapter.GetSiteInfo();
                DateSolarSchedule schedule = SolarScheduleCalculator.BuildSchedule(
                    new ShadowStudyDateEntry { Date = new DateTime(2026, 6, 21) },
                    site,
                    stepMinutes: 180,
                    minAltitudeDegrees: 15.0,
                    maxCompositeDarknessPercent: 80);

                Assert.IsTrue(schedule.HasValidFrames);

                IReadOnlyList<ShadowStudyViewItem> eligible = adapter.GetEligibleViews();
                var batchViews = new List<View>
                {
                    suite.FloorPlan,      // Named "Shadow Study Level 1" (collides with CeilingPlan)
                    suite.CeilingPlan,    // Named "Shadow Study Level 1" (collides with FloorPlan, uncropped!)
                    suite.ThreeD,         // Unique name
                    suite.Section,        // Unique name
                    suite.Elevation,      // Unique name
                    suite.Detail          // Unique name
                };

                var selectedItems = batchViews
                    .Select(v => eligible.First(item => item.Id == v.UniqueId))
                    .ToList();

                int elementCountBefore = new FilteredElementCollector(doc).GetElementCount();
                List<ViewSnapshot> targetSnapshotsBefore = batchViews.Select(CaptureSnapshot).ToList();
                ViewSnapshot siblingSnapshotBefore = CaptureSnapshot(suite.SiblingTemplatedPlan);

                var pipeline = new ShadowStudyPipeline(adapter);
                ShadowStudyRunSummary summary = pipeline.Execute(new ShadowStudyRunRequest
                {
                    Views = selectedItems,
                    DateSchedules = new[] { schedule },
                    OutputFolder = _tempDir,
                    PixelWidth = 360,
                    ForceHiddenLine = true,
                    ForceWhiteBackground = true,
                    KeepIndividualFrames = false
                });

                Assert.IsFalse(summary.WasCancelled);
                Assert.AreEqual(batchViews.Count, summary.GeneratedCount);

                // Verify collision-only (<ViewType>) filenames
                string dateLabel = schedule.DateEntry.DateLabel;
                var expectedFileNames = new[]
                {
                    $"Shadow Study Level 1 (FloorPlan) - {dateLabel} - Composite.png",
                    $"Shadow Study Level 1 (CeilingPlan) - {dateLabel} - Composite.png",
                    $"Shadow Study 3D - {dateLabel} - Composite.png",
                    $"Shadow Study Section - {dateLabel} - Composite.png",
                    $"Shadow Study Elevation - {dateLabel} - Composite.png",
                    $"Shadow Study Detail - {dateLabel} - Composite.png"
                };

                foreach (string expectedFileName in expectedFileNames)
                {
                    string expectedPath = Path.Combine(_tempDir, expectedFileName);
                    Assert.IsTrue(File.Exists(expectedPath), $"Missing expected composite output: {expectedFileName}");
                    GetPngDimensions(expectedPath, out int w, out int h);
                    Assert.AreEqual(360, w, $"Expected 360px width for {expectedFileName}");
                    Assert.Greater(h, 0);
                }

                // Verify zero model / undo-state / view / sibling template / crop-box / scope-box mutations
                Assert.IsFalse(doc.IsModified, "Batch export must roll back cleanly without dirtying the document or Undo stack.");
                Assert.AreEqual(elementCountBefore, new FilteredElementCollector(doc).GetElementCount());
                for (int i = 0; i < batchViews.Count; i++)
                {
                    AssertSnapshotUnchanged(targetSnapshotsBefore[i], batchViews[i]);
                }
                AssertSnapshotUnchanged(siblingSnapshotBefore, suite.SiblingTemplatedPlan);
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

        private static CreatedViewSuite SetupMultiViewSuite(Document doc)
        {
            using (var tx = new Transaction(doc, "Setup Multi-View Suite"))
            {
                tx.Start();

                Level level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .First();

                Wall? existingWall = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .Cast<Wall>()
                    .FirstOrDefault();

                XYZ center = new XYZ(10, 7.5, 6);
                if (existingWall == null)
                {
                    WallType wallType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .First();

                    Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(20, 0, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                    Wall.Create(doc, Line.CreateBound(new XYZ(20, 0, 0), new XYZ(20, 15, 0)), wallType.Id, level.Id, 12.0, 0.0, false, false);
                }
                else
                {
                    BoundingBoxXYZ? box = existingWall.get_BoundingBox(null);
                    if (box != null)
                    {
                        center = (box.Min + box.Max) * 0.5;
                    }
                }

                var viewFamilyTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .ToList();

                ViewFamilyType floorType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.FloorPlan);
                ViewPlan floorPlan = ViewPlan.Create(doc, floorType.Id, level.Id);
                floorPlan.Name = "Shadow Study Level 1";
                floorPlan.CropBoxActive = true;

                ViewPlan siblingPlan = ViewPlan.Create(doc, floorType.Id, level.Id);
                siblingPlan.Name = "Sibling Templated Plan";
                siblingPlan.CropBoxActive = true;

                View template = floorPlan.CreateViewTemplate();
                template.Name = "Shadow Study Controlling Template";
                floorPlan.ViewTemplateId = template.Id;
                siblingPlan.ViewTemplateId = template.Id;

                ViewFamilyType ceilingType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.CeilingPlan);
                ViewPlan ceilingPlan = ViewPlan.Create(doc, ceilingType.Id, level.Id);
                ceilingPlan.Name = "Shadow Study Level 1"; // Deliberate name collision with floorPlan
                ceilingPlan.CropBoxActive = false;        // Deliberate uncropped view

                ViewFamilyType? structType = viewFamilyTypes.FirstOrDefault(vft => vft.ViewFamily == ViewFamily.StructuralPlan);
                ViewPlan? engineeringPlan = structType != null ? ViewPlan.Create(doc, structType.Id, level.Id) : null;
                if (engineeringPlan != null)
                {
                    engineeringPlan.Name = "Shadow Study Engineering";
                }

                ViewPlan? areaPlan = null;
                AreaScheme? areaScheme = new FilteredElementCollector(doc)
                    .OfClass(typeof(AreaScheme))
                    .Cast<AreaScheme>()
                    .FirstOrDefault();
                if (areaScheme != null)
                {
                    areaPlan = ViewPlan.CreateAreaPlan(doc, areaScheme.Id, level.Id);
                    areaPlan.Name = "Shadow Study Area";
                }

                ViewFamilyType threeDType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);
                View3D threeD = View3D.CreateIsometric(doc, threeDType.Id);
                threeD.Name = "Shadow Study 3D";
                threeD.CropBoxActive = true;

                ViewFamilyType sectionType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.Section);
                Transform sectionTransform = Transform.Identity;
                sectionTransform.Origin = center;
                sectionTransform.BasisX = XYZ.BasisX;
                sectionTransform.BasisY = XYZ.BasisZ;
                sectionTransform.BasisZ = -XYZ.BasisY;

                var sectionBox = new BoundingBoxXYZ
                {
                    Transform = sectionTransform,
                    Min = new XYZ(-20, -15, -15),
                    Max = new XYZ(20, 20, 15)
                };
                ViewSection section = ViewSection.CreateSection(doc, sectionType.Id, sectionBox);
                section.Name = "Shadow Study Section";

                ViewFamilyType elevType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.Elevation);
                ElevationMarker marker = ElevationMarker.CreateElevationMarker(doc, elevType.Id, new XYZ(10, -20, 0), 50);
                ViewSection elevation = marker.CreateElevation(doc, floorPlan.Id, 0);
                elevation.Name = "Shadow Study Elevation";

                ViewFamilyType detailType = viewFamilyTypes.First(vft => vft.ViewFamily == ViewFamily.Detail);
                var detailBox = new BoundingBoxXYZ
                {
                    Transform = sectionTransform,
                    Min = new XYZ(-12, -10, -10),
                    Max = new XYZ(12, 12, 10)
                };
                ViewSection detail = ViewSection.CreateDetail(doc, detailType.Id, detailBox);
                detail.Name = "Shadow Study Detail";

                ElementId? titleBlockId = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsElementType()
                    .FirstElementId();
                ViewSheet sheet = ViewSheet.Create(doc, titleBlockId ?? ElementId.InvalidElementId);
                sheet.Name = "Shadow Study Sheet";

                ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_Walls));
                schedule.Name = "Shadow Study Schedule";

                ViewFamilyType? draftingType = viewFamilyTypes.FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Drafting);
                ViewDrafting? draftingView = draftingType != null ? ViewDrafting.Create(doc, draftingType.Id) : null;
                if (draftingView != null)
                {
                    draftingView.Name = "Shadow Study Drafting";
                }

                tx.Commit();

                return new CreatedViewSuite
                {
                    FloorPlan = floorPlan,
                    SiblingTemplatedPlan = siblingPlan,
                    CeilingPlan = ceilingPlan,
                    EngineeringPlan = engineeringPlan,
                    AreaPlan = areaPlan,
                    ThreeD = threeD,
                    Section = section,
                    Elevation = elevation,
                    Detail = detail,
                    Sheet = sheet,
                    Schedule = schedule,
                    DraftingView = draftingView
                };
            }
        }

        private static ViewSnapshot CaptureSnapshot(View view)
        {
            SunAndShadowSettings sun = view.SunAndShadowSettings;
            Parameter? scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            return new ViewSnapshot
            {
                ViewTemplateId = view.ViewTemplateId,
                DisplayStyle = view.DisplayStyle,
                ShadowIntensity = view.ShadowIntensity,
                CropBoxActive = view.CropBoxActive,
                CropBoxMin = view.CropBox.Min,
                CropBoxMax = view.CropBox.Max,
                ScopeBoxId = scopeBoxParam != null ? scopeBoxParam.AsElementId() : ElementId.InvalidElementId,
                SharesSettings = sun.SharesSettings,
                SunPathVisible = sun.Visible,
                SunType = sun.SunAndShadowType,
                SunriseToSunset = sun.SunriseToSunset,
                StartDateAndTime = sun.StartDateAndTime
            };
        }

        private static void AssertSnapshotUnchanged(ViewSnapshot before, View view)
        {
            SunAndShadowSettings sun = view.SunAndShadowSettings;
            Parameter? scopeBoxParam = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            ElementId scopeBoxAfter = scopeBoxParam != null ? scopeBoxParam.AsElementId() : ElementId.InvalidElementId;

            Assert.AreEqual(before.ViewTemplateId, view.ViewTemplateId, $"ViewTemplateId changed on {view.Name}");
            Assert.AreEqual(before.DisplayStyle, view.DisplayStyle, $"DisplayStyle changed on {view.Name}");
            Assert.AreEqual(before.ShadowIntensity, view.ShadowIntensity, $"ShadowIntensity changed on {view.Name}");
            Assert.AreEqual(before.CropBoxActive, view.CropBoxActive, $"CropBoxActive changed on {view.Name}");
            Assert.IsTrue(before.CropBoxMin.IsAlmostEqualTo(view.CropBox.Min), $"CropBox.Min changed on {view.Name}");
            Assert.IsTrue(before.CropBoxMax.IsAlmostEqualTo(view.CropBox.Max), $"CropBox.Max changed on {view.Name}");
            Assert.AreEqual(before.ScopeBoxId, scopeBoxAfter, $"ScopeBox changed on {view.Name}");
            Assert.AreEqual(before.SharesSettings, sun.SharesSettings, $"SharesSettings changed on {view.Name}");
            Assert.AreEqual(before.SunPathVisible, sun.Visible, $"SunPath Visible changed on {view.Name}");
            Assert.AreEqual(before.SunType, sun.SunAndShadowType, $"SunAndShadowType changed on {view.Name}");
            Assert.AreEqual(before.SunriseToSunset, sun.SunriseToSunset, $"SunriseToSunset changed on {view.Name}");
            Assert.AreEqual(before.StartDateAndTime, sun.StartDateAndTime, $"StartDateAndTime changed on {view.Name}");
        }

        private static void GetPngDimensions(string path, out int width, out int height)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                width = decoder.Frames[0].PixelWidth;
                height = decoder.Frames[0].PixelHeight;
            }
        }

        private sealed class CreatedViewSuite
        {
            public ViewPlan FloorPlan { get; set; } = null!;
            public ViewPlan SiblingTemplatedPlan { get; set; } = null!;
            public ViewPlan CeilingPlan { get; set; } = null!;
            public ViewPlan? EngineeringPlan { get; set; }
            public ViewPlan? AreaPlan { get; set; }
            public View3D ThreeD { get; set; } = null!;
            public ViewSection Section { get; set; } = null!;
            public ViewSection Elevation { get; set; } = null!;
            public ViewSection Detail { get; set; } = null!;
            public ViewSheet Sheet { get; set; } = null!;
            public ViewSchedule Schedule { get; set; } = null!;
            public ViewDrafting? DraftingView { get; set; }
        }

        private sealed class ViewSnapshot
        {
            public ElementId ViewTemplateId { get; set; } = ElementId.InvalidElementId;
            public DisplayStyle DisplayStyle { get; set; }
            public int ShadowIntensity { get; set; }
            public bool CropBoxActive { get; set; }
            public XYZ CropBoxMin { get; set; } = XYZ.Zero;
            public XYZ CropBoxMax { get; set; } = XYZ.Zero;
            public ElementId ScopeBoxId { get; set; } = ElementId.InvalidElementId;
            public bool SharesSettings { get; set; }
            public bool SunPathVisible { get; set; }
            public SunAndShadowType SunType { get; set; }
            public bool SunriseToSunset { get; set; }
            public DateTime StartDateAndTime { get; set; }
        }

        private sealed class NoOpFolderDialogService : Synthetic.Shared.UI.IFolderDialogService
        {
            public string? SelectFolder(string description, string? initialDirectory = null) => null;
        }
    }
}
