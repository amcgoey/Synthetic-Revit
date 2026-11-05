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
            // Z translation of +50.0 in link project location
            linkActiveLoc.MockTotalTransform = new Transform
            {
                Origin = new XYZ(10.0, 20.0, 50.0),
                BasisX = new XYZ(1, 0, 0),
                BasisY = new XYZ(0, 1, 0),
                BasisZ = new XYZ(0, 0, 1)
            };
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
            sharedLoc.MockTotalTransform = new Transform
            {
                Origin = new XYZ(50.0, 50.0, 10.0),
                BasisX = new XYZ(1, 0, 0),
                BasisY = new XYZ(0, 1, 0),
                BasisZ = new XYZ(0, 0, 1)
            };
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
            _doc.ActiveProjectLocation.MockTotalTransform = Transform.Identity;

            var linkDoc = new Document { Title = "TowerDoc" };
            var sharedLoc = new ProjectLocation { Name = "TowerSite" };
            // Shared site expects link at (30.0, 40.0, 10.0)
            sharedLoc.MockTotalTransform = new Transform
            {
                Origin = new XYZ(30.0, 40.0, 10.0),
                BasisX = new XYZ(1, 0, 0),
                BasisY = new XYZ(0, 1, 0),
                BasisZ = new XYZ(0, 0, 1)
            };
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

        #endregion
    }
}
