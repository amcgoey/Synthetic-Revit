using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NUnit.Framework;
using Synthetic.Modules.SharedCoordinates.Adapters;
using Synthetic.Modules.SharedCoordinates.Models;
using Synthetic.Modules.SharedCoordinates.ViewModels;
using SyntheticTests.Helpers;

namespace SyntheticTests
{
    /// <summary>
    /// Tier 2 live Revit integration tests for Shared Coordinates module.
    /// Runs inside a real Revit session under RevitTestFramework.
    /// </summary>
    [TestFixture]
    public class Tier2_SharedCoordinatesIntegrationTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        #region Helper Methods

        private Document OpenTestTemplate(Autodesk.Revit.ApplicationServices.Application app)
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string testModelName = "TestTemplate" + app.VersionNumber + ".rvt";
            string modelPathStr = Path.Combine(projectRoot, "tests", "test_models", testModelName);
            if (!File.Exists(modelPathStr))
            {
                throw new FileNotFoundException("Test template model not found: " + modelPathStr);
            }

            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(modelPathStr);
            OpenOptions openOptions = new OpenOptions
            {
                DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
            };
            return app.OpenDocumentFile(modelPath, openOptions);
        }

        #endregion

        [Test]
        public void RevitSharedCoordinates_TypesAndServices_HydrateSuccessfully()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");
            Assert.IsNotNull(_uiapp!.Application, "Revit Application must not be null.");

            Assert.IsNotNull(typeof(RevitSharedCoordinatesAdapter));
            Assert.IsNotNull(typeof(SyncSharedSitesViewModel));
            Assert.IsNotNull(typeof(MoveLinksToSharedSitesViewModel));
            Assert.IsNotNull(typeof(SharedCoordinatesMath));
        }

        [Test]
        public void RevitSharedCoordinates_ActiveModelLevels_ReturnsAscendingOrderedLevels()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");

            Document? doc = _uiapp!.ActiveUIDocument?.Document;
            bool docOpenedHere = false;

            if (doc == null)
            {
                doc = OpenTestTemplate(_uiapp.Application);
                docOpenedHere = true;
            }

            try
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc, _uiapp.ActiveUIDocument);
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
            finally
            {
                if (docOpenedHere && doc != null)
                {
                    doc.Close(false);
                }
            }
        }

        [Test]
        public void RevitSharedCoordinates_GetExistingSharedSiteNames_ReturnsValidSiteNames()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");

            Document? doc = _uiapp!.ActiveUIDocument?.Document;
            bool docOpenedHere = false;

            if (doc == null)
            {
                doc = OpenTestTemplate(_uiapp.Application);
                docOpenedHere = true;
            }

            try
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc, _uiapp.ActiveUIDocument);
                var siteNames = adapter.GetExistingSharedSiteNames();

                Assert.IsNotNull(siteNames);
                Assert.IsTrue(siteNames.Count > 0, "Document must have at least one ProjectLocation site.");
            }
            finally
            {
                if (docOpenedHere && doc != null)
                {
                    doc.Close(false);
                }
            }
        }

        [Test]
        public void RevitSharedCoordinates_GetLoadedLinkedModels_ExecutesWithoutException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");

            Document? doc = _uiapp!.ActiveUIDocument?.Document;
            bool docOpenedHere = false;

            if (doc == null)
            {
                doc = OpenTestTemplate(_uiapp.Application);
                docOpenedHere = true;
            }

            try
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc, _uiapp.ActiveUIDocument);
                var links = adapter.GetLoadedLinkedModels();

                Assert.IsNotNull(links);
            }
            finally
            {
                if (docOpenedHere && doc != null)
                {
                    doc.Close(false);
                }
            }
        }

        [Test]
        public void RevitSharedCoordinates_GetLinkRepositionGroups_ExecutesWithoutException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");

            Document? doc = _uiapp!.ActiveUIDocument?.Document;
            bool docOpenedHere = false;

            if (doc == null)
            {
                doc = OpenTestTemplate(_uiapp.Application);
                docOpenedHere = true;
            }

            try
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc, _uiapp.ActiveUIDocument);
                var groups = adapter.GetLinkRepositionGroups();

                Assert.IsNotNull(groups);
            }
            finally
            {
                if (docOpenedHere && doc != null)
                {
                    doc.Close(false);
                }
            }
        }

        [Test]
        public void RevitSharedCoordinates_SyncSharedSites_CreatesAndUpdatesSitesInTransaction()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");

            Document doc = OpenTestTemplate(_uiapp!.Application);

            try
            {
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
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void RevitSharedCoordinates_RepositionLinks_MovesDisplacedPinnedInstanceToSharedSite()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context must not be null.");
            var app = _uiapp!.Application;

            string tempUnitPath = Path.Combine(Path.GetTempPath(), $"UnitModel_{Guid.NewGuid():N}.rvt");
            try
            {
                // 1. Create temporary .rvt unit model on disk with named ProjectLocation "Floor_3_Test"
                Document unitDoc = app.NewProjectDocument(UnitSystem.Imperial);
                unitDoc.SaveAs(tempUnitPath);

                using (var tx = new Transaction(unitDoc, "Create Shared Site"))
                {
                    tx.Start();
                    var activeLoc = unitDoc.ActiveProjectLocation;
                    var newLoc = activeLoc.Duplicate("Floor_3_Test");
#if REVIT2022 || REVIT2023
                    var newPos = app.Create.NewProjectPosition(10.0, 20.0, 35.0, 0.0);
#else
                    var newPos = new ProjectPosition(10.0, 20.0, 35.0, 0.0);
#endif
                    newLoc.SetProjectPosition(XYZ.Zero, newPos);
                    tx.Commit();
                }
                unitDoc.Save();
                unitDoc.Close(false);

                // 2. Open disposable host document from test template
                Document hostDoc = OpenTestTemplate(app);
                try
                {
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
                finally
                {
                    // Close hostDoc before attempting disk cleanup to release file lock on tempUnitPath
                    hostDoc.Close(false);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempUnitPath))
                    {
                        File.Delete(tempUnitPath);
                    }
                }
                catch { /* Suppress temporary cleanup locks */ }
            }
        }
    }
}
