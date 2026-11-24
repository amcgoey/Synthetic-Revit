using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Adapters;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Features.SharedCoordinates.ViewModels;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SharedCoordinates
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Shared Coordinates module.
    /// Runs inside a real Revit session under RevitTestFramework.
    /// </summary>
    [TestFixture]
    [RevitIsolatedDocTest]
    public class SharedCoordinatesRevitTests : RevitIsolatedDocumentFixture
    {
        [Test]
        public void RevitSharedCoordinates_TypesAndServices_HydrateSuccessfully()
        {
            Assert.IsNotNull(UiApp, "Revit UIApplication context must not be null.");
            Assert.IsNotNull(App, "Revit Application must not be null.");

            Assert.IsNotNull(typeof(RevitSharedCoordinatesAdapter));
            Assert.IsNotNull(typeof(SyncSharedSitesViewModel));
            Assert.IsNotNull(typeof(MoveLinksToSharedSitesViewModel));
            Assert.IsNotNull(typeof(SharedCoordinatesMath));
        }

        [Test]
        public void RevitSharedCoordinates_ActiveModelLevels_ReturnsAscendingOrderedLevels()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            var adapter = new RevitSharedCoordinatesAdapter(doc, UiApp.ActiveUIDocument);
            var levels = adapter.GetActiveModelLevels();

            Assert.IsNotNull(levels);
            Assert.IsTrue(levels.Count > 0, "Test template must contain at least one level.");

            for (int i = 1; i < levels.Count; i++)
            {
                Assert.IsTrue(
                    levels[i].Elevation >= levels[i - 1].Elevation,
                    $"Levels must be ordered by internal elevation ascending: {levels[i - 1].Name} ({levels[i - 1].Elevation}) vs {levels[i].Name} ({levels[i].Elevation})");
            }
        }

        [Test]
        public void RevitSharedCoordinates_GetExistingSharedSiteNames_ReturnsValidSiteNames()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            var adapter = new RevitSharedCoordinatesAdapter(doc, UiApp.ActiveUIDocument);
            var siteNames = adapter.GetExistingSharedSiteNames();

            Assert.IsNotNull(siteNames);
            Assert.IsTrue(siteNames.Count > 0, "Document must have at least one ProjectLocation site.");
        }

        [Test]
        public void RevitSharedCoordinates_GetLoadedLinkedModels_ExecutesWithoutException()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            var adapter = new RevitSharedCoordinatesAdapter(doc, UiApp.ActiveUIDocument);
            var links = adapter.GetLoadedLinkedModels();

            Assert.IsNotNull(links);
        }

        [Test]
        public void RevitSharedCoordinates_GetLinkRepositionGroups_ExecutesWithoutException()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            var adapter = new RevitSharedCoordinatesAdapter(doc, UiApp.ActiveUIDocument);
            var groups = adapter.GetLinkRepositionGroups();

            Assert.IsNotNull(groups);
        }

        [Test]
        public void RevitSharedCoordinates_SyncSharedSites_CreatesAndUpdatesSitesInTransaction()
        {
            Document doc = OpenIsolatedTestTemplateCopy();

            var adapter = new RevitSharedCoordinatesAdapter(doc, null);
            var activeLevels = adapter.GetActiveModelLevels();
            Assert.IsTrue(activeLevels.Count > 0, "Template must have active levels.");

            var refDatum = activeLevels[0];
            string testSiteName = "Integration_Test_Site_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var request = new SyncSharedSitesRequest
            {
                ReferenceDatumLevel = refDatum,
                TargetSites = new List<SharedSiteSyncTarget>
                {
                    new SharedSiteSyncTarget
                    {
                        LevelName = testSiteName,
                        SharedElevation = refDatum.Elevation + 25.0,
                        IsChecked = true
                    }
                }
            };

            var summary = adapter.SyncSharedSites(request);

            Assert.IsEmpty(summary.ErrorMessages, $"SyncSharedSites should not encounter errors: {string.Join("; ", summary.ErrorMessages)}");
            Assert.AreEqual(1, summary.CreatedCount);
            CollectionAssert.Contains(summary.CreatedSiteNames, testSiteName);

            var sitesAfter = adapter.GetExistingSharedSiteNames();
            CollectionAssert.Contains(sitesAfter, testSiteName);
        }

        [Test]
        public void RevitSharedCoordinates_RepositionLinks_MovesDisplacedPinnedInstanceToSharedSite()
        {
            string tempUnitPath = GetTempFilePath($"UnitModel_{Guid.NewGuid():N}.rvt");

            // 1. Create temporary .rvt unit model on disk with named ProjectLocation "Floor_3_Test"
            Document unitDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            unitDoc.SaveAs(tempUnitPath);

            using (var tx = new Transaction(unitDoc, "Create Shared Site"))
            {
                tx.Start();
                var activeLoc = unitDoc.ActiveProjectLocation;
                var newLoc = activeLoc.Duplicate("Floor_3_Test");
#if REVIT2023
                var newPos = App.Create.NewProjectPosition(10.0, 20.0, 35.0, 0.0);
#else
                var newPos = new ProjectPosition(10.0, 20.0, 35.0, 0.0);
#endif
                newLoc.SetProjectPosition(XYZ.Zero, newPos);
                tx.Commit();
            }
            unitDoc.Save();
            unitDoc.Close(false);

            // 2. Open disposable host document from test template
            Document hostDoc = OpenIsolatedTestTemplateCopy();

            RevitLinkInstance linkInstance;
            using (var tx = new Transaction(hostDoc, "Link Unit Model"))
            {
                tx.Start();
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(tempUnitPath);
                var linkOptions = new RevitLinkOptions(false);
                var loadResult = RevitLinkType.Create(hostDoc, modelPath, linkOptions);
                linkInstance = RevitLinkInstance.Create(hostDoc, loadResult.ElementId);

                string linkedDocName = Path.GetFileNameWithoutExtension(tempUnitPath);
                try
                {
                    linkInstance.Name = $"{linkedDocName} : 1 : location Floor_3_Test";
                }
                catch
                {
                    var nameParam = linkInstance.get_Parameter(BuiltInParameter.RVT_LINK_INSTANCE_NAME);
                    if (nameParam != null && !nameParam.IsReadOnly)
                    {
                        nameParam.Set("1 : location Floor_3_Test");
                    }
                }

                // Displace instance from true location:
                ElementTransformUtils.MoveElement(hostDoc, linkInstance.Id, new XYZ(15.0, -10.0, 0.0));
                linkInstance.Pinned = true;
                tx.Commit();
            }

            // 3. Act: Discover groups
            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var groups = adapter.GetLinkRepositionGroups();
            Assert.IsNotNull(groups);
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(1, groups[0].TotalInstanceCount);
            Assert.AreEqual(1, groups[0].OutOfSyncCount);
            Assert.AreEqual(0, groups[0].UnassignedCount);

            // 4. Act: Reposition links
            var repoRequest = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkInstance.GetTypeId().ToString() }
            };
            var summary = adapter.RepositionLinks(repoRequest);

            // 5. Assert: Summary & Pinned status
            Assert.IsEmpty(summary.ErrorMessages, $"RepositionLinks should not encounter errors: {string.Join("; ", summary.ErrorMessages)}");
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsTrue(linkInstance.Pinned, "Link instance should be pinned after repositioning.");

            // 6. Assert: Transform matches expected hostTotal * linkTotal^-1
            Document? linkedDoc = linkInstance.GetLinkDocument();
            Assert.IsNotNull(linkedDoc, "Linked document must be loaded.");
            var linkLoc = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(ProjectLocation))
                .Cast<ProjectLocation>()
                .FirstOrDefault(loc => string.Equals(loc.Name, "Floor_3_Test", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(linkLoc, "ProjectLocation 'Floor_3_Test' must exist in linked document.");

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(
                ToRigidTransform3D(hostDoc.ActiveProjectLocation.GetTotalTransform()),
                ToRigidTransform3D(linkLoc.GetTotalTransform()));
            Transform actual = linkInstance.GetTransform();

            Assert.AreEqual(expected.Origin.X, actual.Origin.X, 1e-3, "Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, 1e-3, "Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, 1e-3, "Origin.Z mismatch");
            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, 1e-3, "BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, 1e-3, "BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, 1e-3, "BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, 1e-3, "BasisY.Y mismatch");
        }

        [Test]
        public void RevitSharedCoordinates_GetLoadedLinkedModels_GroupsByTypeIdAndOmitsUnloaded()
        {
            string archPath = CreateSavedUnitModel("ArchModel.rvt");
            string structPath = CreateSavedUnitModel("StructModel.rvt");
            string unloadedPath = CreateSavedUnitModel("UnloadedModel.rvt");

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            RevitLinkType? unloadedType = null;

            hostDoc.ExecuteTransaction("Load Multiple Link Types", FailurePipelines.ObserveOnly(), () =>
            {
                ElementId archTypeId = LoadLinkType(hostDoc, archPath);
                RevitLinkInstance.Create(hostDoc, archTypeId);
                RevitLinkInstance.Create(hostDoc, archTypeId);

                ElementId structTypeId = LoadLinkType(hostDoc, structPath);
                RevitLinkInstance.Create(hostDoc, structTypeId);

                ElementId unloadedTypeId = LoadLinkType(hostDoc, unloadedPath);
                RevitLinkInstance.Create(hostDoc, unloadedTypeId);
                unloadedType = hostDoc.GetElement(unloadedTypeId) as RevitLinkType;
            });

            Assert.IsNotNull(unloadedType);
            unloadedType!.Unload(null);

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var loaded = adapter.GetLoadedLinkedModels();

            Assert.AreEqual(2, loaded.Count, "Loaded models must group multiple instances per type and omit unloaded links.");
            Assert.IsTrue(loaded.Any(m => m.Name.IndexOf("ArchModel", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsTrue(loaded.Any(m => m.Name.IndexOf("StructModel", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(loaded.Any(m => m.Name.IndexOf("UnloadedModel", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void RevitSharedCoordinates_GetLinkedModelLevels_CalculatesInternalAndSharedElevationsAccurately()
        {
            string unitPath = CreateSavedUnitModel(
                "SharedLevelsUnit.rvt",
                unitDoc =>
                {
                    unitDoc.ExecuteTransaction("Configure Shared Elevation And Levels", FailurePipelines.ObserveOnly(), () =>
                    {
                        ProjectLocation activeLoc = unitDoc.ActiveProjectLocation;
                        activeLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(unitDoc, 10.0, 20.0, 50.0, 0.0));

                        Level firstLevel = new FilteredElementCollector(unitDoc)
                            .OfClass(typeof(Level))
                            .Cast<Level>()
                            .OrderBy(l => l.Elevation)
                            .First();
                        firstLevel.Elevation = 0.0;
                        firstLevel.Name = "Unit_Lvl_0";

                        Level lvl15 = Level.Create(unitDoc, 15.0);
                        lvl15.Name = "Unit_Lvl_15";
                    });
                });

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            RevitLinkInstance linkInst = null!;
            hostDoc.ExecuteTransaction("Link Shared Levels Model", FailurePipelines.ObserveOnly(), () =>
            {
                ElementId linkTypeId = LoadLinkType(hostDoc, unitPath);
                linkInst = RevitLinkInstance.Create(hostDoc, linkTypeId);
            });

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var levels = adapter.GetLinkedModelLevels(linkInst.Id.ToString());

            LevelItem? lvl0 = levels.FirstOrDefault(l => l.Name == "Unit_Lvl_0");
            LevelItem? lvl15 = levels.FirstOrDefault(l => l.Name == "Unit_Lvl_15");
            Assert.IsNotNull(lvl0);
            Assert.IsNotNull(lvl15);
            Assert.AreEqual(0.0, lvl0!.Elevation, 1e-6);
            Assert.AreEqual(50.0, lvl0.SharedElevation, 1e-6);
            Assert.AreEqual(15.0, lvl15!.Elevation, 1e-6);
            Assert.AreEqual(65.0, lvl15.SharedElevation, 1e-6);
        }

        [Test]
        public void RevitSharedCoordinates_GetLinkRepositionGroups_DetectsOutOfSyncAndUnassignedInstances()
        {
            string unitPath = CreateSavedUnitModel(
                "GroupCheckUnit.rvt",
                unitDoc =>
                {
                    unitDoc.ExecuteTransaction("Add Shared Site", FailurePipelines.ObserveOnly(), () =>
                    {
                        ProjectLocation site = unitDoc.ActiveProjectLocation.Duplicate("SharedSite1");
                        site.SetProjectPosition(XYZ.Zero, CreateProjectPosition(unitDoc, 50.0, 50.0, 10.0, 0.0));
                    });
                });

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            hostDoc.ExecuteTransaction("Place Assigned And Unassigned Instances", FailurePipelines.ObserveOnly(), () =>
            {
                ElementId linkTypeId = LoadLinkType(hostDoc, unitPath);

                RevitLinkInstance assignedDisplaced = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(assignedDisplaced, "1 : location SharedSite1");

                RevitLinkInstance unassigned = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(unassigned, "2");
            });

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var groups = adapter.GetLinkRepositionGroups();

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(2, groups[0].TotalInstanceCount);
            Assert.AreEqual(1, groups[0].OutOfSyncCount);
            Assert.AreEqual(1, groups[0].UnassignedCount);
        }

        [Test]
        public void RevitSharedCoordinates_RepositionLinks_CombinedRotationAndTranslation_ReReadsPostRotationOriginAndRestoresPinned()
        {
            const double trueNorthAngle = Math.PI / 3.0;
            string unitPath = CreateSavedUnitModel(
                "CombinedTransformUnit.rvt",
                unitDoc =>
                {
                    unitDoc.ExecuteTransaction("Add Rotated And Offset Site", FailurePipelines.ObserveOnly(), () =>
                    {
                        ProjectLocation targetLoc = unitDoc.ActiveProjectLocation.Duplicate("Rotated_Site");
                        targetLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(unitDoc, 10.0, 20.0, 5.0, trueNorthAngle));
                    });
                });

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            RevitLinkInstance linkInstance = null!;
            hostDoc.ExecuteTransaction("Place Rotated And Displaced Link", FailurePipelines.ObserveOnly(), () =>
            {
                hostDoc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, CreateProjectPosition(hostDoc, 0.0, 0.0, 0.0, 0.0));

                ElementId linkTypeId = LoadLinkType(hostDoc, unitPath);
                linkInstance = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(linkInstance, "1 : location Rotated_Site");

                ElementTransformUtils.RotateElement(
                    hostDoc,
                    linkInstance.Id,
                    Line.CreateBound(XYZ.Zero, XYZ.BasisZ),
                    -0.25);
                ElementTransformUtils.MoveElement(hostDoc, linkInstance.Id, new XYZ(15.0, -10.0, 2.0));
                linkInstance.Pinned = true;
            });

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var request = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkInstance.GetTypeId().ToString() }
            };

            var summary = adapter.RepositionLinks(request);

            Assert.IsEmpty(summary.ErrorMessages, $"RepositionLinks errors: {string.Join("; ", summary.ErrorMessages)}");
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsTrue(linkInstance.Pinned, "Pinned state must be restored to true.");

            Document? linkedDoc = linkInstance.GetLinkDocument();
            Assert.IsNotNull(linkedDoc);
            ProjectLocation linkLoc = new FilteredElementCollector(linkedDoc)
                .OfClass(typeof(ProjectLocation))
                .Cast<ProjectLocation>()
                .First(loc => string.Equals(loc.Name, "Rotated_Site", StringComparison.OrdinalIgnoreCase));

            RigidTransform3D expected = SharedCoordinatesMath.ComputeExpectedTransform(
                ToRigidTransform3D(hostDoc.ActiveProjectLocation.GetTotalTransform()),
                ToRigidTransform3D(linkLoc.GetTotalTransform()));
            Transform actual = linkInstance.GetTransform();

            Assert.AreEqual(expected.Origin.X, actual.Origin.X, 1e-3, "Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, 1e-3, "Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, 1e-3, "Origin.Z mismatch");
            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, 1e-3, "BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, 1e-3, "BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, 1e-3, "BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, 1e-3, "BasisY.Y mismatch");

            var secondSummary = adapter.RepositionLinks(request);
            Assert.AreEqual(0, secondSummary.RepositionedCount);
            Assert.AreEqual(1, secondSummary.AlreadyAlignedCount);
        }

        [Test]
        public void RevitSharedCoordinates_RepositionLinks_SubShortCurveToleranceOffset_SkipsMoveElementAndRestoresPinned()
        {
            string unitPath = CreateSavedUnitModel(
                "SubToleranceUnit.rvt",
                unitDoc =>
                {
                    unitDoc.ExecuteTransaction("Add SubTolerance Site", FailurePipelines.ObserveOnly(), () =>
                    {
                        ProjectLocation targetLoc = unitDoc.ActiveProjectLocation.Duplicate("SubTolerance_Site");
                        targetLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(unitDoc, 0.0, 0.0, 0.0, 0.0));
                    });
                });

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            RevitLinkInstance linkInstance = null!;
            hostDoc.ExecuteTransaction("Place Link With Sub-ShortCurveTolerance Offset", FailurePipelines.ObserveOnly(), () =>
            {
                hostDoc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, CreateProjectPosition(hostDoc, 0.0, 0.0, 0.0, 0.0));

                ElementId linkTypeId = LoadLinkType(hostDoc, unitPath);
                linkInstance = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(linkInstance, "1 : location SubTolerance_Site");

                // 0.001 ft > SharedCoordinatesMath.Tolerance (1e-5), yet < Application.ShortCurveTolerance (~0.0026 ft)
                ElementTransformUtils.MoveElement(hostDoc, linkInstance.Id, new XYZ(0.001, 0.0, 0.0));
                linkInstance.Pinned = true;
            });

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var groups = adapter.GetLinkRepositionGroups();
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(1, groups[0].OutOfSyncCount);

            var summary = adapter.RepositionLinks(new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkInstance.GetTypeId().ToString() }
            });

            Assert.IsEmpty(summary.ErrorMessages, $"Sub-ShortCurveTolerance offset must not fail MoveElement: {string.Join("; ", summary.ErrorMessages)}");
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsTrue(linkInstance.Pinned, "Pinned state must be restored to true.");
        }

        [Test]
        public void RevitSharedCoordinates_EndToEnd_SyncSharedSitesAndRepositionLinks_MovesInstanceToPositiveSharedElevation()
        {
            string unitPath = GetTempFilePath($"E2E_UnitModel_{Guid.NewGuid():N}.rvt");

            // 1. Sync Level 3 (+50.0 shared elevation) inside unit model
            Document unitDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            unitDoc.SaveAs(unitPath);

            var unitAdapter = new RevitSharedCoordinatesAdapter(unitDoc, null);
            var activeUnitLevels = unitAdapter.GetActiveModelLevels();
            LevelItem datumLevel = activeUnitLevels.First();

            var syncSummary = unitAdapter.SyncSharedSites(new SyncSharedSitesRequest
            {
                ReferenceDatumLevel = datumLevel,
                TargetSites = new List<SharedSiteSyncTarget>
                {
                    new SharedSiteSyncTarget
                    {
                        LevelName = "Level 3",
                        SharedElevation = datumLevel.Elevation + 50.0,
                        IsChecked = true
                    }
                }
            });

            Assert.IsEmpty(syncSummary.ErrorMessages);
            Assert.AreEqual(1, syncSummary.CreatedCount);
            unitDoc.Save();
            unitDoc.Close(false);

            // 2. Link unit model into host at origin (0, 0, 0) and reposition to "Level 3"
            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            RevitLinkInstance linkInst = null!;
            hostDoc.ExecuteTransaction("Link Unit At Origin", FailurePipelines.ObserveOnly(), () =>
            {
                hostDoc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, CreateProjectPosition(hostDoc, 0.0, 0.0, 0.0, 0.0));
                ElementId linkTypeId = LoadLinkType(hostDoc, unitPath);
                linkInst = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(linkInst, "1 : location Level 3");
                linkInst.Pinned = true;
            });

            var hostAdapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var groups = hostAdapter.GetLinkRepositionGroups();
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(1, groups[0].OutOfSyncCount);

            var repoSummary = hostAdapter.RepositionLinks(new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkInst.GetTypeId().ToString() }
            });

            Assert.IsEmpty(repoSummary.ErrorMessages);
            Assert.AreEqual(1, repoSummary.RepositionedCount);
            Assert.IsTrue(linkInst.Pinned);

            Transform actual = linkInst.GetTransform();
            Assert.AreEqual(0.0, actual.Origin.X, 1e-3);
            Assert.AreEqual(0.0, actual.Origin.Y, 1e-3);
            Assert.AreEqual(50.0, actual.Origin.Z, 1e-3, "Link instance must move to +50.0 Z (not -50.0 Z).");
        }

        [Test]
        public void RevitSharedCoordinates_GetLinkRepositionGroups_MultiStepResolution_CorrectlyClassifiesInstances()
        {
            string singleSiteName = "Internal";
            string unitPath = CreateSavedUnitModel(
                "MultiStepUnit.rvt",
                unitDoc =>
                {
                    unitDoc.ExecuteTransaction("Configure Single Unit Site", FailurePipelines.ObserveOnly(), () =>
                    {
                        ProjectLocation activeLoc = unitDoc.ActiveProjectLocation;
                        Assert.IsNotNull(activeLoc);
                        activeLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(unitDoc, 25.0, 15.0, 5.0, 0.0));
                        singleSiteName = activeLoc.Name;
                    });
                    Assert.AreEqual(1, unitDoc.ProjectLocations.Size, "AcquireCoordinates requires a single ProjectLocation.");
                });

            Document hostDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            ElementId linkTypeId = ElementId.InvalidElementId;
            RevitLinkInstance inst1 = null!;

            // 1. Place Instance 1 at origin and acquire coordinates (GEO_LOCATION -> singleSiteName) without modifying the link in-memory.
            hostDoc.ExecuteTransaction("Acquire Coordinates For Instance 1", FailurePipelines.ObserveOnly(), () =>
            {
                linkTypeId = LoadLinkType(hostDoc, unitPath);
                inst1 = RevitLinkInstance.Create(hostDoc, linkTypeId);
                hostDoc.AcquireCoordinates(inst1.Id);
            });

            // Per ADR 023: updating the link's site on disk and reloading leaves inst1 at its prior placement -> OutOfSync with GEO_LOCATION assigned.
            RevitLinkType? linkType = hostDoc.GetElement(linkTypeId) as RevitLinkType;
            Assert.IsNotNull(linkType);
            linkType!.Unload(null);

            Document modifiedUnitDoc = App.OpenDocumentFile(unitPath);
            try
            {
                modifiedUnitDoc.ExecuteTransaction("Shift Unit Shared Site On Disk", FailurePipelines.ObserveOnly(), () =>
                {
                    modifiedUnitDoc.ActiveProjectLocation.SetProjectPosition(
                        XYZ.Zero,
                        CreateProjectPosition(modifiedUnitDoc, 100.0, 200.0, 50.0, 0.0));
                });
                modifiedUnitDoc.Save();
            }
            finally
            {
                if (modifiedUnitDoc.IsValidObject)
                {
                    modifiedUnitDoc.Close(false);
                }
            }

            LinkLoadResult reloadResult = linkType.Reload();
            Assert.AreEqual(LinkLoadResultType.LinkLoaded, reloadResult.LoadResult);

            // 2. Place Instance 2 (fallback " : location ") aligned to hostTotal * linkTotal^-1 and Instance 3 (unassigned).
            RevitLinkInstance inst2 = null!;
            RevitLinkInstance inst3 = null!;
            hostDoc.ExecuteTransaction("Place Fallback And Unassigned Instances", FailurePipelines.ObserveOnly(), () =>
            {
                inst2 = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(inst2, $"2 : location {singleSiteName}");

                Document? linkDoc2 = inst2.GetLinkDocument();
                Assert.IsNotNull(linkDoc2);
                ProjectLocation linkLoc2 = new FilteredElementCollector(linkDoc2)
                    .OfClass(typeof(ProjectLocation))
                    .Cast<ProjectLocation>()
                    .First(loc => string.Equals(loc.Name, singleSiteName, StringComparison.OrdinalIgnoreCase));

                RigidTransform3D expected2 = SharedCoordinatesMath.ComputeExpectedTransform(
                    ToRigidTransform3D(hostDoc.ActiveProjectLocation.GetTotalTransform()),
                    ToRigidTransform3D(linkLoc2.GetTotalTransform()));
                XYZ delta2 = new XYZ(expected2.Origin.X, expected2.Origin.Y, expected2.Origin.Z) - inst2.GetTransform().Origin;
                if (delta2.GetLength() > hostDoc.Application.ShortCurveTolerance)
                {
                    ElementTransformUtils.MoveElement(hostDoc, inst2.Id, delta2);
                }

                inst3 = RevitLinkInstance.Create(hostDoc, linkTypeId);
                SetLinkInstanceMark(inst3, "3");
            });

            var adapter = new RevitSharedCoordinatesAdapter(hostDoc, null);
            var groups = adapter.GetLinkRepositionGroups();

            Assert.AreEqual(1, groups.Count);
            var group = groups[0];
            Assert.AreEqual(3, group.TotalInstanceCount);
            Assert.AreEqual(1, group.OutOfSyncCount);
            Assert.AreEqual(1, group.UnassignedCount);

            var summary = adapter.RepositionLinks(new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { linkTypeId.ToString() }
            });

            Assert.IsFalse(
                summary.ErrorMessages.Any(m => !m.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase)),
                $"Unexpected RepositionLinks error(s): {string.Join("; ", summary.ErrorMessages)}");
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.AreEqual(1, summary.AlreadyAlignedCount);
            Assert.AreEqual(1, summary.SkippedCount);
            CollectionAssert.Contains(summary.RepositionedInstanceNames, inst1.Name);
            CollectionAssert.Contains(summary.SkippedInstanceNames, inst3.Name);
        }

        private string CreateSavedUnitModel(string fileNamePrefix, Action<Document>? configure = null)
        {
            string tempPath = GetTempFilePath($"{Path.GetFileNameWithoutExtension(fileNamePrefix)}_{Guid.NewGuid():N}.rvt");
            Document unitDoc = NewTrackedProjectDocument(UnitSystem.Imperial);
            unitDoc.SaveAs(tempPath);
            if (configure != null)
            {
                configure(unitDoc);
                unitDoc.Save();
            }
            unitDoc.Close(false);
            return tempPath;
        }

        private static ElementId LoadLinkType(Document hostDoc, string unitPath)
        {
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(unitPath);
            var loadResult = RevitLinkType.Create(hostDoc, modelPath, new RevitLinkOptions(false));
            return loadResult.ElementId;
        }

        private static void SetLinkInstanceMark(RevitLinkInstance linkInstance, string value)
        {
            Parameter? nameParam = linkInstance.get_Parameter(BuiltInParameter.RVT_LINK_INSTANCE_NAME);
            Assert.IsNotNull(nameParam, "Expected BuiltInParameter.RVT_LINK_INSTANCE_NAME.");
            Assert.IsFalse(nameParam!.IsReadOnly, "Expected RVT_LINK_INSTANCE_NAME to be writable.");
            nameParam.Set(value);
        }

        private static ProjectPosition CreateProjectPosition(
            Document doc,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
#if REVIT2023
            return doc.Application.Create.NewProjectPosition(eastWest, northSouth, elevation, angleRadians);
#else
            return new ProjectPosition(eastWest, northSouth, elevation, angleRadians);
#endif
        }

        private static RigidTransform3D ToRigidTransform3D(Transform transform)
        {
            return new RigidTransform3D(
                new Vector3D(transform.Origin.X, transform.Origin.Y, transform.Origin.Z),
                new Vector3D(transform.BasisX.X, transform.BasisX.Y, transform.BasisX.Z),
                new Vector3D(transform.BasisY.X, transform.BasisY.Y, transform.BasisY.Z),
                new Vector3D(transform.BasisZ.X, transform.BasisZ.Y, transform.BasisZ.Z));
        }
    }
}
