using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.Models;

namespace SyntheticTests.Modules.SharedCoordinates
{
    [TestFixture]
    public class RevitSharedCoordinatesAdapterTests
    {
        private Document _doc = null!;
        private RevitSharedCoordinatesAdapter _adapter = null!;

        [SetUp]
        public void SetUp()
        {
            _doc = new Document();
            _doc.ActiveProjectLocation = new ProjectLocation { Name = "Active Site" };
            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(100.0, 200.0, 0.0, 0.25));
            _doc.AddElement(_doc.ActiveProjectLocation, new ElementId(100));

            _adapter = new RevitSharedCoordinatesAdapter(_doc);
        }

        [Test]
        public void Constructor_NullDoc_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RevitSharedCoordinatesAdapter(null!));
        }

        #region Link Grouping by Type (SP-5)

        [Test]
        public void GetLoadedLinkedModels_GroupsByTypeId_ReturnsDistinctRepresentativePerLinkType()
        {
            var archType = new RevitLinkType { Name = "Arch Model Type" };
            var archTypeId = new ElementId(501);
            _doc.AddElement(archType, archTypeId);

            var structType = new RevitLinkType { Name = "Struct Model Type" };
            var structTypeId = new ElementId(502);
            _doc.AddElement(structType, structTypeId);

            var unloadedType = new RevitLinkType { Name = "Unloaded Model Type" };
            var unloadedTypeId = new ElementId(503);
            _doc.AddElement(unloadedType, unloadedTypeId);

            var archDoc = new Document { Title = "ArchDoc" };
            var structDoc = new Document { Title = "StructDoc" };

            // Multiple instances of Arch link
            var archInst1 = new RevitLinkInstance { Name = "Arch Instance 1", MockTypeId = archTypeId, MockLinkDocument = archDoc };
            var archInst2 = new RevitLinkInstance { Name = "Arch Instance 2", MockTypeId = archTypeId, MockLinkDocument = archDoc };
            var archInst3 = new RevitLinkInstance { Name = "Arch Instance 3", MockTypeId = archTypeId, MockLinkDocument = archDoc };
            _doc.AddElement(archInst1, new ElementId(1001));
            _doc.AddElement(archInst2, new ElementId(1002));
            _doc.AddElement(archInst3, new ElementId(1003));

            // Single instance of Struct link
            var structInst1 = new RevitLinkInstance { Name = "Struct Instance 1", MockTypeId = structTypeId, MockLinkDocument = structDoc };
            _doc.AddElement(structInst1, new ElementId(2001));

            // Unloaded instance (MockLinkDocument == null)
            var unloadedInst = new RevitLinkInstance { Name = "Unloaded Instance", MockTypeId = unloadedTypeId, MockLinkDocument = null };
            _doc.AddElement(unloadedInst, new ElementId(3001));

            var result = _adapter.GetLoadedLinkedModels();

            // Should have 2 entries (one for archType, one for structType; unloadedType omitted)
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Any(item => item.Name == "Arch Model Type"));
            Assert.IsTrue(result.Any(item => item.Name == "Struct Model Type"));
            Assert.IsFalse(result.Any(item => item.Name == "Unloaded Model Type"));
        }

        [Test]
        public void GetLoadedLinkedModels_WithSelection_PicksSelectedRepresentativeInstance()
        {
            var archType = new RevitLinkType { Name = "Arch Model" };
            var archTypeId = new ElementId(601);
            _doc.AddElement(archType, archTypeId);

            var archDoc = new Document();
            var archInst1 = new RevitLinkInstance { Name = "Arch Inst 1", MockTypeId = archTypeId, MockLinkDocument = archDoc };
            var archInst2 = new RevitLinkInstance { Name = "Arch Inst 2", MockTypeId = archTypeId, MockLinkDocument = archDoc };
            _doc.AddElement(archInst1, new ElementId(1101));
            _doc.AddElement(archInst2, new ElementId(1102));

            var uidoc = new UIDocument(_doc);
            uidoc.Selection.SetElementIds(new List<ElementId> { new ElementId(1102) });

            var adapterWithUi = new RevitSharedCoordinatesAdapter(_doc, uidoc);
            var result = adapterWithUi.GetLoadedLinkedModels();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("1102", result[0].Id);
            Assert.IsTrue(result[0].IsSelected);
        }

        #endregion

        #region O(1) Element Lookup & Internal Elevation (ST-2, SP-3)

        [Test]
        public void GetLinkedModelLevels_O1Lookup_CalculatesInternalAndSharedElevationsAccurately()
        {
            var linkType = new RevitLinkType { Name = "Link Type" };
            var linkTypeId = new ElementId(701);
            _doc.AddElement(linkType, linkTypeId);

            var linkDoc = new Document { Title = "Linked Doc" };
            var linkActiveLoc = new ProjectLocation { Name = "Shared Loc" };
            // Shared elevation = +50.0 in link project location
            linkActiveLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(10.0, 20.0, 50.0, 0.0));
            linkDoc.ActiveProjectLocation = linkActiveLoc;

            var lvl1 = new Level { Name = "Level 1", Elevation = 0.0 };
            var lvl2 = new Level { Name = "Level 2", Elevation = 15.0 };
            linkDoc.AddElement(lvl1, new ElementId(801));
            linkDoc.AddElement(lvl2, new ElementId(802));

            var linkInst = new RevitLinkInstance
            {
                Name = "Link Instance",
                MockTypeId = linkTypeId,
                MockLinkDocument = linkDoc
            };
            _doc.AddElement(linkInst, new ElementId(901));

            var levels = _adapter.GetLinkedModelLevels("901");

            Assert.AreEqual(2, levels.Count);
            Assert.AreEqual("Level 1", levels[0].Name);
            Assert.AreEqual(0.0, levels[0].Elevation, 1e-6);
            Assert.AreEqual(50.0, levels[0].SharedElevation, 1e-6);

            Assert.AreEqual("Level 2", levels[1].Name);
            Assert.AreEqual(15.0, levels[1].Elevation, 1e-6);
            Assert.AreEqual(65.0, levels[1].SharedElevation, 1e-6);
        }

        [Test]
        public void GetLinkedModelLevels_InvalidOrNonExistentId_ReturnsEmptyList()
        {
            var levelsInvalid = _adapter.GetLinkedModelLevels("not-a-number");
            Assert.IsEmpty(levelsInvalid);

            var levelsMissing = _adapter.GetLinkedModelLevels("999999");
            Assert.IsEmpty(levelsMissing);
        }

        [Test]
        public void GetActiveModelLevels_ReturnsLevelsOrderedByElevation()
        {
            var lvlHigh = new Level { Name = "Roof", Elevation = 30.0 };
            var lvlLow = new Level { Name = "Basement", Elevation = -10.0 };
            var lvlMid = new Level { Name = "First Floor", Elevation = 0.0 };

            _doc.AddElement(lvlHigh, new ElementId(101));
            _doc.AddElement(lvlLow, new ElementId(102));
            _doc.AddElement(lvlMid, new ElementId(103));

            var result = _adapter.GetActiveModelLevels();

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Basement", result[0].Name);
            Assert.AreEqual(-10.0, result[0].Elevation, 1e-6);
            Assert.AreEqual("First Floor", result[1].Name);
            Assert.AreEqual(0.0, result[1].Elevation, 1e-6);
            Assert.AreEqual("Roof", result[2].Name);
            Assert.AreEqual(30.0, result[2].Elevation, 1e-6);
        }

        #endregion

        #region Sync Shared Sites & Multi-Version ProjectPosition (SP-4)

        [Test]
        public void SyncSharedSites_UpdatesExistingAndCreatesNewSites()
        {
            var existingLoc = new ProjectLocation { Name = "Level 1" };
            existingLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(100.0, 200.0, 5.0, 0.25));
            _doc.AddElement(existingLoc, new ElementId(110));

            var request = new SyncSharedSitesRequest
            {
                ReferenceDatumLevel = new LevelItem
                {
                    Name = "Reference Datum",
                    Elevation = 10.0,
                    SharedElevation = 10.0
                },
                TargetSites = new List<SharedSiteSyncTarget>
                {
                    new SharedSiteSyncTarget
                    {
                        LevelName = "Level 1",
                        SharedElevation = 100.0,
                        IsChecked = true
                    },
                    new SharedSiteSyncTarget
                    {
                        LevelName = "Level 2",
                        SharedElevation = 150.0,
                        IsChecked = true
                    },
                    new SharedSiteSyncTarget
                    {
                        LevelName = "Level 3",
                        SharedElevation = 200.0,
                        IsChecked = false // Not checked -> skipped
                    }
                }
            };

            var summary = _adapter.SyncSharedSites(request);

            Assert.AreEqual(1, summary.UpdatedCount);
            Assert.AreEqual(1, summary.CreatedCount);
            CollectionAssert.Contains(summary.UpdatedSiteNames, "Level 1");
            CollectionAssert.Contains(summary.CreatedSiteNames, "Level 2");

            // Updated existing site: targetElevation = 100.0 - 10.0 = 90.0
            var updatedPos = existingLoc.GetProjectPosition(XYZ.Zero);
            Assert.AreEqual(90.0, updatedPos.Elevation, 1e-6);
            Assert.AreEqual(100.0, updatedPos.EastWest, 1e-6);
            Assert.AreEqual(200.0, updatedPos.NorthSouth, 1e-6);
            Assert.AreEqual(0.25, updatedPos.Angle, 1e-6);

            // New site "Level 2": targetElevation = 150.0 - 10.0 = 140.0
            var siteNames = _adapter.GetExistingSharedSiteNames();
            CollectionAssert.Contains(siteNames, "Level 2");
            CollectionAssert.DoesNotContain(siteNames, "Level 3");
        }

        [Test]
        public void SyncSharedSites_NullRequest_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _adapter.SyncSharedSites(null!));
        }

        [Test]
        public void SyncSharedSites_MissingReferenceDatum_ThrowsArgumentException()
        {
            var req = new SyncSharedSitesRequest { ReferenceDatumLevel = null };
            Assert.Throws<ArgumentException>(() => _adapter.SyncSharedSites(req));
        }

        #endregion

        #region Link Reposition Groups & Reposition Links (ST-3)

        [Test]
        public void GetLinkRepositionGroups_DetectsOutOfSyncAndUnassignedInstances()
        {
            var type1 = new RevitLinkType { Name = "Building A" };
            var typeId1 = new ElementId(1201);
            _doc.AddElement(type1, typeId1);

            var linkDoc = new Document { Title = "Doc A" };
            var sharedLoc = new ProjectLocation { Name = "SharedSite1" };
            sharedLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(50.0, 50.0, 10.0, 0.0));
            linkDoc.AddElement(sharedLoc, new ElementId(1301));

            // Instance 1: assigned, but displaced (out of sync)
            var inst1 = new RevitLinkInstance
            {
                Name = "Inst 1",
                MockTypeId = typeId1,
                MockLinkDocument = linkDoc,
                MockSiteName = "SharedSite1",
                MockTransform = new Transform { Origin = new XYZ(0.0, 0.0, 0.0) }
            };
            _doc.AddElement(inst1, new ElementId(1401));

            // Instance 2: unassigned (<Not Shared>)
            var inst2 = new RevitLinkInstance
            {
                Name = "Inst 2",
                MockTypeId = typeId1,
                MockLinkDocument = linkDoc,
                MockSiteName = "<Not Shared>",
                MockTransform = Transform.Identity
            };
            _doc.AddElement(inst2, new ElementId(1402));

            var groups = _adapter.GetLinkRepositionGroups();

            Assert.AreEqual(1, groups.Count);
            var grp = groups[0];
            Assert.AreEqual("Building A", grp.TypeName);
            Assert.AreEqual(2, grp.TotalInstanceCount);
            Assert.AreEqual(1, grp.OutOfSyncCount);
            Assert.AreEqual(1, grp.UnassignedCount);
        }

        [Test]
        public void RepositionLinks_MovesAndRotatesOutOfSyncLinks_PreservingPinnedState()
        {
            var type1 = new RevitLinkType { Name = "Tower" };
            var typeId1 = new ElementId(1501);
            _doc.AddElement(type1, typeId1);

            // Active doc transform is Identity (survey point at 0,0,0)
            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));

            var linkDoc = new Document { Title = "TowerDoc" };
            var sharedLoc = new ProjectLocation { Name = "TowerSite" };
            // Shared site expects link at (30.0, 40.0, 10.0)
            sharedLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(30.0, 40.0, 10.0, 0.0));
            linkDoc.AddElement(sharedLoc, new ElementId(1601));

            // Link is currently at (0, 0, 0) and pinned
            var inst = new RevitLinkInstance
            {
                Name = "Tower Instance 1",
                MockTypeId = typeId1,
                MockLinkDocument = linkDoc,
                MockSiteName = "TowerSite",
                Pinned = true,
                MockTransform = new Transform { Origin = new XYZ(0.0, 0.0, 0.0) }
            };
            _doc.AddElement(inst, new ElementId(1701));

            var request = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { "1501" }
            };

            var summary = _adapter.RepositionLinks(request);

            Assert.AreEqual(1, summary.RepositionedCount);
            CollectionAssert.Contains(summary.RepositionedInstanceNames, "Tower Instance 1");

            // Instance should be moved to (30, 40, 10)
            Assert.AreEqual(30.0, inst.MockTransform.Origin.X, 1e-4);
            Assert.AreEqual(40.0, inst.MockTransform.Origin.Y, 1e-4);
            Assert.AreEqual(10.0, inst.MockTransform.Origin.Z, 1e-4);

            // Pinned state must be restored to true
            Assert.IsTrue(inst.Pinned, "Pinned state must be restored to true after repositioning.");

            // Running reposition again on the aligned link -> should report already aligned
            var secondSummary = _adapter.RepositionLinks(request);
            Assert.AreEqual(0, secondSummary.RepositionedCount);
            Assert.AreEqual(1, secondSummary.AlreadyAlignedCount);
        }

        [Test]
        public void RepositionLinks_CombinedRotationAndTranslation_ReReadsPostRotationOriginAndCompensates()
        {
            var type = new RevitLinkType { Name = "BuildingType" };
            var typeId = new ElementId(5501);
            _doc.AddElement(type, typeId);

            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));

            var linkDoc = new Document { Title = "BuildingDoc" };
            var targetLoc = new ProjectLocation { Name = "TargetSite" };
            // Expected position: origin (10.0, 20.0, 5.0) and angle PI/2 (90 deg)
            targetLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(10.0, 20.0, 5.0, Math.PI / 2));
            linkDoc.AddElement(targetLoc, new ElementId(5601));

            var inst = new RevitLinkInstance
            {
                Name = "Building Instance 1",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockSiteName = "TargetSite",
                Pinned = true,
                MockTransform = new Transform { Origin = new XYZ(0.0, 0.0, 0.0) },
                // Simulate pivot-induced origin drift during RotateElement (e.g., origin shifts by (2.0, 3.0, 0.0))
                MockPostRotateDrift = new XYZ(2.0, 3.0, 0.0)
            };
            _doc.AddElement(inst, new ElementId(5701));

            var request = new RepositionLinksRequest { SelectedTypeIds = new List<string> { "5501" } };
            var summary = _adapter.RepositionLinks(request);

            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsEmpty(summary.ErrorMessages);
            Assert.IsTrue(inst.Pinned, "Pinned state must be restored.");

            // Expected placement transform has origin = (10, 20, 5)
            // Even though rotation shifted origin to (2, 3, 0), post-rotation re-read ensured remaining translation = (8, 17, 5),
            // leading to exact match at (10, 20, 5).
            Assert.AreEqual(10.0, inst.MockTransform.Origin.X, 1e-4);
            Assert.AreEqual(20.0, inst.MockTransform.Origin.Y, 1e-4);
            Assert.AreEqual(5.0, inst.MockTransform.Origin.Z, 1e-4);
        }

        [Test]
        public void RepositionLinks_SubShortCurveToleranceTranslationRemainder_SkipsMoveElementWithoutException()
        {
            var type = new RevitLinkType { Name = "SmallRemainderType" };
            var typeId = new ElementId(5801);
            _doc.AddElement(type, typeId);

            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));

            var linkDoc = new Document { Title = "SmallDoc" };
            var targetLoc = new ProjectLocation { Name = "SmallSite" };
            // Expected position: origin (0.0, 0.0, 0.0) and angle PI/4
            targetLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, Math.PI / 4));
            linkDoc.AddElement(targetLoc, new ElementId(5901));

            var inst = new RevitLinkInstance
            {
                Name = "Small Instance 1",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockSiteName = "SmallSite",
                Pinned = true,
                MockTransform = new Transform { Origin = new XYZ(0.0, 0.0, 0.0) },
                // Simulate tiny sub-ShortCurveTolerance drift (0.001 ft < 0.0026 ft)
                MockPostRotateDrift = new XYZ(0.001, 0.0, 0.0)
            };
            _doc.AddElement(inst, new ElementId(6001));

            var request = new RepositionLinksRequest { SelectedTypeIds = new List<string> { "5801" } };
            var summary = _adapter.RepositionLinks(request);

            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsEmpty(summary.ErrorMessages);
            Assert.IsTrue(inst.Pinned, "Pinned state must be restored.");
        }

        [Test]
        public void RepositionLinks_PureSubShortCurveToleranceOffset_SkipsMoveElementAndCountsAsRepositioned()
        {
            var type = new RevitLinkType { Name = "SubTolerancePureType" };
            var typeId = new ElementId(6101);
            _doc.AddElement(type, typeId);

            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));

            var linkDoc = new Document { Title = "SubDoc" };
            var targetLoc = new ProjectLocation { Name = "SubSite" };
            // Target is at (0, 0, 0)
            targetLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));
            linkDoc.AddElement(targetLoc, new ElementId(6201));

            // Instance is at (0.001, 0.0, 0.0) - out of sync because 0.001 > SharedCoordinatesMath.Tolerance (1e-5),
            // but remaining translation 0.001 is <= Application.ShortCurveTolerance (0.0026).
            var inst = new RevitLinkInstance
            {
                Name = "Sub Instance 1",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockSiteName = "SubSite",
                Pinned = true,
                MockTransform = new Transform { Origin = new XYZ(0.001, 0.0, 0.0) }
            };
            _doc.AddElement(inst, new ElementId(6301));

            var request = new RepositionLinksRequest { SelectedTypeIds = new List<string> { "6101" } };
            var summary = _adapter.RepositionLinks(request);

            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.IsEmpty(summary.ErrorMessages);
            Assert.IsTrue(inst.Pinned, "Pinned state must be restored.");
        }

        [Test]
        public void EndToEnd_RoundTrip_SyncSharedSitesAndRepositionLinks_MovesInstanceToPositiveSharedElevation()
        {
            // 1. Host document with active project location at internal origin (0, 0, 0)
            var hostDoc = new Document { Title = "HostDoc" };
            hostDoc.ActiveProjectLocation = new ProjectLocation { Name = "Host Active Site" };
            hostDoc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));
            hostDoc.AddElement(hostDoc.ActiveProjectLocation, new ElementId(2001));

            var linkType = new RevitLinkType { Name = "Unit A Type" };
            var linkTypeId = new ElementId(2101);
            hostDoc.AddElement(linkType, linkTypeId);

            // 2. Unit document with a reference datum level at internal elevation 0.0
            var unitDoc = new Document { Title = "UnitDoc" };
            var unitActiveLoc = new ProjectLocation { Name = "Default Site" };
            unitActiveLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));
            unitDoc.ActiveProjectLocation = unitActiveLoc;
            unitDoc.AddElement(unitActiveLoc, new ElementId(3001));

            var unitRefDatum = new Level { Name = "Level 1 Reference", Elevation = 0.0 };
            unitDoc.AddElement(unitRefDatum, new ElementId(3101));

            // Sync shared sites in unit model for host Level 3 at SharedElevation +50.0
            var unitAdapter = new RevitSharedCoordinatesAdapter(unitDoc);
            var syncRequest = new SyncSharedSitesRequest
            {
                ReferenceDatumLevel = new LevelItem
                {
                    Name = "Level 1 Reference",
                    Elevation = 0.0,
                    SharedElevation = 0.0
                },
                TargetSites = new List<SharedSiteSyncTarget>
                {
                    new SharedSiteSyncTarget
                    {
                        LevelName = "Level 3",
                        SharedElevation = 50.0,
                        IsChecked = true
                    }
                }
            };
            var syncSummary = unitAdapter.SyncSharedSites(syncRequest);
            Assert.AreEqual(1, syncSummary.CreatedCount);
            CollectionAssert.Contains(syncSummary.CreatedSiteNames, "Level 3");

            // 3. In host document: create link instance assigned to "Level 3"
            var linkInst = new RevitLinkInstance
            {
                Name = "Unit A Instance 1",
                MockTypeId = linkTypeId,
                MockLinkDocument = unitDoc,
                MockSiteName = "Level 3",
                Pinned = true,
                MockTransform = new Transform { Origin = new XYZ(0.0, 0.0, 0.0) }
            };
            hostDoc.AddElement(linkInst, new ElementId(2201));

            var hostAdapter = new RevitSharedCoordinatesAdapter(hostDoc);

            // 4. Verify GetLinkRepositionGroups flags it as out-of-sync
            var groups = hostAdapter.GetLinkRepositionGroups();
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(1, groups[0].OutOfSyncCount);

            // 5. Reposition links in host
            var repoRequest = new RepositionLinksRequest
            {
                SelectedTypeIds = new List<string> { "2101" }
            };
            var repoSummary = hostAdapter.RepositionLinks(repoRequest);

            Assert.AreEqual(1, repoSummary.RepositionedCount);
            Assert.IsEmpty(repoSummary.ErrorMessages);
            Assert.IsTrue(linkInst.Pinned, "Link instance should be pinned after repositioning.");

            // Crucial assertion: link instance was moved to +50.0 Z (NOT -50.0 Z)
            Assert.AreEqual(0.0, linkInst.MockTransform.Origin.X, 1e-4);
            Assert.AreEqual(0.0, linkInst.MockTransform.Origin.Y, 1e-4);
            Assert.AreEqual(50.0, linkInst.MockTransform.Origin.Z, 1e-4);
        }

        #endregion

        #region Assigned Site Resolution Tests (Ticket #131)

        [Test]
        public void GetAssignedSiteName_GeoLocationElementIdInLinkDoc_ResolvesSiteName()
        {
            var linkDoc = new Document { Title = "Linked Doc" };
            var siteLoc = new ProjectLocation { Name = "Floor 3" };
            var siteId = new ElementId(501);
            linkDoc.AddElement(siteLoc, siteId);

            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 1",
                MockLinkDocument = linkDoc,
                MockGeoLocationParameter = new Parameter
                {
                    StorageType = StorageType.ElementId,
                    ElementIdValue = siteId,
                    HasValue = true
                }
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.AreEqual("Floor 3", resolved);
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationElementIdInHostDoc_ResolvesSiteName()
        {
            var hostSiteLoc = new ProjectLocation { Name = "Floor 4 Host" };
            var hostSiteId = new ElementId(601);
            _doc.AddElement(hostSiteLoc, hostSiteId);

            var linkDoc = new Document { Title = "Linked Doc" };
            var inst = new RevitLinkInstance
            {
                Name = "Unit_B.rvt : 1",
                MockLinkDocument = linkDoc,
                MockGeoLocationParameter = new Parameter
                {
                    StorageType = StorageType.ElementId,
                    ElementIdValue = hostSiteId,
                    HasValue = true
                }
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.AreEqual("Floor 4 Host", resolved);
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationElementIdNotShared_FallsBackToInstanceNameLocation()
        {
            var notSharedLoc = new ProjectLocation { Name = "<Not Shared>" };
            var notSharedId = new ElementId(701);
            _doc.AddElement(notSharedLoc, notSharedId);

            var linkDoc = new Document { Title = "Linked Doc" };
            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 1 : location Floor 3",
                MockLinkDocument = linkDoc,
                MockGeoLocationParameter = new Parameter
                {
                    StorageType = StorageType.ElementId,
                    ElementIdValue = notSharedId,
                    HasValue = true
                }
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.AreEqual("Floor 3", resolved);
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationEmpty_InstanceNameHasLocationKeyword_ExtractsSiteName()
        {
            var linkDoc = new Document { Title = "Linked Doc" };
            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 1 : location Floor 3",
                MockLinkDocument = linkDoc,
                MockSiteName = null
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.AreEqual("Floor 3", resolved);
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationEmpty_TrailingSegmentMatchesLinkedProjectLocation_ResolvesSiteName()
        {
            var linkDoc = new Document { Title = "Linked Doc" };
            var siteLoc = new ProjectLocation { Name = "Floor 3" };
            linkDoc.AddElement(siteLoc, new ElementId(801));

            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 1 : Floor 3",
                MockLinkDocument = linkDoc,
                MockSiteName = null
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.AreEqual("Floor 3", resolved);
        }

        [Test]
        public void GetAssignedSiteName_GeoLocationEmpty_TrailingSegmentDoesNotMatchLinkedProjectLocation_ReturnsNull()
        {
            var linkDoc = new Document { Title = "Linked Doc" };
            var siteLoc = new ProjectLocation { Name = "Different Site" };
            linkDoc.AddElement(siteLoc, new ElementId(802));

            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 1 : Floor 3",
                MockLinkDocument = linkDoc,
                MockSiteName = null
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.IsNull(resolved);
        }

        [Test]
        public void GetAssignedSiteName_InstanceNameExplicitNotShared_ReturnsNull()
        {
            var linkDoc = new Document { Title = "Linked Doc" };
            var inst = new RevitLinkInstance
            {
                Name = "Unit_A.rvt : 2 : <Not Shared>",
                MockLinkDocument = linkDoc,
                MockSiteName = null
            };

            var resolved = _adapter.GetAssignedSiteName(inst, linkDoc);
            Assert.IsNull(resolved);
        }

        [Test]
        public void GetLinkRepositionGroups_MultiStepResolution_CorrectlyClassifiesInstances()
        {
            var linkType = new RevitLinkType { Name = "MultiStep Type" };
            var typeId = new ElementId(4001);
            _doc.AddElement(linkType, typeId);

            var linkDoc = new Document { Title = "MultiStep Doc" };
            var site1 = new ProjectLocation { Name = "Floor 1" };
            site1.SetProjectPosition(XYZ.Zero, new ProjectPosition(10.0, 0.0, 0.0, 0.0));
            var siteId1 = new ElementId(4101);
            linkDoc.AddElement(site1, siteId1);

            var site2 = new ProjectLocation { Name = "Floor 2" };
            site2.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));
            var siteId2 = new ElementId(4102);
            linkDoc.AddElement(site2, siteId2);

            _doc.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, new ProjectPosition(0.0, 0.0, 0.0, 0.0));

            // Instance 1: GEO_LOCATION ElementId -> "Floor 1", placed at origin -> Out of sync
            var inst1 = new RevitLinkInstance
            {
                Name = "Unit : 1",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockTransform = Transform.Identity,
                MockGeoLocationParameter = new Parameter
                {
                    StorageType = StorageType.ElementId,
                    ElementIdValue = siteId1,
                    HasValue = true
                }
            };
            _doc.AddElement(inst1, new ElementId(4201));

            // Instance 2: Name contains " : location Floor 2", placed at (0,0,0) -> In sync
            var inst2 = new RevitLinkInstance
            {
                Name = "Unit : 2 : location Floor 2",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockSiteName = null,
                MockTransform = Transform.Identity
            };
            _doc.AddElement(inst2, new ElementId(4202));

            // Instance 3: Name contains "<Not Shared>" -> Unassigned (skipped)
            var inst3 = new RevitLinkInstance
            {
                Name = "Unit : 3 : <Not Shared>",
                MockTypeId = typeId,
                MockLinkDocument = linkDoc,
                MockSiteName = null,
                MockTransform = Transform.Identity
            };
            _doc.AddElement(inst3, new ElementId(4203));

            var groups = _adapter.GetLinkRepositionGroups();
            Assert.AreEqual(1, groups.Count);
            var group = groups[0];
            Assert.AreEqual(3, group.TotalInstanceCount);
            Assert.AreEqual(1, group.OutOfSyncCount);
            Assert.AreEqual(1, group.UnassignedCount);

            var summary = _adapter.RepositionLinks(new RepositionLinksRequest { SelectedTypeIds = new List<string> { "4001" } });
            Assert.AreEqual(1, summary.RepositionedCount);
            Assert.AreEqual(1, summary.AlreadyAlignedCount);
            Assert.AreEqual(1, summary.SkippedCount);
            CollectionAssert.Contains(summary.RepositionedInstanceNames, "Unit : 1");
            CollectionAssert.Contains(summary.SkippedInstanceNames, "Unit : 3 : <Not Shared>");
        }

        #endregion
    }
}
