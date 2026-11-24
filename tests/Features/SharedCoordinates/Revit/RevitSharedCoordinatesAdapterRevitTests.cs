using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Features.SharedCoordinates.Adapters;
using Synthetic.Features.SharedCoordinates.Models;
using Synthetic.Platform.Revit;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.SharedCoordinates
{
    [TestFixture]
    [RevitAdapterTest]
    public class RevitSharedCoordinatesAdapterRevitTests : RevitTemplateRollbackFixture
    {
        [Test]
        public void Constructor_NullDoc_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new RevitSharedCoordinatesAdapter(null!));
        }

        [Test]
        public void FromProjectPosition_PositiveNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Acute",
                    eastWest: 125.5,
                    northSouth: -42.25,
                    elevation: 37.75,
                    angleRadians: Math.PI / 6.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Positive_Obtuse",
                    eastWest: 45.0,
                    northSouth: 310.75,
                    elevation: 18.5,
                    angleRadians: 2.0 * Math.PI / 3.0);
            });
        }

        [Test]
        public void FromProjectPosition_NegativeNonZeroAngleAndOffsets_MatchesLiveProjectLocationGetTotalTransform()
        {
            WithRolledBackDocument(doc =>
            {
                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Acute",
                    eastWest: -80.25,
                    northSouth: 210.5,
                    elevation: -14.75,
                    angleRadians: -Math.PI / 3.0);

                VerifyProjectPositionParity(
                    doc,
                    siteName: "Parity_Negative_Obtuse",
                    eastWest: -240.5,
                    northSouth: -95.0,
                    elevation: 62.25,
                    angleRadians: -3.0 * Math.PI / 4.0);
            });
        }

        [Test]
        public void GetActiveModelLevels_ReturnsLevelsOrderedByElevation()
        {
            WithRolledBackDocument(doc =>
            {
                doc.ExecuteTransaction("Seed Levels", FailurePipelines.ObserveOnly(), () =>
                {
                    Level low = Level.Create(doc, -10.0);
                    low.Name = "Adapter_Basement";

                    Level high = Level.Create(doc, 30.0);
                    high.Name = "Adapter_Roof";

                    Level mid = Level.Create(doc, 5.0);
                    mid.Name = "Adapter_Mezzanine";
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var levels = adapter.GetActiveModelLevels();

                Assert.IsTrue(levels.Count >= 3);
                for (int i = 1; i < levels.Count; i++)
                {
                    Assert.IsTrue(levels[i].Elevation >= levels[i - 1].Elevation, "Levels must be ordered ascending by elevation.");
                }

                int basementIdx = IndexOfLevel(levels, "Adapter_Basement");
                int mezzIdx = IndexOfLevel(levels, "Adapter_Mezzanine");
                int roofIdx = IndexOfLevel(levels, "Adapter_Roof");
                Assert.IsTrue(basementIdx >= 0 && mezzIdx > basementIdx && roofIdx > mezzIdx);
            });
        }

        [Test]
        public void GetLinkedModelLevels_InvalidOrNonExistentId_ReturnsEmptyList()
        {
            Document doc = EnsureBaselineDocument();
            var adapter = new RevitSharedCoordinatesAdapter(doc);

            Assert.IsEmpty(adapter.GetLinkedModelLevels("not-a-number"));
            Assert.IsEmpty(adapter.GetLinkedModelLevels("99999999"));
        }

        [Test]
        public void SyncSharedSites_NullRequestOrMissingReferenceDatum_ThrowsExpectedException()
        {
            Document doc = EnsureBaselineDocument();
            var adapter = new RevitSharedCoordinatesAdapter(doc);

            Assert.Throws<ArgumentNullException>(() => adapter.SyncSharedSites(null!));
            Assert.Throws<ArgumentException>(() => adapter.SyncSharedSites(new SyncSharedSitesRequest { ReferenceDatumLevel = null! }));
        }

        [Test]
        public void SyncSharedSites_UpdatesExistingAndCreatesNewSites_PreservingEastWestNorthSouthAndAngle()
        {
            WithRolledBackDocument(doc =>
            {
                ProjectLocation? existingLoc = null;
                doc.ExecuteTransaction("Seed Existing Site", FailurePipelines.ObserveOnly(), () =>
                {
                    existingLoc = doc.ActiveProjectLocation.Duplicate("Level 1");
                    existingLoc.SetProjectPosition(XYZ.Zero, CreateProjectPosition(doc, 100.0, 200.0, 5.0, 0.25));
                });

                var adapter = new RevitSharedCoordinatesAdapter(doc);
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
                        new SharedSiteSyncTarget { LevelName = "Level 1", SharedElevation = 100.0, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Level 2", SharedElevation = 150.0, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Level 3", SharedElevation = 200.0, IsChecked = false }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.IsEmpty(summary.ErrorMessages);
                Assert.AreEqual(1, summary.UpdatedCount);
                Assert.AreEqual(1, summary.CreatedCount);
                CollectionAssert.Contains(summary.UpdatedSiteNames, "Level 1");
                CollectionAssert.Contains(summary.CreatedSiteNames, "Level 2");

                ProjectPosition updatedPos = existingLoc!.GetProjectPosition(XYZ.Zero);
                Assert.AreEqual(90.0, updatedPos.Elevation, 1e-6);
                Assert.AreEqual(100.0, updatedPos.EastWest, 1e-6);
                Assert.AreEqual(200.0, updatedPos.NorthSouth, 1e-6);
                Assert.AreEqual(0.25, updatedPos.Angle, 1e-6);

                var siteNames = adapter.GetExistingSharedSiteNames();
                CollectionAssert.Contains(siteNames, "Level 2");
                CollectionAssert.DoesNotContain(siteNames, "Level 3");
            });
        }

        [Test]
        public void SyncSharedSites_OneSiteFailsCommit_OtherSiteCommits_AssimilatesGroup()
        {
            WithRolledBackDocument(doc =>
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc);
                var request = new SyncSharedSitesRequest
                {
                    ReferenceDatumLevel = new LevelItem { Name = "Ref", Elevation = 10.0 },
                    TargetSites = new List<SharedSiteSyncTarget>
                    {
                        new SharedSiteSyncTarget { LevelName = "Failing_NaN_Site", SharedElevation = double.NaN, IsChecked = true },
                        new SharedSiteSyncTarget { LevelName = "Valid_Synced_Site", SharedElevation = 150.0, IsChecked = true }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.AreEqual(1, summary.CreatedCount);
                CollectionAssert.Contains(summary.CreatedSiteNames, "Valid_Synced_Site");
                CollectionAssert.DoesNotContain(summary.CreatedSiteNames, "Failing_NaN_Site");
                Assert.IsNotEmpty(summary.ErrorMessages, "Failing site creation error must be surfaced in ErrorMessages.");

                var siteNames = adapter.GetExistingSharedSiteNames();
                CollectionAssert.Contains(siteNames, "Valid_Synced_Site");
                CollectionAssert.DoesNotContain(siteNames, "Failing_NaN_Site");
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        [Test]
        public void SyncSharedSites_ZeroSitesCommit_RollsBackBatchGroup()
        {
            WithRolledBackDocument(doc =>
            {
                var adapter = new RevitSharedCoordinatesAdapter(doc);
                int siteCountBefore = adapter.GetExistingSharedSiteNames().Count;

                var request = new SyncSharedSitesRequest
                {
                    ReferenceDatumLevel = new LevelItem { Name = "Ref", Elevation = 10.0 },
                    TargetSites = new List<SharedSiteSyncTarget>
                    {
                        new SharedSiteSyncTarget { LevelName = "Failing_NaN_Only_Site", SharedElevation = double.NaN, IsChecked = true }
                    }
                };

                var summary = adapter.SyncSharedSites(request);

                Assert.AreEqual(0, summary.CreatedCount);
                Assert.AreEqual(0, summary.UpdatedCount);
                Assert.IsNotEmpty(summary.ErrorMessages);
                Assert.AreEqual(siteCountBefore, adapter.GetExistingSharedSiteNames().Count);
                Assert.IsFalse(doc.IsModifiable);
            });
        }

        private static int IndexOfLevel(IReadOnlyList<LevelItem> levels, string name)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (string.Equals(levels[i].Name, name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void VerifyProjectPositionParity(
            Document doc,
            string siteName,
            double eastWest,
            double northSouth,
            double elevation,
            double angleRadians)
        {
            ProjectLocation? createdLocation = null;

            doc.ExecuteTransaction($"Create {siteName}", FailurePipelines.ObserveOnly(), () =>
            {
                ProjectLocation activeLoc = doc.ActiveProjectLocation;
                Assert.IsNotNull(activeLoc, "Baseline document must expose ActiveProjectLocation.");

                createdLocation = activeLoc.Duplicate(siteName);
                ProjectPosition pos = CreateProjectPosition(doc, eastWest, northSouth, elevation, angleRadians);
                createdLocation.SetProjectPosition(XYZ.Zero, pos);
            });

            Assert.IsNotNull(createdLocation, $"Failed to create ProjectLocation '{siteName}'.");

            ProjectPosition actualPosition = createdLocation!.GetProjectPosition(XYZ.Zero);
            Transform actualTotal = createdLocation.GetTotalTransform();

            RigidTransform3D computedFromInputs = RigidTransform3D.FromProjectPosition(
                eastWest,
                northSouth,
                elevation,
                angleRadians);
            RigidTransform3D computedFromRevitPosition = RigidTransform3D.FromProjectPosition(
                actualPosition.EastWest,
                actualPosition.NorthSouth,
                actualPosition.Elevation,
                actualPosition.Angle);

            AssertTransformParity(computedFromInputs, actualTotal, 1e-6, $"{siteName} (input parameters)");
            AssertTransformParity(computedFromRevitPosition, actualTotal, 1e-6, $"{siteName} (GetProjectPosition(XYZ.Zero))");
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

        private static void AssertTransformParity(
            RigidTransform3D expected,
            Transform actual,
            double tolerance,
            string context)
        {
            Assert.AreEqual(expected.Origin.X, actual.Origin.X, tolerance, $"{context}: Origin.X mismatch");
            Assert.AreEqual(expected.Origin.Y, actual.Origin.Y, tolerance, $"{context}: Origin.Y mismatch");
            Assert.AreEqual(expected.Origin.Z, actual.Origin.Z, tolerance, $"{context}: Origin.Z mismatch");

            Assert.AreEqual(expected.BasisX.X, actual.BasisX.X, tolerance, $"{context}: BasisX.X mismatch");
            Assert.AreEqual(expected.BasisX.Y, actual.BasisX.Y, tolerance, $"{context}: BasisX.Y mismatch");
            Assert.AreEqual(expected.BasisX.Z, actual.BasisX.Z, tolerance, $"{context}: BasisX.Z mismatch");

            Assert.AreEqual(expected.BasisY.X, actual.BasisY.X, tolerance, $"{context}: BasisY.X mismatch");
            Assert.AreEqual(expected.BasisY.Y, actual.BasisY.Y, tolerance, $"{context}: BasisY.Y mismatch");
            Assert.AreEqual(expected.BasisY.Z, actual.BasisY.Z, tolerance, $"{context}: BasisY.Z mismatch");

            Assert.AreEqual(expected.BasisZ.X, actual.BasisZ.X, tolerance, $"{context}: BasisZ.X mismatch");
            Assert.AreEqual(expected.BasisZ.Y, actual.BasisZ.Y, tolerance, $"{context}: BasisZ.Y mismatch");
            Assert.AreEqual(expected.BasisZ.Z, actual.BasisZ.Z, tolerance, $"{context}: BasisZ.Z mismatch");
        }
    }
}
