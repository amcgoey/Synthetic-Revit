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

            Transform expected = SharedCoordinatesMath.ComputeExpectedTransform(
                hostDoc.ActiveProjectLocation.GetTotalTransform(),
                linkLoc.GetTotalTransform());
            Transform actual = linkInstance.GetTransform();

            Assert.AreEqual(expected.Origin.X, actual.Origin.X, 1e-3, "Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, 1e-3, "Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, 1e-3, "Origin.Z mismatch");
            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, 1e-3, "BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, 1e-3, "BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, 1e-3, "BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, 1e-3, "BasisY.Y mismatch");
        }
    }
}
