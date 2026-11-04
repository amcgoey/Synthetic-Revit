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
    public class SharedCoordinatesAdapterTests
    {
        private Document _doc = null!;
        private UIDocument _uidoc = null!;

        [SetUp]
        public void SetUp()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _uidoc = new UIDocument(_doc);

            // Setup active project location in doc
            var activeLoc = new ProjectLocation { Name = "Active Site" };
            activeLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(500.0, 1000.0, 0.0, 0.25));
            _doc.AddElement(activeLoc, new ElementId(101));
            _doc.ActiveProjectLocation = activeLoc;
        }

        [Test]
        public void GetActiveModelLevels_ReturnsAllLevelsSortedByElevation()
        {
            // Arrange
            var lvl2 = new Level { Name = "Level 2", Elevation = 20.0 };
            var lvl1 = new Level { Name = "Level 1", Elevation = 0.0 };
            var lvlB = new Level { Name = "Basement", Elevation = -10.0 };

            _doc.AddElement(lvl2, new ElementId(202));
            _doc.AddElement(lvl1, new ElementId(201));
            _doc.AddElement(lvlB, new ElementId(200));

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);

            // Act
            var levels = adapter.GetActiveModelLevels();

            // Assert
            Assert.AreEqual(3, levels.Count);
            Assert.AreEqual("Basement", levels[0].Name);
            Assert.AreEqual(-10.0, levels[0].Elevation);
            Assert.AreEqual("Level 1", levels[1].Name);
            Assert.AreEqual(0.0, levels[1].Elevation);
            Assert.AreEqual("Level 2", levels[2].Name);
            Assert.AreEqual(20.0, levels[2].Elevation);
        }

        [Test]
        public void GetExistingSharedSiteNames_ReturnsAllProjectLocationNames()
        {
            // Arrange
            var siteA = new ProjectLocation { Name = "Site A" };
            var siteB = new ProjectLocation { Name = "Site B" };

            _doc.AddElement(siteA, new ElementId(301));
            _doc.AddElement(siteB, new ElementId(302));

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);

            // Act
            var names = adapter.GetExistingSharedSiteNames();

            // Assert
            Assert.IsTrue(names.Contains("Active Site"));
            Assert.IsTrue(names.Contains("Site A"));
            Assert.IsTrue(names.Contains("Site B"));
        }

        [Test]
        public void SyncSharedSites_CreatesNewSitesAndUpdatesExistingSites()
        {
            // Arrange: Existing site "Floor 2" with custom coordinates
            var existingLoc = new ProjectLocation { Name = "Floor 2" };
            existingLoc.SetProjectPosition(XYZ.Zero, new ProjectPosition(123.0, 456.0, 10.0, 0.5));
            _doc.AddElement(existingLoc, new ElementId(401));

            var refDatumLevel = new LevelItem { Name = "Level 1", Elevation = 1.0 };

            var targets = new List<SharedSiteSyncTarget>
            {
                new SharedSiteSyncTarget
                {
                    LevelName = "Floor 2",
                    SharedElevation = 25.0,
                    SiteExists = true,
                    IsChecked = true
                },
                new SharedSiteSyncTarget
                {
                    LevelName = "Floor 3",
                    SharedElevation = 50.0,
                    SiteExists = false,
                    IsChecked = true
                },
                new SharedSiteSyncTarget
                {
                    LevelName = "Floor 4",
                    SharedElevation = 75.0,
                    SiteExists = false,
                    IsChecked = false // Unchecked -> should be skipped
                }
            };

            var request = new SyncSharedSitesRequest
            {
                LinkedModelId = "dummy-link",
                ReferenceDatumLevel = refDatumLevel,
                TargetSites = targets
            };

            var adapter = new RevitSharedCoordinatesAdapter(_doc, _uidoc);

            // Act
            var summary = adapter.SyncSharedSites(request);

            // Assert
            Assert.AreEqual(1, summary.UpdatedCount);
            Assert.AreEqual(1, summary.CreatedCount);
            Assert.Contains("Floor 2", (System.Collections.ICollection)summary.UpdatedSiteNames);
            Assert.Contains("Floor 3", (System.Collections.ICollection)summary.CreatedSiteNames);

            // Check updated site: Elevation = 25.0 - 1.0 = 24.0, EW/NS/Angle preserved
            var updatedPos = existingLoc.GetProjectPosition(XYZ.Zero);
            Assert.AreEqual(24.0, updatedPos.Elevation, 1e-6);
            Assert.AreEqual(123.0, updatedPos.EastWest, 1e-6);
            Assert.AreEqual(456.0, updatedPos.NorthSouth, 1e-6);
            Assert.AreEqual(0.5, updatedPos.Angle, 1e-6);

            // Check created site "Floor 3": Elevation = 50.0 - 1.0 = 49.0
            var allSites = adapter.GetExistingSharedSiteNames();
            Assert.IsTrue(allSites.Contains("Floor 3"));
            Assert.IsFalse(allSites.Contains("Floor 4")); // Unchecked site was not created
        }
    }
}
