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
    }
}
